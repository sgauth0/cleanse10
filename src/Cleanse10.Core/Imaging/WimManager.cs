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

        // ──────────────────────────────────────────────────────────────────────
        // ESD → WIM conversion
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts an ESD file to a standard WIM file using DISM /Export-Image.
        /// DISM cannot mount an ESD read-write; this step is required before <see cref="MountAsync"/>
        /// when the source file has a .esd extension.
        /// </summary>
        /// <param name="esdPath">Path to the source .esd file.</param>
        /// <param name="imageIndex">Image index to export (1-based).</param>
        /// <param name="outputWimPath">Destination .wim file path. Will be overwritten if it exists.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Path to the exported WIM file (<paramref name="outputWimPath"/>).</returns>
        public async Task<string> ExportEsdToWimAsync(
            string esdPath,
            int imageIndex,
            string outputWimPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            return await ExportImageToWimAsync(esdPath, imageIndex, outputWimPath, progress, ct);
        }

        public async Task<string> ExportImageToWimAsync(
            string sourceImagePath,
            int imageIndex,
            string outputWimPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(sourceImagePath))
                throw new FileNotFoundException($"Image not found: {sourceImagePath}");

            // Remove stale output so DISM doesn't append
            if (File.Exists(outputWimPath))
                File.Delete(outputWimPath);

            progress?.Report($"[WimManager] Exporting image index {imageIndex} to WIM: {sourceImagePath} → {outputWimPath}");

            await RunDismAsync(
                $"/Export-Image /SourceImageFile:\"{sourceImagePath}\" /SourceIndex:{imageIndex}" +
                $" /DestinationImageFile:\"{outputWimPath}\" /DestinationName:\"install\" /Compress:max",
                progress, ct);

            progress?.Report("[WimManager] Image export complete.");
            return outputWimPath;
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

            // DISM cannot mount an ESD file read-write. If the caller passed an ESD,
            // export the requested index to a sibling .wim file first, then mount that.
            if (string.Equals(Path.GetExtension(wimPath), ".esd", StringComparison.OrdinalIgnoreCase))
            {
                string convertedWim = Path.ChangeExtension(wimPath, ".wim");
                progress?.Report($"[WimManager] ESD detected — exporting index {imageIndex} to WIM before mounting…");
                wimPath = await ExportEsdToWimAsync(wimPath, imageIndex, convertedWim, progress, ct);
                // After export the WIM contains exactly one image at index 1
                imageIndex = 1;
            }

            Directory.CreateDirectory(mountPath);

            // Unload any Cleanse10 registry hive mounts left over from a previous
            // crashed run BEFORE attempting the discard-unmount.  A loaded NTUSER.DAT
            // hive holds an open handle to a file inside the mount directory, which
            // causes DISM's /Unmount-Wim /Discard to fail — leaving the image mounted
            // and the next /Mount-Wim to return 0xc1420127 (already mounted).
            await HiveManager.CleanupAllHivesAsync(CancellationToken.None);

            // If the WIM file itself is already mounted (at any mount directory),
            // unmount it first.  This handles the case where a previous run crashed
            // and left the WIM mounted at a different (now-stale) directory.
            await UnmountStaleSessionsForWimAsync(wimPath, progress);

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
        // Stale-mount cleanup
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queries DISM for all currently mounted WIM images and unmounts (with /Discard)
        /// any session whose image file path matches <paramref name="wimPath"/>.
        /// This prevents error 0xc1420127 ("already mounted") when remounting after a crash.
        /// </summary>
        private async Task UnmountStaleSessionsForWimAsync(
            string wimPath,
            IProgress<string>? progress)
        {
            string fullWimPath;
            try { fullWimPath = Path.GetFullPath(wimPath); }
            catch { return; }

            List<string> output;
            try
            {
                output = await RunDismCaptureAsync("/Get-MountedWimInfo", CancellationToken.None);
            }
            catch { return; /* best effort — dism might not be available */ }

            // Parse output blocks separated by blank lines.
            // Each mounted image block looks like:
            //   Mount Dir : C:\some\path
            //   Image File : C:\some\file.wim
            //   Image Index : 1
            //   ...
            string? currentMountDir = null;

            foreach (string line in output)
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("Mount Dir", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = trimmed.IndexOf(':');
                    currentMountDir = colon >= 0 ? trimmed[(colon + 1)..].Trim() : null;
                }
                else if (trimmed.StartsWith("Image File", StringComparison.OrdinalIgnoreCase)
                         && currentMountDir != null)
                {
                    int colon = trimmed.IndexOf(':');
                    string imageFile = colon >= 0 ? trimmed[(colon + 1)..].Trim() : string.Empty;

                    try
                    {
                        if (string.Equals(Path.GetFullPath(imageFile), fullWimPath, StringComparison.OrdinalIgnoreCase))
                        {
                            progress?.Report($"[WimManager] Found stale mount for {wimPath} at {currentMountDir} — unmounting…");

                            // Unload hives first so the unmount isn't blocked
                            await HiveManager.CleanupAllHivesAsync(CancellationToken.None);

                            try
                            {
                                await RunDismAsync(
                                    $"/Unmount-Wim /MountDir:\"{currentMountDir}\" /Discard",
                                    null, CancellationToken.None);
                                progress?.Report("[WimManager] Stale mount discarded.");
                            }
                            catch
                            {
                                // Cleanup-Image as last resort
                                try
                                {
                                    await RunDismAsync("/Cleanup-Wim", null, CancellationToken.None);
                                    progress?.Report("[WimManager] Ran /Cleanup-Wim for stale mount.");
                                }
                                catch { /* best effort */ }
                            }
                        }
                    }
                    catch { /* path comparison failed — skip */ }

                    currentMountDir = null;
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Runs dism.exe and captures stdout lines for parsing.</summary>
        private static Task<List<string>> RunDismCaptureAsync(string args, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lines = new List<string>();

            var psi = new ProcessStartInfo("dism.exe", args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Add(e.Data); };
            p.ErrorDataReceived  += (_, e) => { /* discard stderr */ };

            p.Exited += (_, _) =>
            {
                if (p.ExitCode == 0 || p.ExitCode == 3010)
                    tcs.TrySetResult(lines);
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
