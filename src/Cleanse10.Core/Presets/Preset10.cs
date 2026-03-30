namespace Cleanse10.Core.Presets
{
    /// <summary>The four fixed presets available in Cleanse10.</summary>
    public enum Preset10
    {
        /// <summary>Clean base: stripped of bloat, telemetry, Edge junk. Minimal footprint.</summary>
        Lite,

        /// <summary>Clean base + Windows-native OpenClaw build environment (Git, CMake, VS Build Tools 2022, VC++ redistributables). Performance tweaks applied.</summary>
        Claw,

        /// <summary>Clean base with maximum privacy and anti-telemetry hardening.</summary>
        Priv,

        /// <summary>Clean base transformed into a Linux-like Windows environment with WSL2, GlazeWM, WezTerm, and FOSS toolchain.</summary>
        Ux,
    }

    /// <summary>Static metadata for each preset.</summary>
    public record PresetDefinition(
        Preset10 Preset,
        string   Name,
        string   Tagline,
        string   Icon,        // Unicode/emoji glyph for display
        string   Description,
        string[] Removes,
        string[] Adds
    );

    public static class PresetCatalog
    {
        public static readonly PresetDefinition[] All =
        {
            new(Preset10.Lite,
                "10lite",
                "Stripped clean. Nothing extra.",
                "\uE81E",  // Segoe MDL2: Clear
                "Clean Windows 10 base — every preset starts here.",
                new[]
                {
                    "DiagTrack & dmwappushservice (telemetry)",
                    "Edge WebSearch & Bing integration",
                    "Cortana",
                    "Xbox Game Bar & Xbox services",
                    "OneDrive auto-install stub",
                    "Consumer apps (Mail, Maps, Solitaire, Spotify, etc.)",
                    "Windows Feedback Hub",
                    "Mixed Reality Portal",
                    "Teams (consumer)",
                },
                System.Array.Empty<string>()),

            new(Preset10.Claw,
                "10claw",
                "Built to run and build OpenClaw.",
                "\uE943",  // Segoe MDL2: Code
                "Everything in 10lite, plus the Windows-native toolchain to build OpenClaw from source.",
                new[]
                {
                    "DiagTrack & dmwappushservice (telemetry)",
                    "Edge WebSearch & Bing integration",
                    "Cortana",
                    "Xbox Game Bar & Xbox services",
                    "OneDrive auto-install stub",
                    "Consumer bloatware & Feedback Hub",
                },
                new[]
                {
                    "Visual C++ Redistributables 2015–2022 (x64 + x86)",
                    "Git (for cloning OpenClaw repo with submodules)",
                    "CMake 3.20+ (build system generator)",
                    "Visual Studio Build Tools 2022 (C++ workload, Windows 10 SDK)",
                    "Developer Mode enabled",
                    "Performance tweaks: SystemResponsiveness=0, foreground CPU boost",
                }),

            new(Preset10.Priv,
                "10priv",
                "Your data stays yours.",
                "\uE72E",  // Segoe MDL2: Lock
                "Everything in 10lite with aggressive privacy hardening on top.",
                new[]
                {
                    "DiagTrack & dmwappushservice (telemetry)",
                    "Edge WebSearch & Bing integration",
                    "Cortana",
                    "Xbox Game Bar & Xbox services",
                    "OneDrive auto-install stub",
                    "Consumer bloatware & Feedback Hub",
                    "Advertising ID",
                    "Location services & all sensors",
                    "Activity history & timeline",
                    "Clipboard history sync",
                    "Windows Error Reporting (service + policy)",
                    "Speech model update & typing analytics",
                    "Diagnostics Hub, Remote Registry, Link Tracking",
                    "Network Data Usage Monitor (ndu)",
                    "SMBv1 (security risk)",
                },
                new[]
                {
                    "Telemetry hosts blocklist (/etc/hosts) — first-boot",
                    "Scheduled telemetry tasks disabled — first-boot",
                    "Group policy: no data collection (all levels)",
                    "Delivery Optimization P2P sharing disabled",
                    "SmartScreen network calls disabled",
                    "UWP permission gates: camera, mic, location blocked",
                    "Input personalisation & typing analytics disabled",
                    "Bing search in Start menu disabled",
                    "Feedback frequency: never",
                    "Browser language fingerprinting opt-out",
                }),

            new(Preset10.Ux,
                "10nix",
                "Windows that thinks like Linux.",
                "\uE756",  // Segoe MDL2: Globe (Linux/open-world vibe)
                "Everything in 10lite, plus WSL2, GlazeWM tiling WM, WezTerm, scoop, and a full FOSS desktop stack.",
                new[]
                {
                    "DiagTrack & dmwappushservice (telemetry)",
                    "Edge WebSearch & Bing integration",
                    "Cortana",
                    "Xbox Game Bar & Xbox services",
                    "OneDrive auto-install stub",
                    "Consumer bloatware & Feedback Hub",
                    "News & Interests taskbar widget",
                    "Light mode (replaced by system dark mode)",
                    "Superfluous visual animations",
                    "NTFS last-access timestamp writes",
                    "Low-value scheduled tasks (Compat Appraiser, Maps, WinSAT) — first-boot",
                },
                new[]
                {
                    "WSL2 features enabled offline (VirtualMachinePlatform + WSL)",
                    "Ubuntu 24.04 LTS installed via wsl --install — first-boot",
                    ".wslconfig written (memory/CPU/swap limits) — first-boot",
                    "GlazeWM (i3-style tiling WM) — first-boot",
                    "YASB (Yet Another Status Bar) — first-boot",
                    "WezTerm (GPU-accelerated, Lua-configured terminal) — first-boot",
                    "Firefox, VLC, Obsidian, Notepad++, PowerToys — first-boot",
                    "Starship cross-shell prompt — first-boot",
                    "scoop + CLI tools: neovim, ripgrep, fzf, bat, eza, zoxide, lazygit — first-boot",
                    "JetBrainsMono Nerd Font — first-boot",
                    "System dark mode enabled",
                    "News & Interests feed disabled",
                    "High Performance power plan — first-boot",
                    "Visual effects set to best performance — first-boot",
                    "Foreground process boost (Win32PrioritySeparation=38)",
                    "SuperFetch disabled",
                    "Taskbar auto-hidden (status bar replaces it) — first-boot",
                }),
        };

        public static PresetDefinition Get(Preset10 preset)
        {
            foreach (var def in All)
                if (def.Preset == preset) return def;
            throw new System.ArgumentOutOfRangeException(nameof(preset));
        }
    }
}
