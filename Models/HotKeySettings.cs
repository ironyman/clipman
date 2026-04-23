namespace Clipman.Models;

public sealed class HotKeySettings
{
    public string Modifier { get; set; } = "Control+Shift";
    public string Key { get; set; } = "V";

    public override string ToString() => $"{Modifier}+{Key}".Replace("Control", "Ctrl", StringComparison.Ordinal);
}
