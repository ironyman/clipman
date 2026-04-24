using System.Runtime.InteropServices;
using Clipman.Models;

namespace Clipman.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyBaseId = 0x434D;
    private const int GwlWndProc = -4;
    private const int WmHotKey = 0x0312;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint VkShift = 0x10;
    private const uint VkControl = 0x11;
    private const uint VkMenu = 0x12;
    private const uint VkLWin = 0x5B;
    private const uint VkRWin = 0x5C;
    private const uint VkEscape = 0x1B;
    private readonly IntPtr _hwnd;
    private readonly WndProc _newWndProc;
    private readonly Dictionary<int, HotKeyAction> _registeredActions = [];
    private IntPtr _oldWndProc;
    private HotKeySettings? _activeSettings;
    private Action<HotKeyBinding>? _captureCallback;
    private bool _disposed;
    private bool _isConfigurationCaptureActive;

    public GlobalHotKeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _newWndProc = WindowProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_newWndProc));
    }

    public event EventHandler<HotKeyAction>? Pressed;
    public event Func<uint, IntPtr, IntPtr, bool>? WindowMessageReceived;
    public bool IsConfigurationCaptureActive => _isConfigurationCaptureActive;

    public bool Register(HotKeySettings settings)
    {
        _activeSettings = CloneSettings(settings);
        if (_isConfigurationCaptureActive)
        {
            return true;
        }

        return RegisterCore(_activeSettings);
    }

    public bool BeginConfigurationCapture(Action<HotKeyBinding> onCaptured)
    {
        if (_disposed)
        {
            return false;
        }

        _captureCallback = onCaptured ?? throw new ArgumentNullException(nameof(onCaptured));
        if (_isConfigurationCaptureActive)
        {
            return true;
        }

        _isConfigurationCaptureActive = true;
        Unregister();
        return true;
    }

    public void EndConfigurationCapture(bool restoreRegisteredHotkeys = true)
    {
        if (!_isConfigurationCaptureActive)
        {
            return;
        }

        _isConfigurationCaptureActive = false;
        _captureCallback = null;

        if (restoreRegisteredHotkeys && _activeSettings is not null)
        {
            RegisterCore(_activeSettings);
        }
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

        if (_isConfigurationCaptureActive && (msg == WmKeyDown || msg == WmSysKeyDown))
        {
            if (TryCreateCapturedBinding(wParam, out var binding))
            {
                _captureCallback?.Invoke(binding);
            }

            return IntPtr.Zero;
        }

        if (_isConfigurationCaptureActive && msg == WmHotKey)
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
        if (binding.IsGlobal != true)
        {
            return true;
        }

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

    private bool RegisterCore(HotKeySettings settings)
    {
        Unregister();

        var succeeded = true;
        succeeded &= TryRegisterBinding(HotKeyAction.ToggleWindow, settings.ToggleWindow);
        succeeded &= TryRegisterBinding(HotKeyAction.ToggleRightPanel, settings.ToggleRightPanel);
        succeeded &= TryRegisterBinding(HotKeyAction.PasteSelected, settings.PasteSelected);
        succeeded &= TryRegisterBinding(HotKeyAction.TogglePin, settings.TogglePin);

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

    private static bool TryCreateCapturedBinding(IntPtr wParam, out HotKeyBinding binding)
    {
        var virtualKey = unchecked((uint)wParam.ToInt64());
        if (virtualKey == 0 || IsModifierVirtualKey(virtualKey))
        {
            binding = default!;
            return false;
        }

        if (virtualKey == VkEscape)
        {
            binding = new HotKeyBinding
            {
                Modifier = string.Empty,
                Key = string.Empty
            };
            return true;
        }

        var key = VirtualKeyToHotKeyString(virtualKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            binding = default!;
            return false;
        }

        binding = new HotKeyBinding
        {
            Modifier = ModifierFlagsToString(GetCurrentModifierFlags()),
            Key = key
        };
        return true;
    }

    private static bool IsModifierVirtualKey(uint virtualKey) =>
        virtualKey is VkShift or VkControl or VkMenu or VkLWin or VkRWin or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static int GetCurrentModifierFlags()
    {
        var value = 0;
        if (IsVirtualKeyDown(VkControl))
        {
            value |= 1;
        }

        if (IsVirtualKeyDown(VkMenu))
        {
            value |= 2;
        }

        if (IsVirtualKeyDown(VkShift))
        {
            value |= 4;
        }

        if (IsVirtualKeyDown(VkLWin) || IsVirtualKeyDown(VkRWin))
        {
            value |= 8;
        }

        return value;
    }

    private static bool IsVirtualKeyDown(uint virtualKey) =>
        (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    private static string ModifierFlagsToString(int flags)
    {
        var parts = new List<string>(4);
        if ((flags & 1) != 0) parts.Add("Control");
        if ((flags & 2) != 0) parts.Add("Alt");
        if ((flags & 4) != 0) parts.Add("Shift");
        if ((flags & 8) != 0) parts.Add("Win");
        return string.Join('+', parts);
    }

    private static string VirtualKeyToHotKeyString(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        return virtualKey switch
        {
            0x0D => "Enter",
            0x20 => "Space",
            0x09 => "Tab",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            VkEscape => "Esc",
            _ => string.Empty
        };
    }

    private static HotKeySettings CloneSettings(HotKeySettings settings) =>
        new()
        {
            Modifier = settings.Modifier,
            Key = settings.Key,
            ToggleWindow = CloneBinding(settings.ToggleWindow),
            ToggleRightPanel = CloneBinding(settings.ToggleRightPanel),
            PasteSelected = CloneBinding(settings.PasteSelected),
            TogglePin = CloneBinding(settings.TogglePin),
            PasteRecent = settings.PasteRecent.Select(CloneBinding).ToList(),
            StartOnWindowsBoot = settings.StartOnWindowsBoot,
            DetailsPanelExpanded = settings.DetailsPanelExpanded
        };

    private static HotKeyBinding CloneBinding(HotKeyBinding binding) =>
        new()
        {
            Modifier = binding.Modifier,
            Key = binding.Key,
            IsGlobal = binding.IsGlobal
        };

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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
