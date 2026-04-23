using Clipman.Models;
using Clipman.Services;
using Clipman.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace Clipman;

public sealed partial class MainWindow : Window
{
    private const uint WmTrayIcon = 0x8001;
    private const uint WmCommand = 0x0111;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const int TrayIconId = 0x434C49;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int IdiApplication = 0x7F00;
    private const uint InputKeyboard = 1;
    private const uint KeyEventfKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint MonitorDefaultToNearest = 0x00000002;
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

    private readonly AppSettingsService _settingsService = new();
    private readonly EsentClipboardHistoryService _repository = new();
    private readonly ClipboardHistoryService _historyService;
    private readonly ClipboardListenerService _clipboardListenerService;
    private readonly MainViewModel _viewModel;
    private readonly GlobalHotKeyService _hotKeyService;
    private HotKeySettings _hotKeySettings;
    private ScrollViewer? _historyScrollViewer;
    private bool _trayIconAdded;
    private FocusSnapshot _lastHotkeyFocus = FocusSnapshot.Empty;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _hotKeySettings = _settingsService.LoadHotKey();
        var captureService = new ClipboardCaptureService();
        _historyService = new ClipboardHistoryService(_repository, captureService);
        _clipboardListenerService = new ClipboardListenerService(_historyService);
        _viewModel = new MainViewModel(_historyService);
        Root.DataContext = _viewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        TryEnableMica();

        _hotKeyService = new GlobalHotKeyService(WindowNative.GetWindowHandle(this));
        _hotKeyService.Pressed += HotKeyService_Pressed;
        _hotKeyService.WindowMessageReceived += HotKeyService_WindowMessageReceived;
        _hotKeyService.Register(_hotKeySettings);
        InitializeTrayIcon();

        AppWindow.Changed += AppWindow_Changed;
        UpdateTitleBarInsets();

        _clipboardListenerService.Start();
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        HideMainWindow();
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        await _viewModel.LoadAsync();
        await _clipboardListenerService.CaptureNowAsync();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || string.IsNullOrWhiteSpace(tag))
        {
            _viewModel.SelectedKind = null;
            return;
        }

        _viewModel.SelectedKind = Enum.TryParse<ClipKind>(tag, out var kind) ? kind : null;
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedView = args.SelectedItemContainer?.Tag?.ToString();
        if (selectedView == "Options")
        {
            _ = ShowSettingsDialogAsync();
            return;
        }

        _viewModel.ShowPinnedOnly = selectedView == "Pinned";

        if (selectedView == "History")
        {
            _viewModel.SelectedKind = null;
        }
    }

    private void HotKeyService_Pressed(object? sender, EventArgs e)
    {
        _lastHotkeyFocus = CaptureFocusSnapshot();
        DispatcherQueue.TryEnqueue(() => ShowMainWindow(centerOnFocusWindow: true));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowSettingsDialogAsync();
    }

    private async Task ShowSettingsDialogAsync()
    {
        var modifierBox = new ComboBox
        {
            Header = "Modifier",
            ItemsSource = new[]
            {
                "Control+Shift",
                "Control+Alt",
                "Alt+Shift",
                "Win+Shift",
                "Control"
            },
            SelectedItem = _hotKeySettings.Modifier,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var keyBox = new ComboBox
        {
            Header = "Key",
            ItemsSource = Enumerable.Range('A', 26).Select(value => ((char)value).ToString()).Concat(["Space", "Tab", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"]).ToArray(),
            SelectedItem = _hotKeySettings.Key,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                modifierBox,
                keyBox
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _hotKeySettings = new HotKeySettings
        {
            Modifier = modifierBox.SelectedItem?.ToString() ?? "Control+Shift",
            Key = keyBox.SelectedItem?.ToString() ?? "V"
        };
        _settingsService.SaveHotKey(_hotKeySettings);
        _hotKeyService.Register(_hotKeySettings);
    }

    private void HistoryListView_Loaded(object sender, RoutedEventArgs e)
    {
        _historyScrollViewer = FindScrollViewer(HistoryListView);
        if (_historyScrollViewer is not null)
        {
            _historyScrollViewer.ViewChanged -= HistoryScrollViewer_ViewChanged;
            _historyScrollViewer.ViewChanged += HistoryScrollViewer_ViewChanged;
        }
    }

    private async void HistoryScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_historyScrollViewer is null || !_viewModel.HasMore || _viewModel.IsLoading)
        {
            return;
        }

        var nearBottom = _historyScrollViewer.VerticalOffset + _historyScrollViewer.ViewportHeight >= _historyScrollViewer.ScrollableHeight - 160;
        if (nearBottom)
        {
            await _viewModel.LoadMoreAsync();
        }
    }

    private void ToggleMainWindowVisibility()
    {
        if (IsMainWindowVisible())
        {
            HideMainWindow();
            return;
        }

        ShowMainWindow(centerOnFocusWindow: false);
    }

    private void HideMainWindow()
    {
        ShowWindow(WindowNative.GetWindowHandle(this), SwHide);
    }

    private void ShowMainWindow(bool centerOnFocusWindow)
    {
        if (centerOnFocusWindow)
        {
            CenterWindowOnFocusMonitor(_lastHotkeyFocus.WindowHandle);
        }

        ShowWindow(WindowNative.GetWindowHandle(this), SwShow);
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
        FocusSearchBox();
    }

    private bool IsMainWindowVisible() => IsWindowVisible(WindowNative.GetWindowHandle(this));

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        TitleLeftPanel.Margin = new Thickness(AppWindow.TitleBar.LeftInset + 4, 0, 0, 0);
        TitleActionsPanel.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset + 2, 0);
    }

    private void FocusSearchBox()
    {
        _ = DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));
    }

    private async void ShowClipInfo_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return;
        }

        var lines = new[]
        {
            $"Id: {clip.Id}",
            $"Kind: {clip.Kind}",
            $"Title: {clip.Title}",
            $"Preview: {clip.Preview}",
            $"Copied At: {clip.CopiedAt:yyyy-MM-dd HH:mm:ss}",
            $"Pinned: {clip.IsPinned}",
            $"Source App: {clip.SourceApp}",
            $"Window: {clip.SourceWindowTitle}",
            $"Tab: {clip.BrowserTabTitle}",
            $"URL: {clip.SourceUrl}",
            $"Domain: {clip.SourceDomain}",
            $"Reference: {clip.ReferencePath}",
            $"Format: {clip.FormatLabel}",
            $"Available Formats: {clip.FormatsJson}"
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Clip Info",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, lines.Where(line => !line.EndsWith(": ", StringComparison.Ordinal))),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        await dialog.ShowAsync();
    }

    private async void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return;
        }

        await _historyService.SetPinnedAsync(clip.Id, !clip.IsPinned);
        await RefreshViewAndRestoreSelectionAsync(clip.Id);
    }

    private async void DeleteClip_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return;
        }

        await _historyService.DeleteAsync(clip.Id);
        await _viewModel.LoadAsync();
    }

    private async void CopyClip_Click(object sender, RoutedEventArgs e)
    {
        await PutSelectedClipOnClipboardAsync();
    }

    private async void PasteClip_Click(object sender, RoutedEventArgs e)
    {
        if (!await PutSelectedClipOnClipboardAsync())
        {
            return;
        }

        var snapshot = _lastHotkeyFocus;
        if (snapshot.WindowHandle == IntPtr.Zero)
        {
            return;
        }

        HideMainWindow();
        await Task.Delay(90);
        RestoreFocusSnapshot(snapshot);
        await Task.Delay(80);
        SendCtrlV();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _clipboardListenerService.Dispose();
        RemoveTrayIcon();
        _hotKeyService.Dispose();
        _repository.Dispose();
        _hotKeyService.WindowMessageReceived -= HotKeyService_WindowMessageReceived;
        AppWindow.Changed -= AppWindow_Changed;
        if (_isExiting)
        {
            return;
        }
    }

    private void TryEnableMica()
    {
        SystemBackdrop = new MicaBackdrop();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task RefreshViewAndRestoreSelectionAsync(string? preferredId)
    {
        await _viewModel.RefreshAsync();
        if (preferredId is null)
        {
            return;
        }

        _viewModel.SelectedClip = _viewModel.VisibleClips.FirstOrDefault(clip => clip.Id == preferredId)
            ?? _viewModel.VisibleClips.FirstOrDefault();
    }

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
            return true;
        }
        catch
        {
            return false;
        }
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

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkV, 0),
            CreateKeyboardInput(VkV, KeyEventfKeyUp),
            CreateKeyboardInput(VkControl, KeyEventfKeyUp)
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags) =>
        new()
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

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

    private bool HotKeyService_WindowMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
    {
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
            _ = SetForegroundWindow(WindowNative.GetWindowHandle(this));
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
            _isExiting = true;
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
            if (!await PutClipOnClipboardAsync(clips[index]))
            {
                return;
            }

            HideMainWindow();
            await Task.Delay(90);
            RestoreFocusSnapshot(_lastHotkeyFocus);
            await Task.Delay(80);
            SendCtrlV();
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
            return;
        }

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _trayIconAdded = false;
    }

    private NotifyIconData CreateNotifyIconData()
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = WindowNative.GetWindowHandle(this),
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication),
            szTip = "Clipman"
        };
        return data;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly record struct FocusSnapshot(IntPtr WindowHandle, IntPtr FocusHandle)
    {
        public static FocusSnapshot Empty { get; } = new(IntPtr.Zero, IntPtr.Zero);
    }
}
