using System.Collections.Generic;

namespace Cleanse10.Core.Tweaks
{
    /// <summary>
    /// Represents the registry hive a tweak targets when applied offline.
    /// </summary>
    public enum TweakHive
    {
        LocalMachine,   // HKLM in the offline image
        DefaultUser,    // HKCU default user hive (NTUSER.DAT)
        Software,       // HKLM\SOFTWARE sub-hive
        System,         // HKLM\SYSTEM sub-hive
    }

    /// <summary>
    /// Describes a single registry tweak to be applied to an offline Windows image.
    /// </summary>
    public record TweakDefinition(
        string        Key,
        string        ValueName,
        object        Value,
        Microsoft.Win32.RegistryValueKind Kind,
        TweakHive     Hive  = TweakHive.LocalMachine,
        string?       Tag   = null,
        string?       Description = null
    );
}
