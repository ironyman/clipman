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

        // Migrate legacy settings format.
        if (string.IsNullOrWhiteSpace(settings.ToggleWindow.Key))
        {
            settings.ToggleWindow.Modifier = string.IsNullOrWhiteSpace(settings.Modifier) ? "Control+Shift" : settings.Modifier.Trim();
            settings.ToggleWindow.Key = string.IsNullOrWhiteSpace(settings.Key) ? "V" : settings.Key.Trim();
        }

        settings.ToggleWindow = NormalizeBinding(settings.ToggleWindow, "Control+Shift", "V", defaultIsGlobal: true);
        settings.PasteSelected = NormalizeBinding(settings.PasteSelected, string.Empty, "Enter", defaultIsGlobal: false);
        settings.TogglePin = NormalizeBinding(settings.TogglePin, "Control", "P", defaultIsGlobal: false);
        settings.ToggleRightPanel = NormalizeBinding(settings.ToggleRightPanel, "Control", "E", defaultIsGlobal: false);
        settings.FileSearchMode = NormalizeBinding(settings.FileSearchMode, "Control+Shift", "S", defaultIsGlobal: true);

        if (settings.PasteRecent is null || settings.PasteRecent.Count != 9)
        {
            settings.PasteRecent = Enumerable.Range(1, 9)
                .Select(index => new HotKeyBinding
                {
                    Modifier = "Alt",
                    Key = index.ToString(),
                    IsGlobal = false
                })
                .ToList();
        }

        for (var i = 0; i < settings.PasteRecent.Count; i++)
        {
            settings.PasteRecent[i] = NormalizeBinding(settings.PasteRecent[i], "Alt", (i + 1).ToString(), defaultIsGlobal: false);
        }

        // Keep legacy fields in sync for backwards compatibility.
        settings.Modifier = settings.ToggleWindow.Modifier;
        settings.Key = settings.ToggleWindow.Key;
        return settings;
    }

    private static HotKeyBinding NormalizeBinding(HotKeyBinding? binding, string defaultModifier, string defaultKey, bool defaultIsGlobal)
    {
        var normalized = binding ?? new HotKeyBinding
        {
            Modifier = defaultModifier,
            Key = defaultKey,
            IsGlobal = defaultIsGlobal
        };
        normalized.Modifier = normalized.Modifier?.Trim() ?? string.Empty;
        normalized.Key = normalized.Key?.Trim() ?? string.Empty;
        normalized.IsGlobal ??= defaultIsGlobal;
        if (string.IsNullOrWhiteSpace(normalized.Key))
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(normalized.Modifier) && !string.IsNullOrWhiteSpace(defaultModifier) && string.Equals(normalized.Key, defaultKey, StringComparison.OrdinalIgnoreCase))
        {
            normalized.Modifier = defaultModifier;
        }

        normalized.Key = NormalizeKey(normalized.Key);
        normalized.Modifier = NormalizeModifier(normalized.Modifier);
        return normalized;
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        key = key.Trim();
        return key.Length == 1 ? key.ToUpperInvariant() : key;
    }

    private static string NormalizeModifier(string modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier))
        {
            return string.Empty;
        }

        var parts = modifier
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant() switch
            {
                "ctrl" => "Control",
                "control" => "Control",
                "alt" => "Alt",
                "shift" => "Shift",
                "win" => "Win",
                "windows" => "Win",
                _ => part
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join('+', parts);
    }
}
