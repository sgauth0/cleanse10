using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cleanse10.Core.Bloat
{
    /// <summary>
    /// Loads and unloads offline registry hives from a mounted Windows 10 image
    /// so that other components can write registry values via the Win32 registry API.
    /// </summary>
    public static class HiveManager
    {
        // Temp mount names used under HKLM
        public const string SoftwareMount = "CLEANSE10_OFFLINE_SOFTWARE";
        public const string SystemMount   = "CLEANSE10_OFFLINE_SYSTEM";
        public const string DefaultUser   = "CLEANSE10_OFFLINE_NTUSER";

        /// <summary>
        /// Best-effort cleanup: unloads every Cleanse10 hive mount name (both
        /// <see cref="HiveManager"/> and <see cref="Cleanse10.Core.Tweaks.TweakApplicator"/>
        /// names) regardless of whether they are loaded.  Silently ignores errors so it
        /// can be called unconditionally at the start of a pipeline run to clear stale
        /// state from a previous crash.
        /// </summary>
        public static async Task CleanupAllHivesAsync(CancellationToken ct = default)
        {
            var allNames = new[]
            {
                // HiveManager names
                SoftwareMount, SystemMount, DefaultUser,
                // TweakApplicator names
                "CLEANSE10_SOFTWARE", "CLEANSE10_SYSTEM", "CLEANSE10_DEFUSER",
            };

            foreach (var name in allNames)
            {
                bool exists;
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(name))
                    exists = k != null;
                if (exists)
                    await UnloadAsync($@"HKLM\{name}", null, ct);
            }
        }

        /// <summary>Loads the SOFTWARE, SYSTEM and default-user hives from a mounted image.</summary>
        public static async Task LoadHivesAsync(string mountPath, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            // Best-effort cleanup: if a previous crashed/failed run left any of our hive
            // mounts still loaded, unload them now so we get a clean slate.  We suppress
            // progress output and ignore errors — an exit-1 from reg.exe just means the
            // hive was never loaded, which is the normal case on a first run.
            foreach (var name in new[] { SoftwareMount, SystemMount, DefaultUser })
            {
                bool stale;
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(name))
                    stale = k != null;
                if (stale)
                    await UnloadAsync($@"HKLM\{name}", null, ct);
            }

            await LoadAsync(HivePath(mountPath, "SOFTWARE"), $@"HKLM\{SoftwareMount}", progress, ct);
            await LoadAsync(HivePath(mountPath, "SYSTEM"),   $@"HKLM\{SystemMount}",   progress, ct);

            string ntuser = Path.Combine(mountPath, "Users", "Default", "NTUSER.DAT");
            if (File.Exists(ntuser))
                await LoadAsync(ntuser, $@"HKLM\{DefaultUser}", progress, ct);
            else
                progress?.Report("[HiveManager] Default NTUSER.DAT not found — skipping.");
        }

        /// <summary>Unloads all hives previously loaded by <see cref="LoadHivesAsync"/>.</summary>
        public static async Task UnloadHivesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            await UnloadAsync($@"HKLM\{SoftwareMount}", progress, ct);
            await UnloadAsync($@"HKLM\{SystemMount}",   progress, ct);

            // Only unload DefaultUser if it was actually loaded — the key exists in the
            // live HKLM only while the hive is mounted.  Attempting to unload a hive that
            // was never loaded produces a reg.exe exit-1 warning in every run's log.
            //
            // IMPORTANT: the OpenSubKey handle MUST be closed before calling UnloadAsync.
            // RegUnLoadKey requires zero open handles; holding the RegistryKey open here
            // while reg.exe unload runs causes exit-1 (ERROR_SHARING_VIOLATION), leaving
            // the hive mounted and NTUSER.DAT locked for the rest of the pipeline.
            bool hasDefaultUser;
            using (var check = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(DefaultUser))
                hasDefaultUser = check != null;

            if (hasDefaultUser)
                await UnloadAsync($@"HKLM\{DefaultUser}", progress, ct);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static string HivePath(string mountPath, string hiveName)
            => Path.Combine(mountPath, "Windows", "System32", "config", hiveName);

        private static Task LoadAsync(string hivePath, string regPath, IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report($"[HiveManager] Loading {regPath} from {hivePath}");
            return RunRegAsync($"load \"{regPath}\" \"{hivePath}\"", progress, ct);
        }

        private static Task UnloadAsync(string regPath, IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report($"[HiveManager] Unloading {regPath}");
            return RunRegAsync($"unload \"{regPath}\"", progress, ct);
        }

        private static async Task RunRegAsync(string args, IProgress<string>? progress, CancellationToken ct)
        {
            var psi = new ProcessStartInfo("reg.exe", args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var p = Process.Start(psi) ?? throw new Exception($"Failed to start reg.exe with args: {args}");
            try
            {
                await p.WaitForExitAsync(ct);
            }
            catch
            {
                if (!p.HasExited)
                    try { p.Kill(); } catch { /* best effort */ }
                throw;
            }

            if (p.ExitCode != 0)
                progress?.Report($"[WARN] reg.exe exited with {p.ExitCode} for: {args}");
        }
    }
}
