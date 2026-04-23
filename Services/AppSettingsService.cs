using System.Text.Json;
using Clipman.Models;

namespace Clipman.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipman");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public HotKeySettings LoadHotKey()
    {
        if (!File.Exists(_settingsPath))
        {
            return new HotKeySettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<HotKeySettings>(File.ReadAllText(_settingsPath));
            return Normalize(settings);
        }
        catch
        {
            return new HotKeySettings();
        }
    }

    public void SaveHotKey(HotKeySettings settings)
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Normalize(settings), new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static HotKeySettings Normalize(HotKeySettings? settings)
    {
        if (settings is null)
        {
            return new HotKeySettings();
        }

        settings.Modifier = string.IsNullOrWhiteSpace(settings.Modifier) ? "Control+Shift" : settings.Modifier;
        settings.Key = string.IsNullOrWhiteSpace(settings.Key) ? "V" : settings.Key.Trim().ToUpperInvariant();
        return settings;
    }
}
