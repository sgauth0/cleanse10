using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Media
{
    /// <summary>Languages available for Windows 10 22H2 ISO downloads (must match Fido exactly).</summary>
    public static class Win10Languages
    {
        public static readonly string[] All =
        {
            "Arabic", "Brazilian Portuguese", "Bulgarian",
            "Chinese Simplified", "Chinese Traditional", "Croatian",
            "Czech", "Danish", "Dutch", "English", "English International",
            "Estonian", "Finnish", "French", "French Canadian", "German",
            "Greek", "Hebrew", "Hungarian", "Italian", "Japanese", "Korean",
            "Latvian", "Lithuanian", "Norwegian", "Polish", "Portuguese",
            "Romanian", "Russian", "Serbian Latin", "Slovak", "Slovenian",
            "Spanish", "Spanish (Mexico)", "Swedish", "Thai", "Turkish",
            "Ukrainian",
        };

        public const string Default = "English";
    }

    /// <summary>
    /// Downloads an official Windows 10 22H2 ISO from Microsoft using the Fido PowerShell
    /// script (github.com/pbatard/Fido) — the same technique used by Rufus.
    /// Also handles mounting the ISO and extracting install.wim.
    /// </summary>
    public static class Win10DownloadService
    {
        private static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Cleanse10");

        private static readonly string FidoCachePath = Path.Combine(CacheDir, "Fido.ps1");

        private const string FidoUrl =
            "https://raw.githubusercontent.com/pbatard/Fido/master/Fido.ps1";

        // ── Fido script ────────────────────────────────────────────────────────

        /// <summary>Returns path to Fido.ps1, downloading it if absent or stale (&gt;7 days).</summary>
        public static async Task<string> GetFidoScriptAsync(
            IProgress<string> log, CancellationToken ct)
        {
            bool stale = !File.Exists(FidoCachePath) ||
                         (DateTime.UtcNow - File.GetLastWriteTimeUtc(FidoCachePath)).TotalDays > 7;

            if (stale)
            {
                log.Report("[Cleanse10] Fetching Fido script from GitHub…");
                Directory.CreateDirectory(CacheDir);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                string script = await client.GetStringAsync(FidoUrl, ct);
                await File.WriteAllTextAsync(FidoCachePath, script, ct);
            }

            return FidoCachePath;
        }

        // ── Resolve download URL ───────────────────────────────────────────────

        /// <summary>
        /// Runs Fido with -GetUrl to resolve the official Microsoft HTTPS download URL
        /// for a Windows 10 22H2 ISO in the requested language.
        /// </summary>
        public static async Task<string> GetDownloadUrlAsync(
            string language, IProgress<string> log, CancellationToken ct)
        {
            string fidoPath = await GetFidoScriptAsync(log, ct);
            log.Report($"[Cleanse10] Resolving download URL for '{language}'...");

            // Write a temp launcher script to avoid command-line quoting nightmares
            string tempPs1 = Path.Combine(Path.GetTempPath(),
                                           $"cleanse10_fido_{Guid.NewGuid():N}.ps1");
            try
            {
                // Single-quoted PS strings handle most cases; double-quote the path
                string launcher =
                    $"& \"{fidoPath.Replace("\"", "`\"")}\"" +
                    $" -Win 10 -Rel 22H2 -Ed 'Windows 10'" +
                    $" -Lang '{language.Replace("'", "''")}'" +
                    $" -Arch x64 -GetUrl";

                await File.WriteAllTextAsync(tempPs1, launcher, ct);

                string output = await RunPs1Async(tempPs1, ct);

                // URL is the last https:// line in stdout
                string? url = null;
                foreach (string line in output.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = t;
                }

                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException(
                        $"Fido did not return a download URL.\nOutput:\n{output}");

                log.Report("[Cleanse10] Download URL resolved.");
                return url;
            }
            finally
            {
                try { File.Delete(tempPs1); } catch { /* best effort */ }
            }
        }

        // ── Download ISO ───────────────────────────────────────────────────────

        public static async Task DownloadIsoAsync(
            string url,
            string outputPath,
            IProgress<string> log,
            IProgress<(long downloaded, long total)> byteProgress,
            CancellationToken ct)
        {
            log.Report("[Cleanse10] Starting ISO download…");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;

            using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                byteProgress.Report((downloaded, total));
            }

            log.Report($"[Cleanse10] Download complete → {outputPath}");
        }

        // ── Extract install.wim from ISO ───────────────────────────────────────

        public static async Task<string> ExtractInstallWimAsync(
            string isoPath,
            string outputFolder,
            IProgress<string> log,
            IProgress<(long copied, long total)> byteProgress,
            CancellationToken ct)
        {
            log.Report("[Cleanse10] Mounting ISO…");
            Directory.CreateDirectory(outputFolder);

            string safeIso = isoPath.Replace("'", "''");

            string mountScript =
                $"$img = Mount-DiskImage -ImagePath '{safeIso}' -PassThru\n" +
                "$vol = $img | Get-Volume\n" +
                "Write-Output $vol.DriveLetter";

            string driveLetter = (await RunInlinePsAsync(mountScript, ct)).Trim();

            if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length != 1)
                throw new InvalidOperationException(
                    $"Could not determine ISO mount drive letter (got: '{driveLetter}').");

            // Prefer install.wim; fall back to install.esd (common on Microsoft-distributed ISOs).
            string wimSrc;
            string destFileName;
            string wimCandidate = $@"{driveLetter}:\sources\install.wim";
            string esdCandidate = $@"{driveLetter}:\sources\install.esd";
            if (File.Exists(wimCandidate))
            {
                wimSrc      = wimCandidate;
                destFileName = "install.wim";
            }
            else if (File.Exists(esdCandidate))
            {
                wimSrc      = esdCandidate;
                destFileName = "install.esd";
            }
            else
            {
                throw new FileNotFoundException(
                    $"Neither install.wim nor install.esd found in mounted ISO at {driveLetter}:\\sources\\");
            }

            string wimDest = Path.Combine(outputFolder, destFileName);

            try
            {
                long totalBytes = new FileInfo(wimSrc).Length;
                log.Report($"[Cleanse10] Copying {destFileName} ({totalBytes / 1_048_576} MB)…");

                // Virtual ISO drives (Mount-DiskImage) do not support overlapped I/O.
                // FileStream with useAsync:false → FileOptions.None (no FILE_FLAG_OVERLAPPED),
                // which avoids ERROR_INVALID_FUNCTION on virtual DVD drives.
                await Task.Run(() =>
                {
                    using var src2 = new FileStream(wimSrc,  FileMode.Open,   FileAccess.Read,
                                                    FileShare.Read,  1 << 16, useAsync: false);
                    using var dst2 = new FileStream(wimDest, FileMode.Create, FileAccess.Write,
                                                    FileShare.None,  1 << 16, useAsync: false);
                    var  buffer = new byte[1 << 16];
                    long copied = 0;
                    long total  = src2.Length;
                    int  read;
                    while ((read = src2.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dst2.Write(buffer, 0, read);
                        copied += read;
                        byteProgress.Report((copied, total));
                    }
                }, CancellationToken.None); // ct checked inside loop; don't cancel the Task itself

                if (ct.IsCancellationRequested)
                {
                    try { File.Delete(wimDest); } catch { /* best effort */ }
                    throw new OperationCanceledException(ct);
                }
            }
            finally
            {
                log.Report("[Cleanse10] Unmounting ISO…");
                try
                {
                    await RunInlinePsAsync(
                        $"Dismount-DiskImage -ImagePath '{safeIso}'",
                        CancellationToken.None);
                }
                catch { /* best effort */ }
            }

            log.Report($"[Cleanse10] Extracted → {wimDest}");
            return wimDest;
        }

        // ── PowerShell helpers ─────────────────────────────────────────────────

        private static async Task<string> RunPs1Async(string ps1Path, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-ExecutionPolicy Bypass -NonInteractive -NoProfile -File \"{ps1Path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)!;
            // Read stdout and wait for exit concurrently so neither can deadlock the other.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch
            {
                if (!proc.HasExited)
                    try { proc.Kill(); } catch { /* best effort */ }
                throw;
            }
            return await stdoutTask;
        }

        private static async Task<string> RunInlinePsAsync(string script, CancellationToken ct)
        {
            string temp = Path.Combine(Path.GetTempPath(),
                                        $"cleanse10_ps_{Guid.NewGuid():N}.ps1");
            try
            {
                await File.WriteAllTextAsync(temp, script, ct);
                return await RunPs1Async(temp, ct);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }

    }
}
