using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Components
{
    /// <summary>
    /// Removes optional Windows features/components from an offline (mounted) image via dism.exe.
    /// </summary>
    public class ComponentManager
    {
        // Optional features that can be removed for a leaner Windows 10 image
        public static readonly IReadOnlyList<string> DefaultRemoveList = new[]
        {
            "Printing-PrintToPDFServices-Features",
            "Printing-XPSServices-Features",
            "Internet-Explorer-Optional-amd64",
            "MediaPlayback",
            "WindowsMediaPlayer",
            "WorkFolders-Client",
            "FaxServicesClientPackage",
            "MicrosoftWindowsPowerShellV2Root",
            "MicrosoftWindowsPowerShellV2",
            "MSRDC-Infrastructure",
            "SearchEngine-Client-Package",          // Windows Search
            "TelnetClient",
            "TFTP",
            "LegacyComponents",
            "SMB1Protocol",                         // Security risk (spec §2.2) — disable SMBv1
        };

        /// <summary>
        /// Disables/removes each feature in <paramref name="features"/> from the mounted image at <paramref name="mountPath"/>.
        /// </summary>
        public async Task RemoveFeaturesAsync(
            string mountPath,
            IEnumerable<string> features,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            foreach (string feature in features)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Components] Disabling feature: {feature}");
                try
                {
                    await RunDismAsync(
                        $"/Image:\"{mountPath}\" /Disable-Feature /FeatureName:{feature} /Remove",
                        progress, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Feature absent from this edition or already removed — not fatal.
                    progress?.Report($"[WARN] Could not remove feature '{feature}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Enables each feature in <paramref name="features"/> in the mounted image at <paramref name="mountPath"/>.
        /// Uses /All to also enable parent features as needed and /NoRestart to suppress reboot prompts.
        /// </summary>
        public async Task EnableFeaturesAsync(
            string mountPath,
            IEnumerable<string> features,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            foreach (string feature in features)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Components] Enabling feature: {feature}");
                try
                {
                    await RunDismAsync(
                        $"/Image:\"{mountPath}\" /Enable-Feature /FeatureName:{feature} /All /NoRestart",
                        progress, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Feature absent from this edition or already enabled — not fatal.
                    progress?.Report($"[WARN] Could not enable feature '{feature}': {ex.Message}");
                }
            }
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
                // DISM exit codes 0 and 3010 (reboot required) are success
                if (p.ExitCode == 0 || p.ExitCode == 3010)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new Exception($"dism.exe exited with code {p.ExitCode} for args: {args}"));
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
