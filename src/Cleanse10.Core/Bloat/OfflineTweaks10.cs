using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Cleanse10.Core.Bloat
{
    /// <summary>
    /// Applies offline registry tweaks specific to Windows 10.
    ///
    /// IMPORTANT: No TPM / SecureBoot / CPU bypass keys — those are Win11 LabConfig tricks.
    /// No Copilot policy keys — Copilot is not a Win10 feature.
    /// </summary>
    public static class OfflineTweaks10
    {
        /// <summary>
        /// Loads the offline hives, writes all tweaks, then unloads.
        /// Must be called with the image already mounted at <paramref name="mountPath"/>.
        /// </summary>
        public static async Task ApplyAllAsync(
            string mountPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            await HiveManager.LoadHivesAsync(mountPath, progress, ct);
            try
            {
                ApplyTelemetryTweaks(progress);
                ApplyPrivacyTweaks(progress);
                ApplyServiceTweaks(progress);
                ApplyUXTweaks(progress);
                DisableOneDriveSetup(mountPath, progress);
            }
            finally
            {
                await HiveManager.UnloadHivesAsync(progress, ct);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Telemetry
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyTelemetryTweaks(IProgress<string>? progress)
        {
            progress?.Report("[Tweaks] Applying telemetry tweaks…");

            // Telemetry level 0 = Security only (requires Enterprise/Education; on Home/Pro this floors at 1)
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Microsoft\Windows\CurrentVersion\Policies\DataCollection",
                "AllowTelemetry", 0);

            // Connected User Experiences & Telemetry service (DiagTrack) — disabled
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\DiagTrack",
                "Start", 4);
            // WAP Push Message Routing service
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\dmwappushservice",
                "Start", 4);

            // Application Compatibility inventory
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\AppCompat",
                "DisableInventory", 1);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\AppCompat",
                "DisablePcaUI", 1);

            // CEIP
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\SQMClient\Windows",
                "CEIPEnable", 0);

            // Windows Error Reporting
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Error Reporting",
                "Disabled", 1);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Microsoft\Windows\Windows Error Reporting",
                "Disabled", 1);

            // Handwriting telemetry
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\HandwritingErrorReports",
                "PreventHandwritingErrorReports", 1);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\TabletPC",
                "PreventHandwritingDataSharing", 1);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Privacy
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyPrivacyTweaks(IProgress<string>? progress)
        {
            progress?.Report("[Tweaks] Applying privacy tweaks…");

            // Cortana (Win10 — policy-based disable)
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Search",
                "AllowCortana", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Search",
                "AllowCortanaAboveLock", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Search",
                "AllowSearchToUseLocation", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Search",
                "ConnectedSearchUseWeb", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\Windows Search",
                "DisableWebSearch", 1);

            // Advertising ID
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\AdvertisingInfo",
                "DisabledByGroupPolicy", 1);

            // Activity history
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\System",
                "PublishUserActivities", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\System",
                "UploadUserActivities", 0);

            // Location
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\LocationAndSensors",
                "DisableLocation", 1);

            // Clipboard sync
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\System",
                "AllowClipboardHistory", 0);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\System",
                "AllowCrossDeviceClipboard", 0);

            // Consumer features (sponsored app installs)
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\CloudContent",
                "DisableWindowsConsumerFeatures", 1);

            // Default user: advertising ID, suggested apps
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                "Enabled", 0);
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SilentInstalledAppsEnabled", 0);
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SystemPaneSuggestionsEnabled", 0);
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SubscribedContent-338388Enabled", 0);
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SubscribedContent-338389Enabled", 0);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Services
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyServiceTweaks(IProgress<string>? progress)
        {
            progress?.Report("[Tweaks] Applying service tweaks…");

            // Windows Update — notify only (let admin decide when to install)
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\WindowsUpdate\AU",
                "NoAutoUpdate", 1);
            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\WindowsUpdate\AU",
                "AUOptions", 2);

            // SysMain (SuperFetch) — disable (saves RAM on constrained systems)
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\SysMain",
                "Start", 4);

            // Windows Search indexer — set to manual (user can re-enable)
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\WSearch",
                "Start", 3);

            // Xbox services — not needed on debloated images
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\XblAuthManager",
                "Start", 4);
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\XblGameSave",
                "Start", 4);
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\XboxNetApiSvc",
                "Start", 4);
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\BcastDVRUserService",
                "Start", 4);

            // Windows Media Player network sharing
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\WMPNetworkSvc",
                "Start", 4);

            // Geolocation service
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\lfsvc",
                "Start", 4);

            // Maps broker (downloaded maps)
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\MapsBroker",
                "Start", 4);

            // Windows Insider Program
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\wisvc",
                "Start", 4);

            // Retail Demo service
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\RetailDemo",
                "Start", 4);

            // Spatial awareness (Mixed Reality / HoloLens)
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\SharedRealitySvc",
                "Start", 4);

            // Diagnostics Hub Standard Collector
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\diagnosticshub.standardcollector.service",
                "Start", 4);

            // Windows Error Reporting service
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\WerSvc",
                "Start", 4);

            // Remote Registry — no reason to expose registry remotely on a clean image
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\RemoteRegistry",
                "Start", 4);

            // Distributed Link Tracking Client — phones home to track file moves across volumes
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\TrkWks",
                "Start", 4);

            // Network Data Usage Monitor — collects per-app network telemetry
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Services\ndu",
                "Start", 4);
        }

        // ──────────────────────────────────────────────────────────────────────
        // UX / responsiveness
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyUXTweaks(IProgress<string>? progress)
        {
            progress?.Report("[Tweaks] Applying UX tweaks…");

            // Explorer defaults for all new users (NTUSER.DAT)
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "HideFileExt", 0);              // show extensions
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Hidden", 1);                    // show hidden files
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "LaunchTo", 1);                  // open Explorer to "This PC"
            SetDword($@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowSyncProviderNotifications", 0);

            // Menu responsiveness
            SetString($@"HKLM\{HiveManager.DefaultUser}\Control Panel\Desktop", "MenuShowDelay",       "0");
            SetString($@"HKLM\{HiveManager.DefaultUser}\Control Panel\Desktop", "AutoEndTasks",        "1");
            SetString($@"HKLM\{HiveManager.DefaultUser}\Control Panel\Desktop", "WaitToKillAppTimeout","2000");
            SetString($@"HKLM\{HiveManager.DefaultUser}\Control Panel\Desktop", "HungAppTimeout",      "2000");

            // Priority boost for foreground apps
            SetDword($@"HKLM\{HiveManager.SystemMount}\CurrentControlSet\Control\PriorityControl",
                "Win32PrioritySeparation", 38);
        }

        // ──────────────────────────────────────────────────────────────────────
        // OneDrive — prevent setup auto-launch
        // ──────────────────────────────────────────────────────────────────────

        private static void DisableOneDriveSetup(string mountPath, IProgress<string>? progress)
        {
            progress?.Report("[Tweaks] Disabling OneDrive auto-setup…");

            SetDword($@"HKLM\{HiveManager.SoftwareMount}\Policies\Microsoft\Windows\OneDrive",
                "DisableFileSyncNGSC", 1);

            // Also remove the OneDriveSetup run key from the default user hive
            string runKey = $@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(runKey, writable: true);
                key?.DeleteValue("OneDriveSetup", throwOnMissingValue: false);
            }
            catch { /* best-effort */ }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Registry write helpers (operate on already-loaded hive paths)
        // ──────────────────────────────────────────────────────────────────────

        private static void SetDword(string fullKeyPath, string valueName, int data)
        {
            // fullKeyPath is like HKLM\CLEANSE10_OFFLINE_SOFTWARE\Policies\...
            // Strip "HKLM\" prefix before calling Win32 API
            string sub = fullKeyPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
                ? fullKeyPath[5..]
                : fullKeyPath;

            using var key = Registry.LocalMachine.CreateSubKey(sub, writable: true);
            key?.SetValue(valueName, data, RegistryValueKind.DWord);
        }

        private static void SetString(string fullKeyPath, string valueName, string data)
        {
            string sub = fullKeyPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
                ? fullKeyPath[5..]
                : fullKeyPath;

            using var key = Registry.LocalMachine.CreateSubKey(sub, writable: true);
            key?.SetValue(valueName, data, RegistryValueKind.String);
        }
    }
}
