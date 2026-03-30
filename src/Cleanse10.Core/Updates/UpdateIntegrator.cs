using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Updates
{
    /// <summary>
    /// Integrates Windows Update packages (.msu / .cab) into an offline (mounted) image via dism.exe.
    /// </summary>
    public class UpdateIntegrator
    {
        /// <summary>
        /// Adds all .msu and .cab files found in <paramref name="updateFolder"/> to the mounted image.
        /// Files are applied in alphabetical order (which generally matches cumulative update dependency order).
        /// </summary>
        public async Task IntegrateUpdatesAsync(
            string mountPath,
            string updateFolder,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!Directory.Exists(updateFolder))
            {
                progress?.Report($"[Updates] Update folder not found, skipping: {updateFolder}");
                return;
            }

            var packages = new List<string>();
            packages.AddRange(Directory.GetFiles(updateFolder, "*.msu", SearchOption.TopDirectoryOnly));
            packages.AddRange(Directory.GetFiles(updateFolder, "*.cab", SearchOption.TopDirectoryOnly));
            packages.Sort(StringComparer.OrdinalIgnoreCase);

            progress?.Report($"[Updates] Found {packages.Count} update package(s).");

            foreach (string pkg in packages)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Updates] Integrating: {Path.GetFileName(pkg)}");
                await RunDismAsync(
                    $"/Image:\"{mountPath}\" /Add-Package /PackagePath:\"{pkg}\"",
                    progress, ct);
            }

            progress?.Report("[Updates] All packages integrated.");
        }

        private static Task RunDismAsync(string args, IProgress<string>? progress, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var psi = new ProcessStartInfo("dism.exe", args)
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
                if (p.ExitCode == 0 || p.ExitCode == 3010)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new Exception($"dism.exe (updates) exited with code {p.ExitCode}"));
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
