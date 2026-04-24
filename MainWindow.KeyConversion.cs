using Clipman.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Clipman;

public sealed partial class MainWindow
{
    private static bool MatchesBinding(KeyRoutedEventArgs e, HotKeyBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Key))
        {
            return false;
        }

        if (!TryParseVirtualKey(binding.Key, out var expectedKey) || e.Key != expectedKey)
        {
            return false;
        }

        var expectedModifiers = ParseModifierFlags(binding.Modifier);
        var currentModifiers = GetCurrentModifierFlags();
        return expectedModifiers == currentModifiers;
    }

    private static int ParseModifierFlags(string modifier)
    {
        var value = 0;
        foreach (var part in modifier.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            value |= part.ToLowerInvariant() switch
            {
                "control" or "ctrl" => 1,
                "alt" => 2,
                "shift" => 4,
                "win" or "windows" => 8,
                _ => 0
            };
        }

        return value;
    }

    private static int GetCurrentModifierFlags()
    {
        var value = 0;
        if (IsKeyDown(VirtualKey.Control))
        {
            value |= 1;
        }

        if (IsKeyDown(VirtualKey.Menu))
        {
            value |= 2;
        }

        if (IsKeyDown(VirtualKey.Shift))
        {
            value |= 4;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            value |= 8;
        }

        return value;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool TryParseVirtualKey(string keyText, out VirtualKey key)
    {
        key = VirtualKey.None;
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        var normalized = keyText.Trim();
        if (normalized.Length == 1)
        {
            var c = char.ToUpperInvariant(normalized[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = (VirtualKey)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = (VirtualKey)c;
                return true;
            }
        }

        if (normalized.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[1..], out var fnKey) && fnKey is >= 1 and <= 24)
        {
            key = (VirtualKey)((int)VirtualKey.F1 + (fnKey - 1));
            return true;
        }

        key = normalized.ToUpperInvariant() switch
        {
            "ENTER" => VirtualKey.Enter,
            "SPACE" => VirtualKey.Space,
            "TAB" => VirtualKey.Tab,
            "ESC" or "ESCAPE" => VirtualKey.Escape,
            "UP" => VirtualKey.Up,
            "DOWN" => VirtualKey.Down,
            _ => VirtualKey.None
        };
        return key != VirtualKey.None;
    }

    private static string ModifierFlagsToString(int flags)
    {
        var parts = new List<string>(4);
        if ((flags & 1) != 0) parts.Add("Control");
        if ((flags & 2) != 0) parts.Add("Alt");
        if ((flags & 4) != 0) parts.Add("Shift");
        if ((flags & 8) != 0) parts.Add("Win");
        return string.Join('+', parts);
    }

    private static string VirtualKeyToHotKeyString(VirtualKey key)
    {
        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return ((char)('0' + ((int)key - (int)VirtualKey.Number0))).ToString();
        }

        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return ((char)key).ToString();
        }

        if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
        {
            return $"F{(int)key - (int)VirtualKey.F1 + 1}";
        }

        return key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Space => "Space",
            VirtualKey.Tab => "Tab",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Escape => "Esc",
            _ => key.ToString()
        };
    }
}
