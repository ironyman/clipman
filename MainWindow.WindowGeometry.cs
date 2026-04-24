using System.Runtime.InteropServices;

namespace Clipman;

public sealed partial class MainWindow
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    private void CenterWindowOnFocusMonitor(IntPtr focusWindowHandle)
    {
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

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
