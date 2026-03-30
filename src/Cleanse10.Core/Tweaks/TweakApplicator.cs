using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Cleanse10.Core.Tweaks
{
    /// <summary>
    /// Applies a list of <see cref="TweakDefinition"/> objects to an offline (mounted) Windows 10 image
    /// by loading the relevant registry hives, writing the values, and unloading cleanly.
    /// </summary>
    public class TweakApplicator
    {
        private readonly string _mountPath;

        public TweakApplicator(string mountPath)
        {
            _mountPath = mountPath;
        }

        public async Task ApplyAsync(
            IEnumerable<TweakDefinition> tweaks,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            // Separate tweaks by hive so we load each hive only once
            var lmTweaks      = new List<TweakDefinition>();
            var defaultTweaks = new List<TweakDefinition>();

            foreach (var t in tweaks)
            {
                switch (t.Hive)
                {
                    case TweakHive.DefaultUser:
                        defaultTweaks.Add(t);
                        break;
                    default:
                        lmTweaks.Add(t);
                        break;
                }
            }

            if (lmTweaks.Count > 0)
                await ApplyHiveGroupAsync(lmTweaks, "HKLM", progress, ct);

            if (defaultTweaks.Count > 0)
                await ApplyDefaultUserAsync(defaultTweaks, progress, ct);
        }

        // ──────────────────────────────────────────────────────────────────────
        // HKLM-based hives (SOFTWARE, SYSTEM, etc.)
        // ──────────────────────────────────────────────────────────────────────

        private async Task ApplyHiveGroupAsync(
            IEnumerable<TweakDefinition> tweaks,
            string baseKey,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            // Load SOFTWARE hive
            string softwareHive = Path.Combine(_mountPath, "Windows", "System32", "config", "SOFTWARE");
            string systemHive   = Path.Combine(_mountPath, "Windows", "System32", "config", "SYSTEM");
            const string softwareMount = "CLEANSE10_SOFTWARE";
            const string systemMount   = "CLEANSE10_SYSTEM";

            await RegLoadAsync(softwareHive, $@"HKLM\{softwareMount}", progress, ct);
            await RegLoadAsync(systemHive,   $@"HKLM\{systemMount}",   progress, ct);

            try
            {
                foreach (var t in tweaks)
                {
                    ct.ThrowIfCancellationRequested();

                    string resolvedKey = ResolveKey(t.Key, softwareMount, systemMount);
                    progress?.Report($"[Tweaks] {resolvedKey}\\{t.ValueName} = {t.Value}");

                    using var key = Registry.LocalMachine.CreateSubKey(resolvedKey, writable: true);
                    key?.SetValue(t.ValueName, t.Value, t.Kind);
                }
            }
            finally
            {
                await RegUnloadAsync($@"HKLM\{softwareMount}", progress, ct);
                await RegUnloadAsync($@"HKLM\{systemMount}",   progress, ct);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Default user hive (NTUSER.DAT)
        // ──────────────────────────────────────────────────────────────────────

        private async Task ApplyDefaultUserAsync(
            IEnumerable<TweakDefinition> tweaks,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            string ntuserHive = Path.Combine(_mountPath, "Users", "Default", "NTUSER.DAT");
            if (!File.Exists(ntuserHive))
            {
                progress?.Report("[Tweaks] Default user NTUSER.DAT not found — skipping default-user tweaks.");
                return;
            }

            const string mountPoint = "CLEANSE10_DEFUSER";

            // Best-effort cleanup: unload any stale mount under this name before loading.
            // The handle MUST be closed before calling reg.exe unload (same bug pattern
            // as HiveManager.UnloadHivesAsync — open handles block RegUnLoadKey).
            bool staleExists;
            using (var k = Registry.LocalMachine.OpenSubKey(mountPoint))
                staleExists = k != null;
            if (staleExists)
                await RegUnloadAsync($@"HKLM\{mountPoint}", null, ct);

            await RegLoadAsync(ntuserHive, $@"HKLM\{mountPoint}", progress, ct);

            // Verify the hive was actually mounted before writing to it.
            // reg.exe exits 1 silently when the file is locked by another mount name;
            // CLEANSE10_DEFUSER would not exist as an HKLM key, and CreateSubKey would
            // throw ERROR_INVALID_PARAMETER trying to create a top-level key.
            bool hiveLoaded;
            using (var hiveKey = Registry.LocalMachine.OpenSubKey(mountPoint))
                hiveLoaded = hiveKey != null;

            if (!hiveLoaded)
            {
                progress?.Report("[WARN] [Tweaks] DefaultUser hive failed to load — skipping default-user tweaks.");
                return;
            }

            try
            {
                foreach (var t in tweaks)
                {
                    ct.ThrowIfCancellationRequested();
                    string subKey = $@"{mountPoint}\{t.Key.TrimStart('\\')}";
                    progress?.Report($"[Tweaks][DefaultUser] {t.Key}\\{t.ValueName} = {t.Value}");

                    using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true);
                    key?.SetValue(t.ValueName, t.Value, t.Kind);
                }
            }
            finally
            {
                await RegUnloadAsync($@"HKLM\{mountPoint}", progress, ct);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static string ResolveKey(string key, string softwareMount, string systemMount)
        {
            // Allow callers to write keys relative to HKLM\SOFTWARE or HKLM\SYSTEM
            if (key.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
                return $@"{softwareMount}\{key.Substring("SOFTWARE".Length).TrimStart('\\')}";

            if (key.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
                return $@"{systemMount}\{key.Substring("SYSTEM".Length).TrimStart('\\')}";

            return key;
        }

        private static Task RegLoadAsync(string hivePath, string regPath, IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report($"[Tweaks] Loading hive: {regPath}");
            return RunRegAsync($"load \"{regPath}\" \"{hivePath}\"", progress, ct);
        }

        private static Task RegUnloadAsync(string regPath, IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report($"[Tweaks] Unloading hive: {regPath}");
            return RunRegAsync($"unload \"{regPath}\"", progress, ct);
        }

        private static async Task RunRegAsync(string args, IProgress<string>? progress, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var p = System.Diagnostics.Process.Start(psi)
                          ?? throw new Exception("Failed to start reg.exe");

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
