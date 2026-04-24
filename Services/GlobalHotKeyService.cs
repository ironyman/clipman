using System.Runtime.InteropServices;
using Clipman.Models;

namespace Clipman.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyBaseId = 0x434D;
    private const int GwlWndProc = -4;
    private const int WmHotKey = 0x0312;
    private readonly IntPtr _hwnd;
    private readonly WndProc _newWndProc;
    private readonly Dictionary<int, HotKeyAction> _registeredActions = [];
    private IntPtr _oldWndProc;
    private bool _disposed;

    public GlobalHotKeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _newWndProc = WindowProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_newWndProc));
    }

    public event EventHandler<HotKeyAction>? Pressed;
    public event Func<uint, IntPtr, IntPtr, bool>? WindowMessageReceived;

    public bool Register(HotKeySettings settings)
    {
        Unregister();

        var succeeded = true;
        succeeded &= TryRegisterBinding(HotKeyAction.ToggleWindow, settings.ToggleWindow);
        succeeded &= TryRegisterBinding(HotKeyAction.ToggleRightPanel, settings.ToggleRightPanel);

        for (var i = 0; i < 9; i++)
        {
            if (i >= settings.PasteRecent.Count)
            {
                break;
            }

            var action = (HotKeyAction)((int)HotKeyAction.PasteRecent1 + i);
            succeeded &= TryRegisterBinding(action, settings.PasteRecent[i]);
        }

        return succeeded;
    }

    public void Unregister()
    {
        foreach (var id in _registeredActions.Keys.ToArray())
        {
            UnregisterHotKey(_hwnd, id);
        }

        _registeredActions.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (_oldWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            _oldWndProc = IntPtr.Zero;
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (WindowMessageReceived?.Invoke(msg, wParam, lParam) == true)
        {
            return IntPtr.Zero;
        }

        if (msg == WmHotKey && _registeredActions.TryGetValue(wParam.ToInt32(), out var action))
        {
            Pressed?.Invoke(this, action);
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private static uint ParseModifiers(string modifier)
    {
        uint value = 0;
        foreach (var part in modifier.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            value |= part.ToLowerInvariant() switch
            {
                "alt" => 0x0001u,
                "control" or "ctrl" => 0x0002u,
                "shift" => 0x0004u,
                "win" or "windows" => 0x0008u,
                _ => 0u
            };
        }

        return value;
    }

    private static uint ParseKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        key = key.Trim().ToUpperInvariant();
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return key[0];
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            return (uint)(0x70 + functionKey - 1);
        }

        return key switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            _ => 0
        };
    }

    private bool TryRegisterBinding(HotKeyAction action, HotKeyBinding binding)
    {
        var modifiers = ParseModifiers(binding.Modifier);
        var key = ParseKey(binding.Key);
        if (modifiers == 0 || key == 0)
        {
            return false;
        }

        var id = HotKeyBaseId + (int)action;
        if (!RegisterHotKey(_hwnd, id, modifiers, key))
        {
            return false;
        }

        _registeredActions[id] = action;
        return true;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newProc) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, newProc) : SetWindowLong32(hwnd, index, newProc);

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
