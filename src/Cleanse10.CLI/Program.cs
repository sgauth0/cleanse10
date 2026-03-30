using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cleanse10.Core.Bloat;
using Cleanse10.Core.Imaging;
using Cleanse10.Core.Presets;

namespace Cleanse10.CLI
{
    /// <summary>
    /// Cleanse10 command-line interface.
    ///
    /// Usage examples:
    ///   cleanse10 run --preset lite   --wim install.wim --mount C:\mnt --index 1
    ///   cleanse10 run --preset claw   --wim install.wim --mount C:\mnt
    ///   cleanse10 run --preset priv   --wim install.wim --mount C:\mnt --output clean.iso
    ///   cleanse10 run --preset ux     --wim install.wim --mount C:\mnt
    ///   cleanse10 info --wim install.wim
    ///   cleanse10 mount   --wim install.wim --index 1 --mount C:\mnt
    ///   cleanse10 unmount --mount C:\mnt [--discard]
    /// </summary>
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            // ── Root command ─────────────────────────────────────────────────
            var root = new RootCommand("Cleanse10 — Windows 10 image customization tool");

            // ── run ──────────────────────────────────────────────────────────
            var runCmd = new Command("run", "Apply a preset to a WIM/ESD image");

            var presetOpt = new Option<string>(
                "--preset", "Preset name: lite | claw | priv | ux") { IsRequired = true };
            var wimOpt = new Option<FileInfo>(
                "--wim", "Path to install.wim or install.esd") { IsRequired = true };
            var mountOpt = new Option<DirectoryInfo>(
                "--mount", "Empty directory to use as mount point") { IsRequired = true };
            var indexOpt  = new Option<int>("--index",  () => 1,  "WIM image index (default: 1)");
            var outputOpt = new Option<FileInfo?>("--output", "Output ISO path (optional)");
            var driversOpt = new Option<DirectoryInfo?>("--drivers", "Folder with .inf driver files to inject");
            var updatesOpt = new Option<DirectoryInfo?>("--updates", "Folder with .msu/.cab update packages");

            runCmd.AddOption(presetOpt);
            runCmd.AddOption(wimOpt);
            runCmd.AddOption(mountOpt);
            runCmd.AddOption(indexOpt);
            runCmd.AddOption(outputOpt);
            runCmd.AddOption(driversOpt);
            runCmd.AddOption(updatesOpt);

            runCmd.SetHandler(async (ctx) =>
            {
                string    presetStr = ctx.ParseResult.GetValueForOption(presetOpt)!;
                FileInfo  wimFile   = ctx.ParseResult.GetValueForOption(wimOpt)!;
                DirectoryInfo mountDir = ctx.ParseResult.GetValueForOption(mountOpt)!;
                int       index    = ctx.ParseResult.GetValueForOption(indexOpt);
                FileInfo? output   = ctx.ParseResult.GetValueForOption(outputOpt);
                DirectoryInfo? drivers = ctx.ParseResult.GetValueForOption(driversOpt);
                DirectoryInfo? updates = ctx.ParseResult.GetValueForOption(updatesOpt);
                var ct = ctx.GetCancellationToken();

                if (!ParsePreset(presetStr, out Preset10 preset))
                {
                    Console.Error.WriteLine($"Unknown preset '{presetStr}'. Valid values: lite, claw, priv, ux");
                    ctx.ExitCode = 1;
                    return;
                }

                if (!wimFile.Exists)
                {
                    Console.Error.WriteLine($"WIM not found: {wimFile.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }

                if (!mountDir.Exists)
                {
                    Console.Error.WriteLine($"Mount directory not found: {mountDir.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }

                var reporter = new Progress<string>(Console.WriteLine);

                try
                {
                    // 1. Mount
                    Console.WriteLine($"[CLI] Mounting index {index} from {wimFile.FullName}…");
                    var wim = new WimManager();
                    await wim.MountAsync(wimFile.FullName, index, mountDir.FullName, reporter, ct);

                    // 2. Run preset
                    Console.WriteLine($"[CLI] Running preset: {presetStr}");
                    var runner = new PresetRunner10(mountDir.FullName, preset)
                    {
                        DriverFolder = drivers?.FullName,
                        UpdateFolder = updates?.FullName,
                    };
                    await runner.RunAsync(reporter, ct);

                    // 3. Unmount + commit
                    Console.WriteLine("[CLI] Committing and unmounting…");
                    await wim.UnmountAsync(mountDir.FullName, commit: true, reporter, ct);

                    // 4. Optionally build ISO
                    if (output != null)
                    {
                        Console.WriteLine($"[CLI] Building ISO: {output.FullName}");
                        var builder = new IsoBuilder();
                        // The WIM lives at <iso_root>\sources\install.wim — walk up two levels
                        // to reach the ISO root directory that contains boot\, efi\, sources\, etc.
                        string? wimDir    = Path.GetDirectoryName(wimFile.FullName);
                        string  isoSource = (wimDir != null ? Path.GetDirectoryName(wimDir) : null)
                                            ?? Path.GetDirectoryName(wimFile.FullName)!;
                        await builder.BuildAsync(isoSource, output.FullName, reporter, ct);
                    }

                    Console.WriteLine("[CLI] Done.");
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("[CLI] Cancelled.");
                    // Best-effort discard unmount
                    try
                    {
                        var wim = new WimManager();
                        await wim.UnmountAsync(mountDir.FullName, commit: false, reporter, CancellationToken.None);
                    }
                    catch { }
                    ctx.ExitCode = 2;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[CLI] Error: {ex.Message}");
                    // Best-effort discard unmount so the WIM isn't left mounted on failure.
                    // Also unload any stale hives so the next run can discard cleanly.
                    try { await HiveManager.CleanupAllHivesAsync(CancellationToken.None); } catch { }
                    try
                    {
                        var wim2 = new WimManager();
                        await wim2.UnmountAsync(mountDir.FullName, commit: false, reporter, CancellationToken.None);
                    }
                    catch { }
                    ctx.ExitCode = 1;
                }
            });

            root.AddCommand(runCmd);

            // ── info ─────────────────────────────────────────────────────────
            var infoCmd = new Command("info", "List WIM image editions/indexes");
            var infoWimOpt = new Option<FileInfo>("--wim", "Path to WIM/ESD") { IsRequired = true };
            infoCmd.AddOption(infoWimOpt);

            infoCmd.SetHandler(async (ctx) =>
            {
                var wf = ctx.ParseResult.GetValueForOption(infoWimOpt)!;
                var ct = ctx.GetCancellationToken();
                if (!wf.Exists)
                {
                    Console.Error.WriteLine($"File not found: {wf.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }
                var wim  = new WimManager();
                var info = await wim.GetInfoAsync(wf.FullName, ct);
                Console.WriteLine($"Images in {wf.Name}:");
                foreach (var img in info)
                    Console.WriteLine($"  [{img.Index}] {img.Name}  ({img.Architecture})  {img.Description}");
            });

            root.AddCommand(infoCmd);

            // ── mount ────────────────────────────────────────────────────────
            var mountCmd  = new Command("mount", "Mount a WIM index to a folder");
            var mwOpt     = new Option<FileInfo>("--wim",   "WIM/ESD file") { IsRequired = true };
            var mmOpt     = new Option<DirectoryInfo>("--mount", "Empty mount folder") { IsRequired = true };
            var miOpt     = new Option<int>("--index", () => 1, "Image index");
            mountCmd.AddOption(mwOpt); mountCmd.AddOption(mmOpt); mountCmd.AddOption(miOpt);

            mountCmd.SetHandler(async (ctx) =>
            {
                var wf    = ctx.ParseResult.GetValueForOption(mwOpt)!;
                var mdir  = ctx.ParseResult.GetValueForOption(mmOpt)!;
                int idx   = ctx.ParseResult.GetValueForOption(miOpt);
                var ct    = ctx.GetCancellationToken();
                var reporter = new Progress<string>(Console.WriteLine);
                var wim = new WimManager();
                await wim.MountAsync(wf.FullName, idx, mdir.FullName, reporter, ct);
            });

            root.AddCommand(mountCmd);

            // ── unmount ──────────────────────────────────────────────────────
            var unmountCmd  = new Command("unmount", "Unmount a previously mounted WIM");
            var umOpt       = new Option<DirectoryInfo>("--mount", "Mount folder") { IsRequired = true };
            var discardOpt  = new Option<bool>("--discard", () => false, "Discard changes (default: commit)");
            unmountCmd.AddOption(umOpt); unmountCmd.AddOption(discardOpt);

            unmountCmd.SetHandler(async (ctx) =>
            {
                var mdir    = ctx.ParseResult.GetValueForOption(umOpt)!;
                bool discard = ctx.ParseResult.GetValueForOption(discardOpt);
                var ct      = ctx.GetCancellationToken();
                var reporter = new Progress<string>(Console.WriteLine);
                var wim = new WimManager();
                await wim.UnmountAsync(mdir.FullName, commit: !discard, reporter, ct);
            });

            root.AddCommand(unmountCmd);

            // ── presets list ─────────────────────────────────────────────────
            var presetsCmd = new Command("presets", "List all available presets");
            presetsCmd.SetHandler(() =>
            {
                Console.WriteLine("Available presets:");
                foreach (var def in PresetCatalog.All)
                    Console.WriteLine($"  {def.Name,-10}  {def.Tagline}");
            });
            root.AddCommand(presetsCmd);

            return await root.InvokeAsync(args);
        }

        private static bool ParsePreset(string s, out Preset10 preset)
        {
            preset = s.ToLowerInvariant() switch
            {
                "lite" => Preset10.Lite,
                "claw" => Preset10.Claw,
                "priv" => Preset10.Priv,
                "ux"   => Preset10.Ux,
                _      => (Preset10)(-1),
            };
            return (int)preset >= 0;
        }
    }
}
