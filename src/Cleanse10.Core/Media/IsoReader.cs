using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils;
using DiscUtils.Udf;

namespace Cleanse10.Core.Media;

/// <summary>
/// Reads ISO files directly using DiscUtils (UDF parser) — no virtual DVD
/// driver or <c>Mount-DiskImage</c> required.  This avoids the CDROM.sys bug
/// that returns <c>ERROR_INVALID_FUNCTION</c> when reading large files
/// (≥ ~4.2 GB) from a mounted ISO.
/// </summary>
public static class IsoReader
{
    // ──────────────────────────────────────────────────────────────────────
    // ── Extract a single file ──────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts <paramref name="isoRelativePath"/> from the ISO into
    /// <paramref name="destinationPath"/>.  Reports byte-level progress.
    /// </summary>
    public static async Task ExtractFileAsync(
        string isoPath,
        string isoRelativePath,
        string destinationPath,
        IProgress<string>? log = null,
        IProgress<(long copied, long total)>? byteProgress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(isoPath))
            throw new FileNotFoundException($"ISO not found: {isoPath}");

        await Task.Run(() =>
        {
            using var isoStream = File.OpenRead(isoPath);
            using var udf = new UdfReader(isoStream);

            string normalised = NormalisePath(isoRelativePath);
            if (!udf.FileExists(normalised))
                throw new FileNotFoundException(
                    $"'{isoRelativePath}' not found inside ISO.");

            var info = udf.GetFileInfo(normalised);
            long total = info.Length;
            log?.Report($"[ISO] Extracting {isoRelativePath} ({total / 1_048_576} MB)…");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            using var src = udf.OpenFile(normalised, FileMode.Open, FileAccess.Read);
            using var dst = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.SequentialScan);

            byte[] buf = new byte[81920];
            long copied = 0;
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dst.Write(buf, 0, read);
                copied += read;
                byteProgress?.Report((copied, total));
            }

            log?.Report($"[ISO] Extracted → {destinationPath}");
        }, ct);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ── Find the install image path inside the ISO ─────────────────────────
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ISO-relative path to <c>sources\install.wim</c> or
    /// <c>sources\install.esd</c>, whichever exists.
    /// </summary>
    public static string FindInstallImage(string isoPath)
    {
        using var isoStream = File.OpenRead(isoPath);
        using var udf = new UdfReader(isoStream);

        if (udf.FileExists(@"\sources\install.wim"))
            return @"sources\install.wim";

        if (udf.FileExists(@"\sources\install.esd"))
            return @"sources\install.esd";

        throw new FileNotFoundException(
            "Neither install.wim nor install.esd found in ISO.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // ── Stage entire ISO tree (excluding install image) ────────────────────
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts every file from the ISO into <paramref name="destinationRoot"/>,
    /// preserving directory structure, but skipping <c>install.wim</c> and
    /// <c>install.esd</c> in the <c>sources</c> directory.
    /// </summary>
    public static async Task StageContentsAsync(
        string isoPath,
        string destinationRoot,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(isoPath))
            throw new FileNotFoundException($"ISO not found: {isoPath}");

        await Task.Run(() =>
        {
            using var isoStream = File.OpenRead(isoPath);
            using var udf = new UdfReader(isoStream);

            log?.Report("[ISO] Staging ISO contents…");
            Directory.CreateDirectory(destinationRoot);

            CopyDirectory(udf, @"\", destinationRoot, log, ct);

            log?.Report("[ISO] ISO contents staged.");
        }, ct);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ── Private helpers ───────────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────────

    private static void CopyDirectory(
        UdfReader udf,
        string isoDir,
        string localDir,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(localDir);

        foreach (string filePath in udf.GetFiles(isoDir))
        {
            ct.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(filePath);

            // Skip the install image — it's handled separately via ExportImageToWimAsync.
            if (isoDir.TrimStart('\\').Equals("sources", StringComparison.OrdinalIgnoreCase)
                && (fileName.Equals("install.wim", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("install.esd", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Skip any pre-existing autounattend.xml at the ISO root so that it
            // only appears in the output when we intentionally generate one.
            if (isoDir.TrimStart('\\').Length == 0
                && fileName.Equals("autounattend.xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destPath = Path.Combine(localDir, fileName);
            using var src = udf.OpenFile(filePath, FileMode.Open, FileAccess.Read);
            using var dst = new FileStream(
                destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.SequentialScan);

            byte[] buf = new byte[81920];
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dst.Write(buf, 0, read);
            }
        }

        foreach (string subDir in udf.GetDirectories(isoDir))
        {
            ct.ThrowIfCancellationRequested();
            string dirName = Path.GetFileName(subDir);
            CopyDirectory(udf, subDir, Path.Combine(localDir, dirName), log, ct);
        }
    }

    private static string NormalisePath(string relativePath)
    {
        // DiscUtils UDF expects paths starting with '\'
        string p = relativePath.Replace('/', '\\');
        if (!p.StartsWith('\\'))
            p = @"\" + p;
        return p;
    }
}
