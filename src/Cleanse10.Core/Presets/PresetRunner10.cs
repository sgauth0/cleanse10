using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cleanse10.Core.Bloat;
using Cleanse10.Core.Components;
using Cleanse10.Core.Drivers;
using Cleanse10.Core.Tweaks;
using Cleanse10.Core.Unattended;
using Cleanse10.Core.Updates;

namespace Cleanse10.Core.Presets
{
    /// <summary>
    /// Orchestrates all operations for a given <see cref="Preset10"/> against an already-mounted image.
    /// Caller is responsible for mounting and unmounting the WIM; this class operates purely on the
    /// directory tree at <c>mountPath</c>.
    /// </summary>
    public class PresetRunner10
    {
        private readonly string  _mountPath;
        private readonly Preset10 _preset;

        // Optional extras
        public string? DriverFolder  { get; init; }
        public string? UpdateFolder  { get; init; }
        public UnattendedConfig? UnattendedConfig { get; init; }

        public PresetRunner10(string mountPath, Preset10 preset)
        {
            _mountPath = mountPath;
            _preset    = preset;
        }

        /// <summary>
        /// Runs the full preset pipeline:
        ///   1. Remove bloatware AppX packages
        ///   2. Remove optional Windows features
        ///   3. Apply offline registry tweaks
        ///   4. Inject drivers (if DriverFolder supplied)
        ///   5. Integrate updates (if UpdateFolder supplied)
        ///   6. Write unattend.xml (if UnattendedConfig supplied)
        ///   7. For Claw: set up developer environment
        /// </summary>
        public async Task RunAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var def = PresetCatalog.Get(_preset);
            progress?.Report($"[Preset] Starting preset: {def.Name} — {def.Tagline}");

            // ── 1. Bloatware removal ─────────────────────────────────────────
            progress?.Report("[Preset] Phase 1/7 — Removing bloatware…");
            var bloatMgr = new BloatwareManager();
            await bloatMgr.RemovePackagesAsync(_mountPath, BloatwareList10.Packages, progress, ct);

            // ── 2. Optional feature removal ──────────────────────────────────
            progress?.Report("[Preset] Phase 2/7 — Removing optional features…");
            var compMgr = new ComponentManager();
            await compMgr.RemoveFeaturesAsync(_mountPath, GetFeatureList(), progress, ct);

            // ── 2b. Optional feature enablement (10nix: WSL2) ────────────────
            var featuresToEnable = GetFeaturesToEnable();
            if (featuresToEnable.Count > 0)
            {
                progress?.Report("[Preset] Phase 2/7 — Enabling optional features…");
                await compMgr.EnableFeaturesAsync(_mountPath, featuresToEnable, progress, ct);
            }

            // ── 3. Offline registry tweaks ───────────────────────────────────
            progress?.Report("[Preset] Phase 3/7 — Applying offline registry tweaks…");
            await OfflineTweaks10.ApplyAllAsync(_mountPath, progress, ct);

            // Apply additional preset-specific tweaks via TweakApplicator
            var extraTweaks = GetExtraTweaks();
            if (extraTweaks.Count > 0)
            {
                var applicator = new TweakApplicator(_mountPath);
                await applicator.ApplyAsync(extraTweaks, progress, ct);
            }

            // ── 4. Driver injection ──────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(DriverFolder))
            {
                progress?.Report("[Preset] Phase 4/7 — Injecting drivers…");
                var driverMgr = new DriverManager();
                await driverMgr.AddDriversFromFolderAsync(_mountPath, DriverFolder, progress, ct);
            }
            else
            {
                progress?.Report("[Preset] Phase 4/7 — No driver folder specified, skipping.");
            }

            // ── 5. Update integration ────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(UpdateFolder))
            {
                progress?.Report("[Preset] Phase 5/7 — Integrating updates…");
                var updater = new UpdateIntegrator();
                await updater.IntegrateUpdatesAsync(_mountPath, UpdateFolder, progress, ct);
            }
            else
            {
                progress?.Report("[Preset] Phase 5/7 — No update folder specified, skipping.");
            }

            // ── 6. Unattended XML ────────────────────────────────────────────
            if (UnattendedConfig != null)
            {
                progress?.Report("[Preset] Phase 6/7 — Writing unattend.xml…");
                UnattendedGenerator.WriteToImage(UnattendedConfig, _mountPath);
            }
            else
            {
                progress?.Report("[Preset] Phase 6/7 — No unattended config, skipping.");
            }

            // ── 7. Claw dev environment / first-boot scripts ─────────────────
            if (_preset == Preset10.Claw)
            {
                progress?.Report("[Preset] Phase 7/7 — Configuring developer environment (10claw)…");
                await SetupClawEnvironmentAsync(_mountPath, progress, ct);
            }
            else if (_preset == Preset10.Priv)
            {
                progress?.Report("[Preset] Phase 7/7 — Writing privacy first-boot script (10priv)…");
                await SetupPrivEnvironmentAsync(_mountPath, progress, ct);
            }
            else if (_preset == Preset10.Ux)
            {
                progress?.Report("[Preset] Phase 7/7 — Writing Linux-like first-boot script (10nix)…");
                await SetupUxEnvironmentAsync(_mountPath, progress, ct);
            }
            else
            {
                progress?.Report("[Preset] Phase 7/7 — No first-boot script for this preset, skipping.");
            }

            progress?.Report($"[Preset] {def.Name} complete.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Feature lists per preset
        // ──────────────────────────────────────────────────────────────────────

        private IReadOnlyList<string> GetFeatureList()
        {
            // Base features removed by all presets
            var features = new List<string>(ComponentManager.DefaultRemoveList);

            return features;
        }

        private IReadOnlyList<string> GetFeaturesToEnable()
        {
            // Only 10nix needs WSL2 features enabled offline
            if (_preset == Preset10.Ux)
            {
                return new[]
                {
                    "VirtualMachinePlatform",
                    "Microsoft-Windows-Subsystem-Linux",
                };
            }

            return [];
        }

        // ──────────────────────────────────────────────────────────────────────
        // Extra per-preset registry tweaks (on top of OfflineTweaks10)
        // ──────────────────────────────────────────────────────────────────────

        private List<TweakDefinition> GetExtraTweaks()
        {
            var tweaks = new List<TweakDefinition>();

            switch (_preset)
            {
                case Preset10.Priv:
                    // Maximum privacy: add all privacy + telemetry tweaks from catalog
                    tweaks.AddRange(TweakCatalog.GetByTags(TweakCatalog.TagPrivacy, TweakCatalog.TagTelemetry));
                    break;

                case Preset10.Ux:
                    // 10nix: dark mode, performance, privacy, and UX tweaks all apply
                    tweaks.AddRange(TweakCatalog.GetByTags(
                        TweakCatalog.TagUX,
                        TweakCatalog.TagPerformance,
                        TweakCatalog.TagPrivacy,
                        TweakCatalog.TagTelemetry));
                    break;

                case Preset10.Claw:
                    // Performance tweaks needed to run OpenClaw at full speed (spec §3)
                    tweaks.AddRange(TweakCatalog.GetByTags(TweakCatalog.TagBase, TweakCatalog.TagPerformance));
                    break;

                case Preset10.Lite:
                default:
                    // Base set already applied by OfflineTweaks10
                    tweaks.AddRange(TweakCatalog.GetByTags(TweakCatalog.TagBase));
                    break;
            }

            return tweaks;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 10priv first-boot script
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a first-boot RunOnce script for the 10priv preset.
        /// Handles things that require an online session:
        ///   - Disable scheduled telemetry / data-collection tasks
        ///   - Append telemetry host entries to C:\Windows\System32\drivers\etc\hosts
        /// </summary>
        private static async Task SetupPrivEnvironmentAsync(
            string mountPath,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            string scriptDir  = Path.Combine(mountPath, "Windows", "Setup", "Scripts");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = Path.Combine(scriptDir, "PrivSetup.ps1");

            await File.WriteAllTextAsync(scriptPath, PrivBootstrapScript, ct);
            progress?.Report($"[Priv] Bootstrap script written to: {scriptPath}");

            await HiveManager.LoadHivesAsync(mountPath, progress, ct);
            try
            {
                SetString(
                    $@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    "PrivSetup",
                    @"powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""C:\Windows\Setup\Scripts\PrivSetup.ps1""");
            }
            finally
            {
                await HiveManager.UnloadHivesAsync(progress, ct);
            }

            progress?.Report("[Priv] Privacy first-boot script configured.");
        }

        private const string PrivBootstrapScript = @"
# Cleanse10 — 10priv first-boot privacy hardening
# Runs once on first user login as Administrator.
# Disables scheduled telemetry tasks, blocks telemetry hosts, and stops
# services that require a running system to stop cleanly.

param()
$ErrorActionPreference = 'Stop'

function Log { param($msg) Write-Host ""[Priv] $msg"" }

Log 'Starting 10priv first-boot privacy hardening...'

# ── 1. Disable remaining services (require online session to stop) ────────────
$svcs = @(
    'Spooler',       # Print Spooler — disable if no printer connected
    'Fax',           # Fax service
    'PeerDistSvc',   # BranchCache — enterprise peer caching
    'SessionEnv',    # Remote Desktop Configuration
    'TermService',   # Remote Desktop Services
    'UmRdpService'   # Remote Desktop Services UserMode Port Redirector
)
foreach ($svc in $svcs) {
    try {
        Set-Service -Name $svc -StartupType Disabled -ErrorAction Stop
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        Log ""Disabled service: $svc""
    } catch {
        Log ""[WARN] Could not disable service $svc : $($_.Exception.Message)""
    }
}

# ── 2. Disable telemetry / data-collection scheduled tasks ───────────────────
$tasks = @(
    '\Microsoft\Windows\Application Experience\AitAgent',
    '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
    '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
    '\Microsoft\Windows\Application Experience\StartupAppTask',
    '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
    '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
    '\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask',
    '\Microsoft\Windows\Autochk\Proxy',
    '\Microsoft\Windows\CloudExperienceHost\CreateObjectTask',
    '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector',
    '\Microsoft\Windows\DiskFootprint\Diagnostics',
    '\Microsoft\Windows\Feedback\Siuf\DmClient',
    '\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload',
    '\Microsoft\Windows\Maps\MapsToastTask',
    '\Microsoft\Windows\Maps\MapsUpdateTask',
    '\Microsoft\Windows\Maps\MapsDownloadTask',
    '\Microsoft\Windows\MemoryDiagnostic\MemoryDiagnosticResultsNotifier',
    '\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem',
    '\Microsoft\Windows\Shell\CollectTelemetryData',
    '\Microsoft\Windows\Shell\FamilySafetyMonitor',
    '\Microsoft\Windows\Shell\FamilySafetyRefreshTask',
    '\Microsoft\Windows\Windows Error Reporting\QueueReporting',
    '\Microsoft\Windows\WindowsUpdate\Automatic App Update',
    '\Microsoft\Windows\WindowsUpdate\Scheduled Start',
    '\Microsoft\Windows\UpdateOrchestrator\Schedule Retry Scan',
    '\Microsoft\Windows\SettingSync\BackgroundUploadTask',
    '\Microsoft\Windows\Defrag\ScheduledDefrag',
    '\Microsoft\Windows\FileHistory\File History (maintenance mode)',
    '\Microsoft\XblGameSave\XblGameSaveTask',
    '\Microsoft\XblGameSave\XblGameSaveTaskLogon'
)
foreach ($task in $tasks) {
    try {
        Disable-ScheduledTask -TaskPath (Split-Path $task -Parent) -TaskName (Split-Path $task -Leaf) -ErrorAction Stop | Out-Null
        Log ""Disabled task: $task""
    } catch {
        Log ""[WARN] Could not disable task $task : $($_.Exception.Message)""
    }
}

# ── 3. Block telemetry hosts via system hosts file ────────────────────────────
Log 'Appending telemetry hosts blocklist to hosts file...'
$hostsPath = ""$env:SystemRoot\System32\drivers\etc\hosts""
$blocklist = @(
    '0.0.0.0 vortex.data.microsoft.com',
    '0.0.0.0 vortex-win.data.microsoft.com',
    '0.0.0.0 telecommand.telemetry.microsoft.com',
    '0.0.0.0 telecommand.telemetry.microsoft.com.nsatc.net',
    '0.0.0.0 oca.telemetry.microsoft.com',
    '0.0.0.0 oca.telemetry.microsoft.com.nsatc.net',
    '0.0.0.0 sqm.telemetry.microsoft.com',
    '0.0.0.0 sqm.telemetry.microsoft.com.nsatc.net',
    '0.0.0.0 watson.telemetry.microsoft.com',
    '0.0.0.0 watson.telemetry.microsoft.com.nsatc.net',
    '0.0.0.0 redir.metaservices.microsoft.com',
    '0.0.0.0 choice.microsoft.com',
    '0.0.0.0 choice.microsoft.com.nsatc.net',
    '0.0.0.0 df.telemetry.microsoft.com',
    '0.0.0.0 reports.wes.df.telemetry.microsoft.com',
    '0.0.0.0 services.wes.df.telemetry.microsoft.com',
    '0.0.0.0 sqm.microsoft.com',
    '0.0.0.0 telemetry.microsoft.com',
    '0.0.0.0 watson.microsoft.com',
    '0.0.0.0 statsfe2.ws.microsoft.com',
    '0.0.0.0 corpext.msitadfs.glbdns2.microsoft.com',
    '0.0.0.0 compatible.telemetry.microsoft.com',
    '0.0.0.0 geo.settings-win.data.microsoft.com.akadns.net',
    '0.0.0.0 settings-win.data.microsoft.com',
    '0.0.0.0 v10.events.data.microsoft.com',
    '0.0.0.0 v20.events.data.microsoft.com'
)
$existing = Get-Content $hostsPath -Raw -ErrorAction SilentlyContinue
$marker   = '# Cleanse10 10priv telemetry block'
if ($existing -notmatch [regex]::Escape($marker)) {
    Add-Content -Path $hostsPath -Value ([char]10 + $marker)
    foreach ($entry in $blocklist) {
        Add-Content -Path $hostsPath -Value $entry
    }
    Log 'Hosts file updated.'
} else {
    Log 'Hosts file already contains 10priv block — skipping.'
}

# ── 4. Self-delete ────────────────────────────────────────────────────────────
Log '10priv first-boot complete.'
Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

        // ──────────────────────────────────────────────────────────────────────
        // 10nix first-boot script
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a first-boot RunOnce script for the 10nix preset.
        /// Handles things that require an online session:
        ///   - Install WSL2 + Ubuntu 24.04 and configure it
        ///   - Install scoop + FOSS app stack (GlazeWM, WezTerm, Nerd Fonts, CLI tools)
        ///   - Switch to High Performance power plan
        ///   - Set visual effects to best performance
        ///   - Disable superfluous scheduled tasks
        /// </summary>
        private static async Task SetupUxEnvironmentAsync(
            string mountPath,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            string scriptDir  = Path.Combine(mountPath, "Windows", "Setup", "Scripts");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = Path.Combine(scriptDir, "NixSetup.ps1");

            await File.WriteAllTextAsync(scriptPath, NixBootstrapScript, ct);
            progress?.Report($"[Nix] Bootstrap script written to: {scriptPath}");

            await HiveManager.LoadHivesAsync(mountPath, progress, ct);
            try
            {
                SetString(
                    $@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    "NixSetup",
                    @"powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""C:\Windows\Setup\Scripts\NixSetup.ps1""");
            }
            finally
            {
                await HiveManager.UnloadHivesAsync(progress, ct);
            }

            progress?.Report("[Nix] Linux-like environment first-boot script configured.");
        }

        private const string NixBootstrapScript = @"
# Cleanse10 — 10nix first-boot Linux-like environment setup
# Runs once on first user login as Administrator.
# Installs WSL2, FOSS desktop stack (GlazeWM, WezTerm, scoop tools),
# sets dark mode, power plan, and visual effects.

param()
$ErrorActionPreference = 'Continue'

function Log { param($msg) Write-Host ""[Nix] $msg"" }

Log 'Starting 10nix first-boot Linux-like environment setup...'

# ── 1. Install WSL2 kernel update + Ubuntu 24.04 ─────────────────────────────
Log 'Enabling WSL2 and installing Ubuntu 24.04...'
try {
    wsl --install -d Ubuntu-24.04 --no-launch
    Log 'WSL2 + Ubuntu 24.04 installed. Reboot may be required before first use.'
} catch {
    Log ""[WARN] WSL install step failed (may need reboot first): $($_.Exception.Message)""
}

# ── 2. WSL2 post-install configuration (wsl.conf + .wslconfig) ───────────────
Log 'Writing WSL2 configuration...'
$wslConfig = @'
[wsl2]
memory=4GB
processors=4
swap=4GB
localhostForwarding=true
'@
$wslConfigPath = Join-Path $env:USERPROFILE '.wslconfig'
if (-not (Test-Path $wslConfigPath)) {
    Set-Content -Path $wslConfigPath -Value $wslConfig -Encoding UTF8
    Log '.wslconfig written.'
} else {
    Log '.wslconfig already exists — skipping.'
}

# ── 3. High Performance power plan ───────────────────────────────────────────
Log 'Activating High Performance power plan...'
$hp = powercfg /list | Select-String 'High performance'
if ($hp) {
    $guid = ($hp.ToString().Trim() -split '\s+')[3]
    powercfg /setactive $guid
    Log ""High Performance plan active: $guid""
} else {
    powercfg /duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c | Out-Null
    powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
    Log 'High Performance plan restored and activated.'
}

# ── 4. Visual effects — best performance ─────────────────────────────────────
Log 'Setting visual effects to best performance...'
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name 'VisualFXSetting' -Value 2 -Type DWord -Force

# ── 5. Install scoop (user-level, no admin needed) ────────────────────────────
Log 'Installing scoop...'
if (-not (Get-Command scoop -ErrorAction SilentlyContinue)) {
    Invoke-Expression (New-Object Net.WebClient).DownloadString('https://get.scoop.sh')
    Log 'scoop installed.'
} else {
    Log 'scoop already present.'
}

# Add scoop buckets
foreach ($bucket in @('extras', 'nerd-fonts')) {
    scoop bucket add $bucket 2>$null
}

# ── 6. Install FOSS desktop stack via winget ─────────────────────────────────
Log 'Installing FOSS desktop stack via winget...'
$wingetApps = @(
    'GlazeWM.GlazeWM',
    'amnweb.yet-another-status-bar',
    'wez.wezterm',
    'Mozilla.Firefox',
    'VideoLAN.VLC',
    'Obsidian.Obsidian',
    'Notepad++.Notepad++',
    'Starship.Starship',
    'Microsoft.PowerToys',
    'Git.Git',
    'File-New-Project.EarTrumpet'
)
foreach ($app in $wingetApps) {
    Log ""Installing $app...""
    winget install --id $app -e --accept-source-agreements --accept-package-agreements --silent 2>$null
}

# ── 7. Install CLI tools + Nerd Fonts via scoop ───────────────────────────────
Log 'Installing CLI tools via scoop...'
$scoopTools = @('neovim', 'ripgrep', 'fd', 'fzf', 'bat', 'eza', 'zoxide', 'jq', 'lazygit', 'tldr', 'ncdu')
foreach ($tool in $scoopTools) {
    scoop install $tool 2>$null
}

Log 'Installing Nerd Fonts via scoop...'
scoop install nerd-fonts/JetBrainsMono-NF 2>$null

# ── 8. Disable low-value scheduled tasks ─────────────────────────────────────
Log 'Disabling low-value scheduled tasks...'
$tasks = @(
    '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
    '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
    '\Microsoft\Windows\Application Experience\StartupAppTask',
    '\Microsoft\Windows\Maps\MapsToastTask',
    '\Microsoft\Windows\Maps\MapsUpdateTask',
    '\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem',
    '\Microsoft\Windows\Defrag\ScheduledDefrag',
    '\Microsoft\Windows\DiskCleanup\SilentCleanup',
    '\Microsoft\Windows\Maintenance\WinSAT'
)
foreach ($task in $tasks) {
    try {
        Disable-ScheduledTask -TaskPath (Split-Path $task -Parent) -TaskName (Split-Path $task -Leaf) -ErrorAction Stop | Out-Null
        Log ""Disabled task: $task""
    } catch {
        Log ""[WARN] Could not disable task $task : $($_.Exception.Message)""
    }
}

# ── 9. Auto-hide taskbar (status bar will be the only bar) ────────────────────
Log 'Auto-hiding Windows taskbar...'
$tbKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
if (Test-Path $tbKey) {
    $settings = (Get-ItemProperty -Path $tbKey -Name 'Settings').Settings
    $settings[8] = $settings[8] -bor 1
    Set-ItemProperty -Path $tbKey -Name 'Settings' -Value $settings
    Log 'Taskbar auto-hide enabled.'
}

# ── 10. Self-delete ────────────────────────────────────────────────────────────
Log '10nix first-boot complete. Please reboot to finish WSL2 setup.'
Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

        // ──────────────────────────────────────────────────────────────────────
        // 10claw dev environment
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Configures the offline image for the 10claw developer environment.
        ///
        /// Because WSL2, winget, and MSYS2 require an online/running system, the bulk
        /// of Claw setup happens at first boot via a RunOnce script written to the image.
        /// This method writes:
        ///   - Developer Mode registry key
        ///   - A PowerShell bootstrap script to C:\Windows\Setup\Scripts\ClawSetup.ps1
        ///   - A RunOnce entry that launches the script on first login
        /// </summary>
        private static async Task SetupClawEnvironmentAsync(
            string mountPath,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            // Write bootstrap script first (pure file I/O, no hive needed)
            string scriptDir  = Path.Combine(mountPath, "Windows", "Setup", "Scripts");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = Path.Combine(scriptDir, "ClawSetup.ps1");

            await File.WriteAllTextAsync(scriptPath, ClawBootstrapScript, ct);
            progress?.Report($"[Claw] Bootstrap script written to: {scriptPath}");

            // Single load/unload pair: enable Developer Mode and write RunOnce entry together
            await HiveManager.LoadHivesAsync(mountPath, progress, ct);
            try
            {
                SetDword(
                    $@"HKLM\{HiveManager.SoftwareMount}\Microsoft\Windows\CurrentVersion\AppModelUnlock",
                    "AllowDevelopmentWithoutDevLicense", 1);
                SetDword(
                    $@"HKLM\{HiveManager.SoftwareMount}\Microsoft\Windows\CurrentVersion\AppModelUnlock",
                    "AllowAllTrustedApps", 1);

                SetString(
                    $@"HKLM\{HiveManager.DefaultUser}\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    "ClawSetup",
                    @"powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""C:\Windows\Setup\Scripts\ClawSetup.ps1""");
            }
            finally
            {
                await HiveManager.UnloadHivesAsync(progress, ct);
            }

            progress?.Report("[Claw] Developer environment bootstrap configured.");
        }

        private const string ClawBootstrapScript = @"
# Cleanse10 — 10claw first-boot OpenClaw build environment setup
# Runs once on first user login as Administrator.
# Installs the Windows-native toolchain required to build OpenClaw from source
# per the OpenClaw Optimization Guide (cmake -G ""Visual Studio 17 2022"" -A Win32).

param()
$ErrorActionPreference = 'Stop'

function Log { param($msg) Write-Host ""[Claw] $msg"" }

Log 'Starting 10claw OpenClaw build environment setup...'

# ── 1. Visual C++ Redistributables (required to run OpenClaw pre-built binaries) ─
Log 'Installing Visual C++ Redistributables...'
winget install --id Microsoft.VCRedist.2015+.x64 -e --accept-source-agreements --accept-package-agreements --silent
winget install --id Microsoft.VCRedist.2015+.x86 -e --accept-source-agreements --accept-package-agreements --silent

# ── 2. Git (clone OpenClaw repo with submodules) ─────────────────────────────────
Log 'Installing Git...'
winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements --silent

# ── 3. CMake 3.20+ (build system generator) ──────────────────────────────────────
Log 'Installing CMake...'
winget install --id Kitware.CMake -e --accept-source-agreements --accept-package-agreements --silent

# ── 4. Visual Studio Build Tools 2022 with C++ workload ──────────────────────────
# Includes MSVC compiler, linker, Windows 10 SDK — required for:
#   cmake -G ""Visual Studio 17 2022"" -A Win32
Log 'Installing Visual Studio Build Tools 2022 (C++ workload, Windows 10 SDK)...'
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --accept-source-agreements --accept-package-agreements --silent --override ""--add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows10SDK.19041 --quiet --wait --norestart""

# ── 5. Reload PATH so git/cmake are available in this session ─────────────────────
$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')

# ── 6. Reminder ───────────────────────────────────────────────────────────────────
Log 'Build environment ready.'
Log 'To build OpenClaw:'
Log '  git clone --recursive https://github.com/pjasicek/OpenClaw.git'
Log '  cd OpenClaw && mkdir build && cd build'
Log '  cmake -G ""Visual Studio 17 2022"" -A Win32 ..'
Log '  cmake --build . --config Release'
Log '  (then copy CLAW.REZ from your Captain Claw installation into the output folder)'

# ── 7. Self-delete this RunOnce script ────────────────────────────────────────────
Log '10claw setup complete.'
Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

        // ──────────────────────────────────────────────────────────────────────
        // Registry helpers (require hives already loaded)
        // ──────────────────────────────────────────────────────────────────────

        private static void SetDword(string fullKeyPath, string valueName, int data)
        {
            string sub = fullKeyPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
                ? fullKeyPath[5..] : fullKeyPath;
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(sub, writable: true);
            key?.SetValue(valueName, data, Microsoft.Win32.RegistryValueKind.DWord);
        }

        private static void SetString(string fullKeyPath, string valueName, string data)
        {
            string sub = fullKeyPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
                ? fullKeyPath[5..] : fullKeyPath;
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(sub, writable: true);
            key?.SetValue(valueName, data, Microsoft.Win32.RegistryValueKind.String);
        }
    }
}
