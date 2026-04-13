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
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
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

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"PowerShell failed with exit code {proc.ExitCode}.\n{stderr}{stdout}");

            return stdout;
        }

    }
}
