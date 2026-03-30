using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Cleanse10.Core.Tweaks
{
    /// <summary>
    /// Central catalog of all registry tweaks available in Cleanse10.
    /// Tweaks are tagged so presets can select subsets.
    /// </summary>
    public static class TweakCatalog
    {
        // ──────────────────────────────────────────────────────────────────────
        // Tag constants
        // ──────────────────────────────────────────────────────────────────────
        public const string TagPrivacy     = "privacy";
        public const string TagTelemetry   = "telemetry";
        public const string TagPerformance = "performance";
        public const string TagUX          = "ux";
        public const string TagBase        = "base";       // applied by every preset

        // ──────────────────────────────────────────────────────────────────────
        // Full catalog
        // ──────────────────────────────────────────────────────────────────────
        public static readonly IReadOnlyList<TweakDefinition> All = new List<TweakDefinition>
        {
            // ── Disable telemetry / data collection ──────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry",      0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable telemetry (policy)"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable telemetry (user policy)"),
            new(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start",                      4, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable Connected User Experience and Telemetry service"),
            new(@"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start",               4, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable WAP Push Message Routing service"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory",         1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable Application Compatibility inventory"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisablePcaUI",             1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable Program Compatibility Assistant"),
            new(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable",               0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable CEIP"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry),
            new(@"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry),
            // Prevent Windows Update from contacting Windows Update internet locations (use WSUS / no auto-fetch)
            new(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Block WU internet connections"),

            // Cover 32-bit apps that read the Wow6432Node path
            new(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable telemetry (32-bit app policy path)"),
            // Feedback notification suppression — implements "Feedback frequency: never"
            new(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Suppress feedback/SIUF notifications"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "LimitDiagnosticLogCollection", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Limit diagnostic log collection"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "LimitDumpCollection",           1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Limit crash dump uploads"),
            // App-V CEIP
            new(@"SOFTWARE\Policies\Microsoft\AppV\CEIP", "CEIPEnable", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable App-V CEIP"),
            // Application Impact Telemetry + User Activity Reporter
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable",   0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable Application Impact Telemetry"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableUAR",  1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable User Activity Reporter"),

            // ── Disable Windows Error Reporting ──────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled",              1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable Windows Error Reporting"),
            new(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled",                       1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "DontSendAdditionalData", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Block WER additional data upload"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "LoggingDisabled",        1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagTelemetry, "Disable WER event logging"),

            // ── Disable Cortana (Win10) ───────────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana",        0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable Cortana"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortanaAboveLock", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowSearchToUseLocation", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch",    1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),

            // ── Disable advertising ID ────────────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable advertising ID"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled",      0, RegistryValueKind.DWord, TweakHive.DefaultUser,   TagPrivacy),

            // ── Disable activity history / timeline ───────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities",       0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable activity history"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities",        0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),

            // ── Disable location services ─────────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable location services"),

            // ── Disable SmartScreen network calls ─────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen",            0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable SmartScreen (policy)"),
            new(@"SOFTWARE\Policies\Microsoft\MicrosoftEdge\PhishingFilter", "EnabledV9",      0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable Edge SmartScreen / phishing filter"),

            // ── Disable cloud clipboard ───────────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory",       0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),
            new(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard",   0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy),

            // ── Delivery Optimization — disable P2P update sharing ────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 100, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable Delivery Optimization P2P sharing"),

            // ── Cloud content / soft-landing ──────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding",           1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable cloud soft-landing suggestions"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableCloudOptimizedContent", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable cloud-optimised content"),

            // ── UWP app permission gates (Force Deny = 2) ─────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessCamera",      2, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Block UWP access to camera"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMicrophone",  2, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Block UWP access to microphone"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessLocation",    2, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Block UWP access to location"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsGetDiagnosticInfo", 2, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Block UWP access to diagnostic info"),

            // ── Input personalisation / typing analytics ──────────────────────
            new(@"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization",              0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable inking/typing cloud learning"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocationScripting",        1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable location via scripting"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableSensors",                  1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable all sensors"),
            // Speech data collection — implements the "Speech recognition service" removal claim
            new(@"SOFTWARE\Policies\Microsoft\Speech", "AllowSpeechModelUpdate",                              0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable speech model update / data collection"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\TextInput", "AllowLinguisticDataCollection", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPrivacy, "Disable typing analytics upload"),

            // ── DefaultUser: tailored experiences & content delivery ──────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Disable tailored experiences with diagnostic data"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "ContentDeliveryAllowed",      0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Block content delivery manager"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEnabled",     0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEverEnabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "OemPreInstalledAppsEnabled",  0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Disable settings suggestions (353696)"),

            // ── DefaultUser: input personalisation ────────────────────────────
            new(@"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Restrict implicit text collection"),
            new(@"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection",  1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Restrict implicit ink collection"),
            new(@"SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Disable contact harvesting for personalisation"),

            // ── DefaultUser: feedback frequency = never ───────────────────────
            new(@"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Set feedback frequency to never"),

            // ── DefaultUser: Bing in Start / Cortana consent ──────────────────
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Disable Bing search in Start menu"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent",    0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Revoke Cortana consent"),

            // ── DefaultUser: browser language fingerprinting ──────────────────
            new(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagPrivacy, "Opt out of browser language list for websites"),

            // ── Disable auto-updates (let admin control) ──────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate",      1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagBase, "Disable automatic Windows Update install"),
            new(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions",         2, RegistryValueKind.DWord, TweakHive.LocalMachine, TagBase, "Set AU to notify only"),

            // ── Performance tweaks ────────────────────────────────────────────
            new(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 3, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPerformance, "Enable prefetcher"),
            new(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnableSuperfetch", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPerformance, "Disable SuperFetch (SysMain)"),
            new(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPerformance),
            new(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPerformance, "Foreground app priority boost"),
            // Disable NTFS last-access timestamp writes — reduces disk I/O on every file access
            new(@"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagPerformance, "Disable NTFS last-access timestamp"),

            // ── UX tweaks ─────────────────────────────────────────────────────
            // Dark mode (10nix) — system + apps
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme",   0, RegistryValueKind.DWord, TweakHive.DefaultUser,  TagUX, "Enable dark mode for apps"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Enable dark mode for system UI"),
            // News & Interests feed (disable taskbar widget — 10nix)
            new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds",          0, RegistryValueKind.DWord, TweakHive.LocalMachine, TagUX, "Disable News and Interests taskbar widget (policy)"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", 2, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Hide News and Interests from taskbar"),

            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Show file extensions"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden",      1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Show hidden files"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo",    1, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Open Explorer to This PC"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Hide OneDrive sync notifications"),
            new(@"Control Panel\Desktop", "MenuShowDelay",                                    "0", RegistryValueKind.String, TweakHive.DefaultUser, TagUX, "Remove menu delay"),
            new(@"Control Panel\Desktop", "AutoEndTasks",                                    "1", RegistryValueKind.String, TweakHive.DefaultUser, TagPerformance, "Auto-end unresponsive tasks"),
            new(@"Control Panel\Desktop", "WaitToKillAppTimeout",                            "2000", RegistryValueKind.String, TweakHive.DefaultUser, TagPerformance),
            new(@"Control Panel\Desktop", "HungAppTimeout",                                  "2000", RegistryValueKind.String, TweakHive.DefaultUser, TagPerformance),
            // Disable Start menu recently used program and document tracking
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsMenu",  1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagUX, "Disable recent docs in Start menu"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs",  0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Disable Start menu program tracking"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs",   0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagUX, "Disable Start menu document tracking"),

            // ── Disable OneDrive setup nag ────────────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC",       1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagBase, "Prevent OneDrive from running at setup"),

            // ── Disable suggested / sponsored apps ────────────────────────────
            new(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord, TweakHive.LocalMachine, TagBase, "Disable consumer feature installs"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagBase, "Disable silent app installs"),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagBase),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagBase),
            new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord, TweakHive.DefaultUser, TagBase),
        };

        /// <summary>Returns all tweaks that have one of the given tags (OR logic).</summary>
        public static IEnumerable<TweakDefinition> GetByTags(params string[] tags)
        {
            var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            return All.Where(t => t.Tag != null && set.Contains(t.Tag));
        }
    }
}
