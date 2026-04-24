using Clipman.Models;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Clipman;

public sealed partial class MainWindow
{
    private const uint WmTrayIcon = 0x8001;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const int TrayIconId = 0x434C49;
    private const int IdiApplication = 0x7F00;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint CmShowHide = 0x1001;
    private const uint CmExit = 0x1002;
    private const uint CmFirstClip = 0x2000;
    private const int MaxTrayClipItems = 12;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint ImageIcon = 1;

    private IntPtr _trayThemeIconHandle;

    private bool HotKeyService_WindowMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.ptMinTrackSize.X = Math.Max(minMaxInfo.ptMinTrackSize.X, GetMinimumWindowWidth());
            Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
            return false;
        }

        if (msg == WmTrayIcon)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == WmLButtonUp)
            {
                DispatcherQueue.TryEnqueue(ToggleMainWindowVisibility);
                return true;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowTrayContextMenu();
                return true;
            }
        }

        return false;
    }

    private void ShowTrayContextMenu()
    {
        _lastHotkeyFocus = CaptureFocusSnapshot();

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var clips = GetTrayMenuClips();

            _ = AppendMenu(menu, MfString, CmShowHide, IsMainWindowVisible() ? "Hide Clipman" : "Show Clipman");
            _ = AppendMenu(menu, MfSeparator, 0, null);

            if (clips.Count == 0)
            {
                _ = AppendMenu(menu, MfString | MfGrayed, CmFirstClip, "(No clips)");
            }
            else
            {
                for (var i = 0; i < clips.Count; i++)
                {
                    _ = AppendMenu(menu, MfString, CmFirstClip + (uint)i, BuildTrayLabel(clips[i]));
                }
            }

            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, CmExit, "Exit");

            _ = GetCursorPos(out var cursor);
            var selected = TrackPopupMenuEx(
                menu,
                TpmLeftAlign | TpmBottomAlign | TpmRightButton | TpmReturnCmd,
                cursor.X,
                cursor.Y,
                WindowNative.GetWindowHandle(this),
                IntPtr.Zero);

            HandleTrayCommand(selected, clips);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void HandleTrayCommand(uint commandId, IReadOnlyList<ClipboardClip> clips)
    {
        if (commandId == 0)
        {
            return;
        }

        if (commandId == CmShowHide)
        {
            ToggleMainWindowVisibility();
            return;
        }

        if (commandId == CmExit)
        {
            Application.Current.Exit();
            return;
        }

        if (commandId < CmFirstClip)
        {
            return;
        }

        var index = (int)(commandId - CmFirstClip);
        if (index < 0 || index >= clips.Count)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            await PasteClipToLastFocusAsync(clips[index]);
        });
    }

    private IReadOnlyList<ClipboardClip> GetTrayMenuClips()
    {
        try
        {
            return Task.Run(async () =>
                await _historyService.GetPageAsync(0, MaxTrayClipItems, null, null, false))
                .GetAwaiter()
                .GetResult()
                .OrderByDescending(clip => clip.IsPinned)
                .ThenByDescending(clip => clip.CopiedAt)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string BuildTrayLabel(ClipboardClip clip)
    {
        var label = $"{(clip.IsPinned ? "[P] " : string.Empty)}{clip.Title}";
        return label.Length <= 70 ? label : $"{label[..67]}...";
    }

    private void InitializeTrayIcon()
    {
        var data = CreateNotifyIconData();
        _trayIconAdded = Shell_NotifyIcon(NimAdd, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded)
        {
            ReleaseTrayThemeIcon();
            return;
        }

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _trayIconAdded = false;
        ReleaseTrayThemeIcon();
    }

    private NotifyIconData CreateNotifyIconData()
    {
        var iconHandle = _trayThemeIconHandle != IntPtr.Zero
            ? _trayThemeIconHandle
            : LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = WindowNative.GetWindowHandle(this),
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = iconHandle,
            szTip = "Clipman"
        };
        return data;
    }

    private void UpdateTrayThemeIcon(string iconPath)
    {
        if (_isWindowClosed || string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        var iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (iconHandle == IntPtr.Zero)
        {
            return;
        }

        var previous = _trayThemeIconHandle;
        _trayThemeIconHandle = iconHandle;

        if (_trayIconAdded)
        {
            var data = CreateNotifyIconData();
            _ = Shell_NotifyIcon(NimModify, ref data);
        }

        if (previous != IntPtr.Zero)
        {
            _ = DestroyIcon(previous);
        }
    }

    private void ReleaseTrayThemeIcon()
    {
        if (_trayThemeIconHandle == IntPtr.Zero)
        {
            return;
        }

        _ = DestroyIcon(_trayThemeIconHandle);
        _trayThemeIconHandle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }
}
