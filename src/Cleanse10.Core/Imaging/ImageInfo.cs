namespace Cleanse10.Core.Imaging;

public class WimImageInfo
{
    public int Index { get; set; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string SizeFormatted => FormatSize(SizeBytes);

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        return $"{bytes / 1024.0:F2} KB";
    }
}

public class WimFileInfo
{
    public string FilePath { get; init; } = string.Empty;
    public IReadOnlyList<WimImageInfo> Images { get; init; } = [];
}
