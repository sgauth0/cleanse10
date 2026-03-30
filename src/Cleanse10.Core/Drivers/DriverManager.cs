using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Drivers
{
    /// <summary>
    /// Integrates third-party drivers (.inf) into an offline (mounted) Windows 10 image via dism.exe.
    /// </summary>
    public class DriverManager
    {
        /// <summary>
        /// Adds all .inf drivers found recursively under <paramref name="driverFolder"/> into the mounted image.
        /// </summary>
        public async Task AddDriversFromFolderAsync(
            string mountPath,
            string driverFolder,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!Directory.Exists(driverFolder))
            {
                progress?.Report($"[Drivers] Driver folder not found, skipping: {driverFolder}");
                return;
            }

            var infFiles = Directory.GetFiles(driverFolder, "*.inf", SearchOption.AllDirectories);
            progress?.Report($"[Drivers] Found {infFiles.Length} driver(s) in {driverFolder}");

            foreach (string inf in infFiles)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"[Drivers] Adding driver: {inf}");
                await RunDismAsync(
                    $"/Image:\"{mountPath}\" /Add-Driver /Driver:\"{inf}\"",
                    progress, ct);
            }
        }

        /// <summary>
        /// Adds a single driver package (folder containing .inf) into the mounted image.
        /// Pass <paramref name="recurse"/> = true to inject all drivers in subdirectories at once.
        /// </summary>
        public async Task AddDriverAsync(
            string mountPath,
            string driverPathOrFolder,
            bool recurse = false,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            string recurseFlag = recurse ? " /Recurse" : string.Empty;
            progress?.Report($"[Drivers] Adding driver: {driverPathOrFolder}");
            await RunDismAsync(
                $"/Image:\"{mountPath}\" /Add-Driver /Driver:\"{driverPathOrFolder}\"{recurseFlag}",
                progress, ct);
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
                    tcs.TrySetException(new Exception($"dism.exe (drivers) exited with code {p.ExitCode}"));
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
