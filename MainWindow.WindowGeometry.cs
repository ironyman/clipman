using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Clipman;

public sealed partial class MainWindow
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint MonitorDefaultToNull = 0x00000000;

    private void CenterWindowOnFocusMonitor(IntPtr focusWindowHandle)
    {
        //if (TryPositionWindowNearCaretOrCursor())
        //{
        //    return;
        //}

        var targetWindow = focusWindowHandle != IntPtr.Zero ? focusWindowHandle : GetForegroundWindow();
        if (targetWindow == IntPtr.Zero)
        {
            return;
        }

        var monitor = MonitorFromWindow(targetWindow, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var workWidth = info.rcWork.Right - info.rcWork.Left;
        var workHeight = info.rcWork.Bottom - info.rcWork.Top;
        var x = info.rcWork.Left + Math.Max(0, (workWidth - size.Width) / 2);
        var y = info.rcWork.Top + Math.Max(0, (workHeight - size.Height) / 2);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private bool TryPositionWindowNearCaretOrCursor()
    {
        if (!TryGetFocusCaretPoint(out var anchor) && !GetCursorPosForGeometry(out anchor))
        {
            return false;
        }

        var monitor = MonitorFromPoint(anchor, MonitorDefaultToNull);
        if (monitor == IntPtr.Zero)
        {
            monitor = MonitorFromPoint(anchor, MonitorDefaultToNearest);
        }

        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        var x = anchor.X - (size.Width / 2);
        var y = anchor.Y + 20;

        var maxX = info.rcWork.Right - size.Width;
        var maxY = info.rcWork.Bottom - size.Height;
        x = Math.Max(info.rcWork.Left, Math.Min(x, maxX));
        y = Math.Max(info.rcWork.Top, Math.Min(y, maxY));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        return true;
    }

    private static bool TryGetFocusCaretPoint(out GeometryPoint point)
    {
        point = default;
        try
        {
            var ok = GetFocusCaretScreenPoint(out var x, out var y);
            if (ok == 0 || x < 0 || y < 0)
            {
                return false;
            }

            point.X = x;
            point.Y = y;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private int GetWindowFrameWidth()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        if (!GetWindowRect(hwnd, out var windowRect) || !GetClientRect(hwnd, out var clientRect))
        {
            return 0;
        }

        var windowWidth = Math.Max(0, windowRect.Right - windowRect.Left);
        var clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
        return Math.Max(0, windowWidth - clientWidth);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(GeometryPoint pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosForGeometry(out GeometryPoint lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("clipman_uia_bridge.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int GetFocusCaretScreenPoint(out int x, out int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct GeometryPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }
}
