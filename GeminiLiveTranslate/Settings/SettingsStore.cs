using System.IO;
using System.Text.Json;
using GeminiLiveTranslate.Diagnostics;

namespace GeminiLiveTranslate.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "gemini-live-translate-dotnet");

    public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public AppSettings Load()
    {
        WindowSizeDiagnostics.Log(
            "settings-load-start",
            details: $"path={SettingsPath}; exists={File.Exists(SettingsPath)}");
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
                settings.Normalize();
                WindowSizeDiagnostics.Log("settings-load-success", settings, details: $"path={SettingsPath}");
                return settings;
            }
        }
        catch (Exception ex)
        {
            WindowSizeDiagnostics.Log(
                "settings-load-failed",
                details: $"path={SettingsPath}; error={ex.GetType().Name}: {ex.Message}");
            // Corrupt settings should not prevent startup; save will rewrite a valid file.
        }

        var defaults = new AppSettings();
        WindowSizeDiagnostics.Log("settings-load-defaults", defaults, details: $"path={SettingsPath}");
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
