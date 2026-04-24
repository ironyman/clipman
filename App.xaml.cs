using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Clipman;

public partial class App : Application
{
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    private const uint WmMenuChar = 0x0120;
    private const uint WmSysChar = 0x0106;
    private static readonly SubclassProc MenuSuppressProc = SubclassWndProc;
    private static readonly IntPtr MenuSuppressSubclassId = new(1);

    private Window? _window;
    private IntPtr _windowHandle;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _windowHandle = WindowNative.GetWindowHandle(_window);
        if (_windowHandle != IntPtr.Zero)
        {
            _ = SetWindowSubclass(_windowHandle, MenuSuppressProc, MenuSuppressSubclassId, IntPtr.Zero);
        }

        _window.Closed += OnWindowClosed;
    }

    private static IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        const int WM_MENUCHAR = 0x0120;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSCHAR = 0x0106;

        const int VK_MENU = 0x12; // Alt

        const int MNC_CLOSE = 1;
        const int MNC_IGNORE = 0;


        switch (msg)
        {
            case WM_MENUCHAR:
                // Suppress menu beep
                return new IntPtr((MNC_CLOSE << 16));

            case WM_SYSKEYDOWN:
                if ((int)wParam == VK_MENU)
                {
                    // Swallow Alt → prevents menu activation + beep
                    return IntPtr.Zero;
                }
                break;

            case WM_SYSCHAR:
                // Swallow system char (extra safety)
                return IntPtr.Zero;
        }


        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            _ = RemoveWindowSubclass(_windowHandle, MenuSuppressProc, MenuSuppressSubclassId);
            _windowHandle = IntPtr.Zero;
        }
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
