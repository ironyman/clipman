using Clipman.Models;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Clipman;

public sealed partial class MainWindow
{
    private const uint WmPaste = 0x0302;

    private async Task<bool> PutSelectedClipOnClipboardAsync()
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return false;
        }

        return await PutClipOnClipboardAsync(clip);
    }

    private static async Task<bool> PutClipOnClipboardAsync(ClipboardClip clip)
    {
        try
        {
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };

            if (!string.IsNullOrWhiteSpace(clip.ContentText))
            {
                package.SetText(clip.ContentText);
            }
            else if (!string.IsNullOrWhiteSpace(clip.ReferencePath))
            {
                var storageItems = await ResolveStorageItemsAsync(clip.ReferencePath);
                if (storageItems.Count > 0)
                {
                    package.SetStorageItems(storageItems);
                }
                else
                {
                    package.SetText(clip.ReferencePath);
                }
            }
            else if (clip.ContentBytes is { Length: > 0 } && clip.Kind == ClipKind.Image)
            {
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(clip.ContentBytes.AsBuffer());
                stream.Seek(0);
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            }
            else if (!string.IsNullOrWhiteSpace(clip.SourceUrl))
            {
                package.SetText(clip.SourceUrl);
            }
            else
            {
                package.SetText(clip.Preview);
            }

            Clipboard.SetContent(package);
            Clipboard.Flush();
            return await WaitForClipboardReadyAsync();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForClipboardReadyAsync()
    {
        for (var i = 0; i < 8; i++)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content is not null && content.AvailableFormats.Count > 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(15);
        }

        return false;
    }

    private static async Task<IReadOnlyList<IStorageItem>> ResolveStorageItemsAsync(string referencePath)
    {
        var lines = referencePath
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = new List<IStorageItem>();
        foreach (var line in lines)
        {
            if (File.Exists(line))
            {
                items.Add(await StorageFile.GetFileFromPathAsync(line));
                continue;
            }

            if (Directory.Exists(line))
            {
                items.Add(await StorageFolder.GetFolderFromPathAsync(line));
            }
        }

        return items;
    }

    private FocusSnapshot CaptureFocusSnapshot()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return FocusSnapshot.Empty;
        }

        var threadId = GetWindowThreadProcessId(foreground, out _);

        var info = new GuiThreadInfo
        {
            cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };
        var focus = GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
        return new FocusSnapshot(foreground, focus);
    }

    private void RestoreFocusSnapshot(FocusSnapshot snapshot)
    {
        if (snapshot.WindowHandle == IntPtr.Zero || !IsWindow(snapshot.WindowHandle))
        {
            return;
        }

        ShowWindow(snapshot.WindowHandle, SwShow);

        var targetThread = GetWindowThreadProcessId(snapshot.WindowHandle, out _);
        var currentThread = GetCurrentThreadId();
        var attached = false;
        if (targetThread != 0 && targetThread != currentThread)
        {
            attached = AttachThreadInput(currentThread, targetThread, true);
        }

        SetForegroundWindow(snapshot.WindowHandle);
        if (snapshot.FocusHandle != IntPtr.Zero && IsWindow(snapshot.FocusHandle))
        {
            SetFocus(snapshot.FocusHandle);
        }

        if (attached)
        {
            AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private IntPtr GetTargetControlHandle(FocusSnapshot snapshot)
    {
        if (snapshot.FocusHandle != IntPtr.Zero && IsWindow(snapshot.FocusHandle))
        {
            return snapshot.FocusHandle;
        }

        if (snapshot.WindowHandle == IntPtr.Zero || !IsWindow(snapshot.WindowHandle))
        {
            return IntPtr.Zero;
        }

        var targetThread = GetWindowThreadProcessId(snapshot.WindowHandle, out _);
        if (targetThread == 0)
        {
            return IntPtr.Zero;
        }

        var info = new GuiThreadInfo
        {
            cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };
        return GetGUIThreadInfo(targetThread, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    private static bool TrySendPasteMessage(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var result = SendMessageTimeout(
            hwnd,
            WmPaste,
            IntPtr.Zero,
            IntPtr.Zero,
            0x0002,
            250,
            out _);

        return result != IntPtr.Zero;
    }

    private static bool SendCtrlV()
    {
        try
        {
            return SendCtrlVToForeground() != 0;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo lpgui);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("clipman_uia_bridge.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SendCtrlVToForeground();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeRect rcCaret;
    }

    private readonly record struct FocusSnapshot(IntPtr WindowHandle, IntPtr FocusHandle)
    {
        public static FocusSnapshot Empty { get; } = new(IntPtr.Zero, IntPtr.Zero);
    }
}
