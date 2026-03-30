using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cleanse10.Core.Bloat;

namespace Cleanse10.Core.Imaging
{
    /// <summary>
    /// Wraps dism.exe for mount / unmount / query operations on WIM/ESD files.
    /// </summary>
    public sealed class WimManager : IDisposable
    {
        // ──────────────────────────────────────────────────────────────────────
        // Query
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Returns information about all images in a WIM/ESD file.</summary>
        public async Task<IReadOnlyList<WimImageInfo>> GetInfoAsync(
            string wimPath,
            CancellationToken ct = default)
        {
            if (!File.Exists(wimPath))
                throw new FileNotFoundException($"WIM not found: {wimPath}");

            var info = await OpenWimWithDismAsync(wimPath, ct);
            return info.Images;
        }

        // Synchronous helper used internally
        public WimFileInfo OpenWim(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}");
            try   { return OpenWimWithDismAsync(path, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { throw new InvalidOperationException($"Cannot read WIM file: {ex.Message}"); }
        }

        private static async Task<WimFileInfo> OpenWimWithDismAsync(string path, CancellationToken ct)
        {
            var tcs    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sbOut  = new StringBuilder();
            var sbErr  = new StringBuilder();

            var psi = new ProcessStartInfo("dism.exe", $"/Get-WimInfo /WimFile:\"{path}\"")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            p.Exited += (_, _) =>
            {
                if (p.ExitCode == 0)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new InvalidOperationException(
                        $"dism.exe /Get-WimInfo failed (exit {p.ExitCode}): {sbErr}"));
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

            await tcs.Task;

            string output = sbOut.ToString();

            var indexMatches = Regex.Matches(output, @"Index\s*:\s*(\d+)");
            var nameMatches  = Regex.Matches(output, @"Name\s*:\s*(.+)");
            var descMatches  = Regex.Matches(output, @"Description\s*:\s*(.+)");
            var archMatches  = Regex.Matches(output, @"Architecture\s*:\s*(.+)");
            var sizeMatches  = Regex.Matches(output, @"Size\s*:\s*([\d,]+)");

            int count  = Math.Min(indexMatches.Count, nameMatches.Count);
            var images = new List<WimImageInfo>();

            for (int i = 0; i < count; i++)
            {
                images.Add(new WimImageInfo
                {
                    Index        = int.Parse(indexMatches[i].Groups[1].Value),
                    Name         = i < nameMatches.Count ? nameMatches[i].Groups[1].Value.Trim() : $"Image {i + 1}",
                    Description  = i < descMatches.Count ? descMatches[i].Groups[1].Value.Trim() : string.Empty,
                    Architecture = i < archMatches.Count ? archMatches[i].Groups[1].Value.Trim() : string.Empty,
                    SizeBytes    = i < sizeMatches.Count
                                       ? long.Parse(sizeMatches[i].Groups[1].Value.Replace(",", ""))
                                       : 0,
                });
            }

            if (images.Count == 0)
                throw new InvalidOperationException("No images found in WIM file.");

            return new WimFileInfo { FilePath = path, Images = images };
        }

        // ──────────────────────────────────────────────────────────────────────
        // Mount / Unmount
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Mounts <paramref name="wimPath"/> index <paramref name="imageIndex"/> to <paramref name="mountPath"/>.</summary>
        public async Task MountAsync(
            string wimPath,
            int imageIndex,
            string mountPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(wimPath))
                throw new FileNotFoundException($"WIM not found: {wimPath}");

            Directory.CreateDirectory(mountPath);

            // Unload any Cleanse10 registry hive mounts left over from a previous
            // crashed run BEFORE attempting the discard-unmount.  A loaded NTUSER.DAT
            // hive holds an open handle to a file inside the mount directory, which
            // causes DISM's /Unmount-Wim /Discard to fail — leaving the image mounted
            // and the next /Mount-Wim to return 0xc1420127 (already mounted).
            await HiveManager.CleanupAllHivesAsync(CancellationToken.None);

            // Discard any stale DISM session left over from a previous crashed or
            // elevated-but-failed run.  A dangling session causes exit code 13
            // (ERROR_INVALID_DATA) even when the directory looks empty to the OS.
            // Suppress output and ignore failure — no prior session is the normal case.
            try
            {
                await RunDismAsync(
                    $"/Unmount-Wim /MountDir:\"{mountPath}\" /Discard",
                    null, CancellationToken.None);
                progress?.Report("[WimManager] Discarded stale mount session.");
            }
            catch { /* best effort */ }

            // Strip read-only attribute — ISOs and copy operations often set it,
            // and DISM returns 0xc1510111 if the WIM file is read-only.
            var attrs = File.GetAttributes(wimPath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(wimPath, attrs & ~FileAttributes.ReadOnly);
                progress?.Report("[WimManager] Cleared read-only attribute on WIM file.");
            }

            progress?.Report($"[WimManager] Mounting index {imageIndex} from {wimPath} → {mountPath}");

            await RunDismAsync(
                $"/Mount-Wim /WimFile:\"{wimPath}\" /Index:{imageIndex} /MountDir:\"{mountPath}\"",
                progress, ct);

            progress?.Report("[WimManager] Mount complete.");
        }

        /// <summary>Unmounts the image at <paramref name="mountPath"/>, optionally committing changes.</summary>
        public async Task UnmountAsync(
            string mountPath,
            bool commit,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            string flag = commit ? "/Commit" : "/Discard";
            progress?.Report($"[WimManager] Unmounting {mountPath} ({(commit ? "commit" : "discard")})…");

            await RunDismAsync(
                $"/Unmount-Wim /MountDir:\"{mountPath}\" {flag}",
                progress, ct);

            progress?.Report("[WimManager] Unmount complete.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

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
                    tcs.TrySetException(new Exception($"dism.exe exited with code {p.ExitCode} for: {args}"));
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
