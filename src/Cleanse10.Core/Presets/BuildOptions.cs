namespace Cleanse10.Core.Presets;

/// <summary>
/// User-configurable options collected before running a preset.
/// Drives unattend.xml generation and other pre-build choices.
/// </summary>
public record BuildOptions
{
    /// <summary>
    /// Desired computer name written to unattend.xml.
    /// Null or empty = auto-generate (<c>*</c>).
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// When <c>true</c> the image will include an unattend.xml that skips OOBE,
    /// accepts the EULA automatically, and creates the local administrator account
    /// specified by <see cref="AdminUsername"/> / <see cref="AdminPassword"/>.
    /// </summary>
    public bool AfkInstall { get; init; }

    /// <summary>
    /// Local administrator account username.
    /// Only applied when <see cref="AfkInstall"/> is <c>true</c>.
    /// </summary>
    public string? AdminUsername { get; init; }

    /// <summary>
    /// Local administrator account password (plaintext).
    /// Only applied when <see cref="AfkInstall"/> is <c>true</c>.
    /// </summary>
    public string? AdminPassword { get; init; }
}
