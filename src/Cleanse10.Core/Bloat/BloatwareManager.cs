using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Bloat
{
    /// <summary>
    /// Removes provisioned AppX packages from an offline (mounted) Windows 10 image via dism.exe.
    /// </summary>
    public class BloatwareManager
    {
        /// <summary>
        /// Removes every package in <paramref name="packages"/> from the mounted image at <paramref name="mountPath"/>.
        /// Packages that are not present in the image are silently skipped.
        /// </summary>
        public async Task RemovePackagesAsync(
            string mountPath,
            IEnumerable<string> packages,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            // Build the current provisioned-package list once so we can skip missing entries
            var present = await GetProvisionedPackageNamesAsync(mountPath, progress, ct);

            foreach (string pkg in packages)
            {
                ct.ThrowIfCancellationRequested();

                // Match by partial name (provisioned name contains version suffix)
                string? fullName = FindMatch(present, pkg);
                if (fullName is null)
                {
                    progress?.Report($"[Bloat] Not found (skipping): {pkg}");
                    continue;
                }

                progress?.Report($"[Bloat] Removing: {fullName}");
                await RunDismAsync(
                    $"/Image:\"{mountPath}\" /Remove-ProvisionedAppxPackage /PackageName:{fullName}",
                    progress, ct);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static async Task<List<string>> GetProvisionedPackageNamesAsync(
            string mountPath,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var lines  = new List<string>();
            var result = new List<string>();

            var psi = new ProcessStartInfo(
                "dism.exe",
                $"/Image:\"{mountPath}\" /Get-ProvisionedAppxPackages")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var p = Process.Start(psi)
                          ?? throw new Exception("Failed to start dism.exe to list packages.");

            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) lines.Add(e.Data);
            };
            p.BeginOutputReadLine();

            try
            {
                await p.WaitForExitAsync(ct);
            }
            catch
            {
                // Kill the process so it doesn't linger after cancellation or error.
                if (!p.HasExited)
                    try { p.Kill(); } catch { /* best effort */ }
                throw;
            }

            // Each package block has a "PackageName : Microsoft.Foo_x.y.z_neutral~..." line
            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith("PackageName", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    if (colon >= 0)
                        result.Add(line[(colon + 1)..].Trim());
                }
            }

            return result;
        }

        private static string? FindMatch(List<string> fullNames, string shortName)
        {
            foreach (string fn in fullNames)
            {
                if (fn.StartsWith(shortName, StringComparison.OrdinalIgnoreCase))
                    return fn;
            }
            return null;
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
                    tcs.TrySetException(new Exception($"dism.exe (bloat removal) exited with code {p.ExitCode}"));
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
