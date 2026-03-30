using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleanse10.Core.Settings
{
    /// <summary>
    /// Persisted application settings for Cleanse10.
    /// Serialized as JSON to %APPDATA%\Cleanse10\settings.json.
    /// </summary>
    public class AppSettings
    {
        public string LastMountPath    { get; set; } = string.Empty;
        public string LastWimPath      { get; set; } = string.Empty;
        public string LastOutputPath   { get; set; } = string.Empty;
        public string LastDriverFolder { get; set; } = string.Empty;
        public string LastUpdateFolder { get; set; } = string.Empty;
        public bool   DarkMode         { get; set; } = false;

        // ──────────────────────────────────────────────────────────────────────
        // Persistence helpers
        // ──────────────────────────────────────────────────────────────────────

        private static readonly string _settingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cleanse10");

        private static string SettingsFile => Path.Combine(_settingsDir, "settings.json");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                }
            }
            catch
            {
                // Swallow — return defaults on any read failure
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, _jsonOptions));
            }
            catch
            {
                // Swallow — settings are best-effort
            }
        }
    }
}
