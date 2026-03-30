using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Imaging
{
    /// <summary>
    /// Thin facade around dism.exe for general-purpose offline image operations.
    /// Most callers should use <see cref="WimManager"/> (mount/unmount) plus the
    /// specialized managers in Bloat / Components / Drivers / Updates instead.
    /// </summary>
    public sealed class DismService : IDisposable
    {
        /// <summary>
        /// Runs an arbitrary dism.exe command against the mounted image at <paramref name="mountPath"/>.
        /// </summary>
        public Task RunCommandAsync(
            string mountPath,
            string dismArgs,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            string fullArgs = $"/Image:\"{mountPath}\" {dismArgs}";
            return RunDismAsync(fullArgs, progress, ct);
        }

        /// <summary>
        /// Returns the raw text output of a dism.exe query against the mounted image.
        /// </summary>
        public async Task<string> QueryAsync(
            string mountPath,
            string dismArgs,
            CancellationToken ct = default)
        {
            var sb  = new StringBuilder();
            var psi = new ProcessStartInfo(
                "dism.exe",
                $"/Image:\"{mountPath}\" {dismArgs}")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var p = Process.Start(psi)
                          ?? throw new InvalidOperationException("Failed to start dism.exe");

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            p.BeginOutputReadLine();

            await p.WaitForExitAsync(ct);
            return sb.ToString();
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
                    tcs.TrySetException(new Exception($"dism.exe exited with code {p.ExitCode}"));
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

        public void Dispose() { }
    }
}
