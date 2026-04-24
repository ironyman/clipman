namespace Clipman.Models;

public sealed class HotKeyBinding
{
    public string Modifier { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

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
    public string Modifier { get; set; } = "Control+Shift";
    public string Key { get; set; } = "V";

    public HotKeyBinding ToggleWindow { get; set; } = new()
    {
        Modifier = "Control+Shift",
        Key = "V"
    };

    public HotKeyBinding PasteSelected { get; set; } = new()
    {
        Modifier = string.Empty,
        Key = "Enter"
    };

    public HotKeyBinding TogglePin { get; set; } = new()
    {
        Modifier = "Control",
        Key = "P"
    };

    public HotKeyBinding ToggleRightPanel { get; set; } = new()
    {
        Modifier = "Control",
        Key = "E"
    };

    public List<HotKeyBinding> PasteRecent { get; set; } =
    [
        new() { Modifier = "Alt", Key = "1" },
        new() { Modifier = "Alt", Key = "2" },
        new() { Modifier = "Alt", Key = "3" },
        new() { Modifier = "Alt", Key = "4" },
        new() { Modifier = "Alt", Key = "5" },
        new() { Modifier = "Alt", Key = "6" },
        new() { Modifier = "Alt", Key = "7" },
        new() { Modifier = "Alt", Key = "8" },
        new() { Modifier = "Alt", Key = "9" }
    ];

    public bool StartOnWindowsBoot { get; set; }

    public override string ToString() => ToggleWindow.ToString();
}
