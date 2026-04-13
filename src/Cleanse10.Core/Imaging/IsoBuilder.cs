using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Cleanse10.Core.Imaging
{
    /// <summary>
    /// Rebuilds a bootable Windows 10 ISO from a modified source directory using oscdimg.exe.
    /// </summary>
    public class IsoBuilder
    {
        // ──────────────────────────────────────────────────────────────────────
        // ── oscdimg.exe resolution ─────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Relative path from a Windows Kits root to oscdimg.exe.</summary>
        private const string OscdimgRelPath =
            @"Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe";

        /// <summary>Windows ADK online installer (small bootstrapper, ~1.6 MB).</summary>
        private const string AdkSetupUrl = "https://go.microsoft.com/fwlink/?linkid=2271337";

        /// <summary>Registry key where Windows Kits records its install root.</summary>
        private const string KitsRootRegKey =
            @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";

        /// <summary>
        /// Locates oscdimg.exe on the system, installing the Windows ADK
        /// Deployment Tools automatically if it is not already present.
        /// </summary>
        public static async Task<string> EnsureOscdimgAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            // 1. Check PATH
            string? onPath = FindOnPath("oscdimg.exe");
            if (onPath != null) return onPath;

            // 2. Check the registry for an existing Windows Kits install root
            //    (covers both Windows SDK and ADK — they share the same key).
            string? kitsRoot = ReadKitsRoot();
            if (kitsRoot != null)
            {
                string candidate = Path.Combine(kitsRoot, OscdimgRelPath);
                if (File.Exists(candidate)) return candidate;
            }

            // 3. Check hard-coded fallback paths
            string[] fallbacks =
            [
                @"C:\Program Files (x86)\Windows Kits\10\" + OscdimgRelPath,
                @"C:\Program Files\Windows Kits\10\" + OscdimgRelPath,
            ];
            foreach (string p in fallbacks)
                if (File.Exists(p)) return p;

            // 4. Not found — auto-install ADK Deployment Tools.
            //    Use the existing Kits root if one is registered (the ADK installer
            //    refuses a custom path when another Kits install is detected).
            string installPath = kitsRoot ?? @"C:\Program Files (x86)\Windows Kits\10";

            progress?.Report("[IsoBuilder] oscdimg.exe not found — installing Windows ADK Deployment Tools…");

            string adkSetup = Path.Combine(Path.GetTempPath(),
                $"cleanse10_adksetup_{Guid.NewGuid():N}.exe");

            try
            {
                // Download the ADK online bootstrapper (~1.6 MB)
                progress?.Report("[IsoBuilder] Downloading ADK setup from Microsoft…");
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
                {
                    using var resp = await http.GetAsync(AdkSetupUrl,
                        HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(adkSetup, FileMode.Create,
                        FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await src.CopyToAsync(dst, ct);
                }

                // Install only Deployment Tools (contains oscdimg.exe).
                // /quiet = no UI, /norestart = don't reboot.
                // /installpath must match the existing Kits root or the installer
                // exits with 1001.
                progress?.Report("[IsoBuilder] Installing ADK Deployment Tools (this may take a few minutes)…");

                var tcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var psi = new ProcessStartInfo(adkSetup,
                    $"/quiet /norestart /features OptionId.DeploymentTools " +
                    $"/installpath \"{installPath}\"")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) progress?.Report($"[ADK] {e.Data}");
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) progress?.Report($"[ADK] {e.Data}");
                };

                proc.Exited += (_, _) =>
                {
                    if (proc.ExitCode == 0 || proc.ExitCode == 3010)
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(new Exception(
                            $"ADK setup exited with code {proc.ExitCode}."));
                    proc.Dispose();
                };

                ct.Register(() =>
                {
                    try { proc.Kill(); } catch { }
                    tcs.TrySetCanceled(ct);
                });

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await tcs.Task;
                progress?.Report("[IsoBuilder] ADK Deployment Tools installed.");
            }
            finally
            {
                try { File.Delete(adkSetup); } catch { /* best effort */ }
            }

            // Re-check: the installer puts oscdimg under the Kits root.
            string expected = Path.Combine(installPath, OscdimgRelPath);
            if (File.Exists(expected)) return expected;

            // Re-read registry in case the installer changed the root.
            kitsRoot = ReadKitsRoot();
            if (kitsRoot != null)
            {
                expected = Path.Combine(kitsRoot, OscdimgRelPath);
                if (File.Exists(expected)) return expected;
            }

            throw new FileNotFoundException(
                "oscdimg.exe could not be found after installing the ADK. " +
                "Please install the Windows ADK Deployment Tools manually: " +
                "https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install");
        }

        private static string? ReadKitsRoot()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(KitsRootRegKey);
                return key?.GetValue("KitsRoot10") as string;
            }
            catch { return null; }
        }

        private static string? FindOnPath(string exe)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv == null) return null;
            foreach (string dir in pathEnv.Split(';'))
            {
                string candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // ──────────────────────────────────────────────────────────────────────
        // ── ISO building ───────────────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────

        private readonly string _oscdimgPath;

        public IsoBuilder(string oscdimgPath)
        {
            _oscdimgPath = oscdimgPath;
        }

        /// <summary>
        /// Builds a bootable ISO from <paramref name="sourceDir"/> and writes it to <paramref name="outputIso"/>.
        /// </summary>
        public async Task BuildAsync(
            string sourceDir,
            string outputIso,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            string? outputDir = Path.GetDirectoryName(outputIso);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Boot sector paths inside the source directory
            string efiBoot  = Path.Combine(sourceDir, "efi", "microsoft", "boot", "efisys.bin");
            string biosboot = Path.Combine(sourceDir, "boot", "etfsboot.com");

            // Build oscdimg arguments for a dual-boot (BIOS + UEFI) ISO
            string args;
            if (File.Exists(efiBoot) && File.Exists(biosboot))
            {
                args = $"-m -o -u2 -udfver102 " +
                       $"-bootdata:2#p0,e,b\"{biosboot}\"#pEF,e,b\"{efiBoot}\" " +
                       $"\"{sourceDir}\" \"{outputIso}\"";
            }
            else if (File.Exists(efiBoot))
            {
                args = $"-m -o -u2 -udfver102 " +
                       $"-bootdata:1#pEF,e,b\"{efiBoot}\" " +
                       $"\"{sourceDir}\" \"{outputIso}\"";
            }
            else
            {
                // Fallback: no boot sector (won't be bootable — warn the caller)
                progress?.Report("[WARN] No boot sector found; ISO will not be bootable.");
                args = $"-m -o -u2 -udfver102 \"{sourceDir}\" \"{outputIso}\"";
            }

            progress?.Report($"[IsoBuilder] Running: {_oscdimgPath} {args}");

            await RunProcessAsync(_oscdimgPath, args, progress, ct);

            progress?.Report($"[IsoBuilder] ISO written to: {outputIso}");
        }

        private static Task RunProcessAsync(
            string exe,
            string args,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.OutputDataReceived += (_, e) => { if (e.Data != null) progress?.Report(e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) progress?.Report("[IsoBuilder] " + e.Data); };

            p.Exited += (_, _) =>
            {
                if (p.ExitCode == 0)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new Exception($"oscdimg exited with code {p.ExitCode}"));
                p.Dispose();
            };

            ct.Register(() =>
            {
                try { p.Kill(); } catch { }
                tcs.TrySetCanceled(ct);
            });

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            return tcs.Task;
        }
    }
}
