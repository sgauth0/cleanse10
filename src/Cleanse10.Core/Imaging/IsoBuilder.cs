using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Imaging
{
    /// <summary>
    /// Rebuilds a bootable Windows 10 ISO from a modified source directory using oscdimg.exe.
    /// </summary>
    public class IsoBuilder
    {
        private readonly string _oscdimgPath;

        public IsoBuilder(string oscdimgPath = "oscdimg.exe")
        {
            _oscdimgPath = oscdimgPath;
        }

        /// <summary>
        /// Builds a bootable ISO from <paramref name="sourceDir"/> and writes it to <paramref name="outputIso"/>.
        /// Requires oscdimg.exe from the Windows ADK.
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
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) progress?.Report("[ERR] " + e.Data); };

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
