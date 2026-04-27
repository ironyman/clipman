namespace Clipman.Models;

public sealed class HotKeyBinding
{
    public string Modifier { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool? IsGlobal { get; set; }

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return "None";
        }

        return string.IsNullOrWhiteSpace(Modifier)
            ? Key
            : $"{Modifier}+{Key}".Replace("Control", "Ctrl", StringComparison.Ordinal);
    }
}

public sealed class HotKeySettings
{
    // Legacy fields for migration from older settings.json shape.
    public string Modifier { get; set; } = "Alt";
    public string Key { get; set; } = "V";

    public HotKeyBinding ToggleWindow { get; set; } = new()
    {
        Modifier = "Alt",
        Key = "V",
        IsGlobal = true
    };

    public HotKeyBinding PasteSelected { get; set; } = new()
    {
        Modifier = string.Empty,
        Key = "Enter",
        IsGlobal = false
    };

    public HotKeyBinding TogglePin { get; set; } = new()
    {
        Modifier = "Control",
        Key = "P",
        IsGlobal = false
    };

    public HotKeyBinding ToggleRightPanel { get; set; } = new()
    {
        Modifier = "Control",
        Key = "E",
        IsGlobal = false
    };

    public HotKeyBinding FileSearchMode { get; set; } = new()
    {
        Modifier = "Control+Shift",
        Key = "S",
        IsGlobal = true
    };

    public List<HotKeyBinding> PasteRecent { get; set; } =
    [
        new() { Modifier = "Alt", Key = "1", IsGlobal = false },
        new() { Modifier = "Alt", Key = "2", IsGlobal = false },
        new() { Modifier = "Alt", Key = "3", IsGlobal = false },
        new() { Modifier = "Alt", Key = "4", IsGlobal = false },
        new() { Modifier = "Alt", Key = "5", IsGlobal = false },
        new() { Modifier = "Alt", Key = "6", IsGlobal = false },
        new() { Modifier = "Alt", Key = "7", IsGlobal = false },
        new() { Modifier = "Alt", Key = "8", IsGlobal = false },
        new() { Modifier = "Alt", Key = "9", IsGlobal = false }
    ];

    public bool StartOnWindowsBoot { get; set; }
    public bool FileSearchServiceEnabled { get; set; } = false;
    public bool DetailsPanelExpanded { get; set; } = true;

    public override string ToString() => ToggleWindow.ToString();
}
