using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cleanse10.Core.Bloat;
using Cleanse10.Core.Imaging;
using Cleanse10.Core.Media;
using Cleanse10.Core.Presets;
using Cleanse10.Core.Unattended;

namespace Cleanse10.CLI
{
    /// <summary>
    /// Cleanse10 command-line interface.
    ///
    /// Usage examples:
    ///   cleanse10 run --preset lite   --wim install.wim --mount C:\mnt --index 1
    ///   cleanse10 run --preset claw   --iso Win10.iso --index 6 --output claw.iso
    ///   cleanse10 run --preset priv   --wim install.wim --mount C:\mnt --output clean.iso
    ///   cleanse10 run --preset ux     --iso Win10.iso --index 6 --output ux.iso
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
            var runCmd = new Command("run", "Apply a preset to a WIM/ESD or ISO image");

            var presetOpt  = new Option<string>(
                "--preset", "Preset name: lite | claw | priv | ux") { IsRequired = true };
            var wimOpt     = new Option<FileInfo?>(
                "--wim", "Path to install.wim or install.esd (mutually exclusive with --iso)");
            var isoOpt     = new Option<FileInfo?>(
                "--iso", "Path to a Windows 10 ISO file (mutually exclusive with --wim)");
            var mountOpt   = new Option<DirectoryInfo?>(
                "--mount", "Empty directory to use as mount point (auto-created for --iso if omitted)");
            var indexOpt   = new Option<int>("--index",  () => 1,  "WIM image index (default: 1)");
            var outputOpt  = new Option<FileInfo?>("--output", "Output ISO path (required with --iso)");
            var driversOpt = new Option<DirectoryInfo?>("--drivers", "Folder with .inf driver files to inject");
            var updatesOpt = new Option<DirectoryInfo?>("--updates", "Folder with .msu/.cab update packages");
            var hostnameOpt      = new Option<string?>("--hostname",       "Computer name written into unattend.xml (omit for auto-generated)");
            var afkOpt           = new Option<bool>   ("--afk",            () => false, "Skip OOBE and accept EULA automatically (AFK install)");
            var adminUserOpt     = new Option<string?>("--admin-username",  "Local administrator account username (required with --afk)");
            var adminPassOpt     = new Option<string?>("--admin-password",  "Local administrator account password (used with --afk)");

            runCmd.AddOption(presetOpt);
            runCmd.AddOption(wimOpt);
            runCmd.AddOption(isoOpt);
            runCmd.AddOption(mountOpt);
            runCmd.AddOption(indexOpt);
            runCmd.AddOption(outputOpt);
            runCmd.AddOption(driversOpt);
            runCmd.AddOption(updatesOpt);
            runCmd.AddOption(hostnameOpt);
            runCmd.AddOption(afkOpt);
            runCmd.AddOption(adminUserOpt);
            runCmd.AddOption(adminPassOpt);

            runCmd.SetHandler(async (ctx) =>
            {
                string    presetStr = ctx.ParseResult.GetValueForOption(presetOpt)!;
                FileInfo? wimFile   = ctx.ParseResult.GetValueForOption(wimOpt);
                FileInfo? isoFile   = ctx.ParseResult.GetValueForOption(isoOpt);
                DirectoryInfo? mountDir = ctx.ParseResult.GetValueForOption(mountOpt);
                int       index    = ctx.ParseResult.GetValueForOption(indexOpt);
                FileInfo? output   = ctx.ParseResult.GetValueForOption(outputOpt);
                DirectoryInfo? drivers = ctx.ParseResult.GetValueForOption(driversOpt);
                DirectoryInfo? updates = ctx.ParseResult.GetValueForOption(updatesOpt);
                string?  hostname   = ctx.ParseResult.GetValueForOption(hostnameOpt);
                bool     afk        = ctx.ParseResult.GetValueForOption(afkOpt);
                string?  adminUser  = ctx.ParseResult.GetValueForOption(adminUserOpt);
                string?  adminPass  = ctx.ParseResult.GetValueForOption(adminPassOpt);
                var ct = ctx.GetCancellationToken();

                // ── Input validation ───────────────────────────────────────
                if (!ParsePreset(presetStr, out Preset10 preset))
                {
                    Console.Error.WriteLine($"Unknown preset '{presetStr}'. Valid values: lite, claw, priv, ux");
                    ctx.ExitCode = 1;
                    return;
                }

                bool isIsoInput = isoFile != null;

                if (wimFile == null && isoFile == null)
                {
                    Console.Error.WriteLine("Either --wim or --iso must be specified.");
                    ctx.ExitCode = 1;
                    return;
                }

                if (wimFile != null && isoFile != null)
                {
                    Console.Error.WriteLine("--wim and --iso are mutually exclusive. Specify one or the other.");
                    ctx.ExitCode = 1;
                    return;
                }

                if (isIsoInput && output == null)
                {
                    Console.Error.WriteLine("--output is required when using --iso.");
                    ctx.ExitCode = 1;
                    return;
                }

                if (!isIsoInput && wimFile != null && !wimFile.Exists)
                {
                    Console.Error.WriteLine($"WIM not found: {wimFile.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }

                if (isIsoInput && !isoFile!.Exists)
                {
                    Console.Error.WriteLine($"ISO not found: {isoFile!.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }

                if (!isIsoInput && mountDir == null)
                {
                    Console.Error.WriteLine("--mount is required when using --wim.");
                    ctx.ExitCode = 1;
                    return;
                }

                if (mountDir != null && !mountDir.Exists)
                {
                    Console.Error.WriteLine($"Mount directory not found: {mountDir.FullName}");
                    ctx.ExitCode = 1;
                    return;
                }

                var reporter = new Progress<string>(Console.WriteLine);

                if (isIsoInput)
                {
                    await RunIsoToIsoAsync(ctx, isoFile!, preset, presetStr, index,
                        output!, mountDir, drivers, updates, hostname, afk,
                        adminUser, adminPass, reporter, ct);
                }
                else
                {
                    await RunWimAsync(ctx, wimFile!, preset, presetStr, index,
                        output, mountDir!, drivers, updates, hostname, afk,
                        adminUser, adminPass, reporter, ct);
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

        // ──────────────────────────────────────────────────────────────────────
        // ── ISO-to-ISO pipeline ───────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Full ISO-to-ISO pipeline: extract WIM → export edition → mount →
        /// apply preset → unmount → stage ISO contents → build output ISO.
        /// </summary>
        private static async Task RunIsoToIsoAsync(
            InvocationContext ctx,
            FileInfo isoFile,
            Preset10 preset,
            string presetStr,
            int index,
            FileInfo output,
            DirectoryInfo? mountDir,
            DirectoryInfo? drivers,
            DirectoryInfo? updates,
            string? hostname,
            bool afk,
            string? adminUser,
            string? adminPass,
            IProgress<string> reporter,
            CancellationToken ct)
        {
            string? tempWimDir  = null;
            string? stageDir    = null;
            string? tempMount   = null;
            bool autoMount      = mountDir == null;

            try
            {
                // 0. Ensure oscdimg.exe before doing heavy I/O.
                Console.WriteLine("[CLI] Checking for oscdimg.exe…");
                string oscdimgPath = await IsoBuilder.EnsureOscdimgAsync(reporter, ct);

                // 1. Find install image inside the ISO.
                Console.WriteLine("[CLI] Finding install image inside ISO…");
                string installRelPath = await Task.Run(() =>
                    IsoReader.FindInstallImage(isoFile.FullName));
                Console.WriteLine($"[CLI] Found: {installRelPath}");

                // 2. Extract install.wim/esd to a temp directory.
                Console.WriteLine("[CLI] Extracting install image from ISO…");
                tempWimDir = Path.Combine(Path.GetTempPath(), $"cleanse10_cli_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempWimDir);
                string extractedWim = Path.Combine(tempWimDir, Path.GetFileName(installRelPath));

                await IsoReader.ExtractFileAsync(
                    isoFile.FullName, installRelPath, extractedWim,
                    reporter, byteProgress: null, ct);

                long extractedSizeMb = new FileInfo(extractedWim).Length / 1048576;
                Console.WriteLine($"[CLI] Extracted: {extractedWim} ({extractedSizeMb} MB)");

                // 3. Export the selected edition into a writable WIM.
                Console.WriteLine($"[CLI] Exporting index {index}…");
                string exportedWim = Path.Combine(tempWimDir, "exported.wim");
                var exportMgr = new WimManager();
                await exportMgr.ExportImageToWimAsync(extractedWim, index, exportedWim, reporter, ct);

                long exportedSizeMb = new FileInfo(exportedWim).Length / 1048576;
                Console.WriteLine($"[CLI] Exported: {exportedWim} ({exportedSizeMb} MB)");

                // Delete the multi-edition source WIM to reclaim disk space.
                try { File.Delete(extractedWim); } catch { /* best effort */ }

                // 4. Mount — the exported WIM always has index 1.
                if (autoMount)
                {
                    tempMount = Path.Combine(Path.GetTempPath(), $"cleanse10_mnt_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(tempMount);
                }
                else
                {
                    tempMount = mountDir!.FullName;
                }

                Console.WriteLine($"[CLI] Mounting index 1 at {tempMount}…");
                var wim = new WimManager();
                await wim.MountAsync(exportedWim, 1, tempMount, reporter, ct);

                // 5. Run preset.
                Console.WriteLine($"[CLI] Running preset: {presetStr}");

                bool needsUnattend = afk || !string.IsNullOrWhiteSpace(hostname);
                var unattendedCfg = needsUnattend
                    ? new UnattendedConfig
                    {
                        ComputerName     = string.IsNullOrWhiteSpace(hostname) ? "*" : hostname,
                        SkipOOBE         = afk,
                        AcceptEula       = afk,
                        HideEulaPage     = afk,
                        HideWirelessPage = afk,
                        AdminUsername    = afk ? adminUser : null,
                        AdminPassword    = afk ? adminPass : null,
                        WimIndex         = 1,
                    }
                    : null;

                var runner = new PresetRunner10(tempMount, preset)
                {
                    DriverFolder     = drivers?.FullName,
                    UpdateFolder     = updates?.FullName,
                    UnattendedConfig = unattendedCfg,
                };
                await runner.RunAsync(reporter, ct);

                // 6. Unmount + commit.
                Console.WriteLine("[CLI] Committing and unmounting…");
                await wim.UnmountAsync(tempMount, commit: true, reporter, ct);

                // 7. Stage ISO contents (everything except install.wim/esd).
                Console.WriteLine("[CLI] Staging ISO contents…");
                stageDir = Path.Combine(Path.GetTempPath(), $"cleanse10_stage_{Guid.NewGuid():N}");
                await IsoReader.StageContentsAsync(isoFile.FullName, stageDir, reporter, ct);

                // Copy the serviced WIM into the staged tree.
                string stagedSources = Path.Combine(stageDir, "sources");
                Directory.CreateDirectory(stagedSources);
                string stagedWim = Path.Combine(stagedSources, "install.wim");
                File.Copy(exportedWim, stagedWim, overwrite: true);

                long servicedSizeMb = new FileInfo(stagedWim).Length / 1048576;
                Console.WriteLine($"[CLI] Serviced WIM copied to staged tree ({servicedSizeMb} MB)");

                // 8. Write autounattend.xml only for full AFK (unattended) installs.
                //    Hostname-only builds use Windows\Panther\unattend.xml (specialize pass).
                if (unattendedCfg != null && afk)
                {
                    Console.WriteLine("[CLI] Writing autounattend.xml to ISO root…");
                    UnattendedGenerator.WriteToIsoRoot(unattendedCfg, stageDir);
                }

                // 9. Build output ISO.
                Console.WriteLine($"[CLI] Building ISO: {output.FullName}");
                var builder = new IsoBuilder(oscdimgPath);
                await builder.BuildAsync(stageDir, output.FullName, reporter, ct);

                if (File.Exists(output.FullName))
                {
                    long isoSizeMb = new FileInfo(output.FullName).Length / 1048576;
                    Console.WriteLine($"[CLI] Done! Output ISO: {output.FullName} ({isoSizeMb} MB)");
                }
                else
                {
                    Console.Error.WriteLine($"[CLI] ERROR: oscdimg completed but output file not found at {output.FullName}");
                    ctx.ExitCode = 1;
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[CLI] Cancelled.");
                if (tempMount != null)
                {
                    try
                    {
                        var wim2 = new WimManager();
                        await wim2.UnmountAsync(tempMount, commit: false, reporter, CancellationToken.None);
                    }
                    catch { /* best effort */ }
                }
                ctx.ExitCode = 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CLI] Error ({ex.GetType().Name}): {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                try { await HiveManager.CleanupAllHivesAsync(CancellationToken.None); } catch { }
                if (tempMount != null)
                {
                    try
                    {
                        var wim2 = new WimManager();
                        await wim2.UnmountAsync(tempMount, commit: false, reporter, CancellationToken.None);
                    }
                    catch { /* best effort */ }
                }
                ctx.ExitCode = 1;
            }
            finally
            {
                if (tempWimDir != null)
                    try { Directory.Delete(tempWimDir, recursive: true); } catch { /* best effort */ }
                if (stageDir != null)
                    try { Directory.Delete(stageDir, recursive: true); } catch { /* best effort */ }
                if (autoMount && tempMount != null)
                    try { Directory.Delete(tempMount, recursive: true); } catch { /* best effort */ }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── WIM-only pipeline (original) ──────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// WIM/ESD pipeline: mount → apply preset → unmount → optionally build ISO
        /// from an existing directory tree surrounding the WIM.
        /// </summary>
        private static async Task RunWimAsync(
            InvocationContext ctx,
            FileInfo wimFile,
            Preset10 preset,
            string presetStr,
            int index,
            FileInfo? output,
            DirectoryInfo mountDir,
            DirectoryInfo? drivers,
            DirectoryInfo? updates,
            string? hostname,
            bool afk,
            string? adminUser,
            string? adminPass,
            IProgress<string> reporter,
            CancellationToken ct)
        {
            try
            {
                // 0. Early validation: ensure oscdimg.exe is available if building an ISO.
                string? oscdimgPath = null;
                if (output != null)
                {
                    Console.WriteLine("[CLI] Checking for oscdimg.exe…");
                    oscdimgPath = await IsoBuilder.EnsureOscdimgAsync(reporter, ct);
                }

                // 1. Mount
                Console.WriteLine($"[CLI] Mounting index {index} from {wimFile.FullName}…");
                var wim = new WimManager();
                await wim.MountAsync(wimFile.FullName, index, mountDir.FullName, reporter, ct);

                // 2. Run preset
                Console.WriteLine($"[CLI] Running preset: {presetStr}");

                bool needsUnattend = afk || !string.IsNullOrWhiteSpace(hostname);
                var unattendedCfg = needsUnattend
                    ? new UnattendedConfig
                    {
                        ComputerName     = string.IsNullOrWhiteSpace(hostname) ? "*" : hostname,
                        SkipOOBE         = afk,
                        AcceptEula       = afk,
                        HideEulaPage     = afk,
                        HideWirelessPage = afk,
                        AdminUsername    = afk ? adminUser : null,
                        AdminPassword    = afk ? adminPass : null,
                        WimIndex         = index,
                    }
                    : null;

                var runner = new PresetRunner10(mountDir.FullName, preset)
                {
                    DriverFolder     = drivers?.FullName,
                    UpdateFolder     = updates?.FullName,
                    UnattendedConfig = unattendedCfg,
                };
                await runner.RunAsync(reporter, ct);

                // 3. Unmount + commit
                Console.WriteLine("[CLI] Committing and unmounting…");
                await wim.UnmountAsync(mountDir.FullName, commit: true, reporter, ct);

                // 4. Optionally build ISO
                if (output != null)
                {
                    string? wimDirPath = Path.GetDirectoryName(wimFile.FullName);
                    string? isoRoot    = wimDirPath != null ? Path.GetDirectoryName(wimDirPath) : null;

                    if (isoRoot == null
                        || !Directory.Exists(Path.Combine(isoRoot, "boot"))
                        || !Directory.Exists(Path.Combine(isoRoot, "sources")))
                    {
                        Console.Error.WriteLine("[CLI] Cannot build ISO: the WIM is not inside a valid Windows ISO directory tree (expected boot\\ and sources\\ alongside the WIM).");
                        Console.Error.WriteLine("[CLI] The WIM was serviced successfully, but ISO output was skipped.");
                    }
                    else
                    {
                        if (unattendedCfg != null && afk)
                        {
                            Console.WriteLine("[CLI] Writing autounattend.xml to ISO root…");
                            UnattendedGenerator.WriteToIsoRoot(unattendedCfg, isoRoot);
                        }

                        Console.WriteLine($"[CLI] Building ISO: {output.FullName}");
                        var builder = new IsoBuilder(oscdimgPath!);
                        await builder.BuildAsync(isoRoot, output.FullName, reporter, ct);
                    }
                }

                Console.WriteLine("[CLI] Done.");
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[CLI] Cancelled.");
                try
                {
                    var wim = new WimManager();
                    await wim.UnmountAsync(mountDir.FullName, commit: false, reporter, CancellationToken.None);
                }
                catch { /* best effort */ }
                ctx.ExitCode = 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CLI] Error ({ex.GetType().Name}): {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                try { await HiveManager.CleanupAllHivesAsync(CancellationToken.None); } catch { }
                try
                {
                    var wim2 = new WimManager();
                    await wim2.UnmountAsync(mountDir.FullName, commit: false, reporter, CancellationToken.None);
                }
                catch { /* best effort */ }
                ctx.ExitCode = 1;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── Helpers ───────────────────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

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
