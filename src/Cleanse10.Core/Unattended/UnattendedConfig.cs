using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Cleanse10.Core.Unattended
{
    /// <summary>
    /// Settings that drive unattended XML generation for a Windows 10 image.
    /// </summary>
    public class UnattendedConfig
    {
        public string ComputerName     { get; set; } = "*";           // * = auto-generate
        public string TimeZone         { get; set; } = "UTC";
        public string UILanguage       { get; set; } = "en-US";
        public string UserLocale       { get; set; } = "en-US";
        public string InputLocale      { get; set; } = "0409:00000409";
        public bool   SkipOOBE         { get; set; } = true;
        public bool   AcceptEula       { get; set; } = true;
        public bool   HideEulaPage     { get; set; } = true;
        public bool   HideWirelessPage { get; set; } = true;
        public bool   HideLocalAccount { get; set; } = false;

        // WIM image index to install (1 = first/only edition, e.g. Windows 10 Pro)
        public int WimIndex { get; set; } = 1;

        // Local admin account (optional — leave null to skip)
        public string? AdminUsername { get; set; }
        public string? AdminPassword { get; set; }   // plaintext; generator will mark plainText=true
    }
}
