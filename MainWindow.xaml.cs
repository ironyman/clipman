using Clipman.Models;
using Clipman.Services;
using Clipman.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Clipman;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SettingsToCaptionButtonsGap = 8;
    private const int RecentHotkeySlotCount = 9;
    private const double RightPanelSlideOffset = 28;
    private const int RightPanelAnimationDurationMs = 180;

    private readonly AppSettingsService _settingsService = new();
    private readonly EsentClipboardHistoryService _repository = new();
    private readonly ClipboardHistoryService _historyService;
    private readonly ClipboardListenerService _clipboardListenerService;
    private readonly MainViewModel _viewModel;
    private readonly GlobalHotKeyService _hotKeyService;
    private HotKeySettings _hotKeySettings;
    private ScrollViewer? _historyScrollViewer;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private bool _trayIconAdded;
    private FocusSnapshot _lastHotkeyFocus = FocusSnapshot.Empty;
    private bool _isEditingTextClip;
    private bool _isRecordingClipboardText;
    private string? _recordingClipId;
    private readonly Dictionary<string, int> _recentSlotByClipId = [];
    private string? _tagDialogClipId;
    private bool _isWindowClosed;
    private bool _isRightPanelVisible = true;
    private bool _isRightPanelAnimating;

    public MainWindow()
    {
        InitializeComponent();

        _hotKeySettings = _settingsService.LoadHotKey();
        _hotKeySettings.StartOnWindowsBoot = IsStartupEnabled();
        var captureService = new ClipboardCaptureService();
        _historyService = new ClipboardHistoryService(_repository, captureService);
        _clipboardListenerService = new ClipboardListenerService(_historyService);
        _viewModel = new MainViewModel(_historyService);
        Root.DataContext = _viewModel;
        _historyService.ClipAdded += HistoryService_ClipAdded;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        TryEnableMica();
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        ApplyThemeIcons();
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

        _hotKeyService = new GlobalHotKeyService(WindowNative.GetWindowHandle(this));
        _hotKeyService.Pressed += HotKeyService_Pressed;
        _hotKeyService.WindowMessageReceived += HotKeyService_WindowMessageReceived;
        _hotKeyService.Register(_hotKeySettings);
        InitializeTrayIcon();

        AppWindow.Changed += AppWindow_Changed;
        DragRegion.SizeChanged += DragRegion_SizeChanged;
        SettingsButton.SizeChanged += SettingsButton_SizeChanged;
        if (GetRightPanelToggleButton() is FrameworkElement rightPanelToggleButton)
        {
            rightPanelToggleButton.SizeChanged += RightPanelToggleButton_SizeChanged;
        }
        UpdateTitleBarInsets();
        UpdateTitleBarPassthroughRegions();
        UpdateRightPanelToggleIcon();

        _clipboardListenerService.Start();
        Closed += MainWindow_Closed;

        HideMainWindow();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _viewModel.LoadAsync();
        await UpdateRecentSlotHintsAsync();
        await _clipboardListenerService.CaptureNowAsync();
        RenderTagBadges();
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

    private void HotKeyService_Pressed(object? sender, HotKeyAction action)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            HandleHotKeyAction(action);
        });
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowSettingsDialogAsync();
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

    private void HistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isEditingTextClip)
        {
            ExitEditMode();
        }

        RenderTagBadges();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Down or VirtualKey.Up)
        {
            MoveSelection(e.Key == VirtualKey.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (MatchesBinding(e, _hotKeySettings.PasteSelected))
        {
            PasteClip_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (MatchesBinding(e, _hotKeySettings.TogglePin))
        {
            TogglePin_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void HistoryListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Down or VirtualKey.Up)
        {
            MoveSelection(e.Key == VirtualKey.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (MatchesBinding(e, _hotKeySettings.PasteSelected))
        {
            PasteClip_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (MatchesBinding(e, _hotKeySettings.TogglePin))
        {
            TogglePin_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_viewModel.VisibleClips.Count == 0)
        {
            return;
        }

        var currentIndex = _viewModel.SelectedClip is null
            ? -1
            : _viewModel.VisibleClips.IndexOf(_viewModel.SelectedClip);
        var targetIndex = Math.Clamp(currentIndex + delta, 0, _viewModel.VisibleClips.Count - 1);
        _viewModel.SelectedClip = _viewModel.VisibleClips[targetIndex];
        HistoryListView.ScrollIntoView(_viewModel.SelectedClip);
    }

    private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer?.ContentTemplateRoot is not FrameworkElement root ||
            root.FindName("SlotHintTextBlock") is not TextBlock slotHint ||
            args.Item is not ClipboardClip clip)
        {
            return;
        }

        if (_recentSlotByClipId.TryGetValue(clip.Id, out var slot))
        {
            slotHint.Text = slot.ToString();
            slotHint.Visibility = Visibility.Visible;
        }
        else
        {
            slotHint.Visibility = Visibility.Collapsed;
            slotHint.Text = string.Empty;
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
            await UpdateRecentSlotHintsAsync();
        }
    }

    private void ToggleMainWindowVisibility()
    {
        if (IsMainWindowVisible())
        {
            HideMainWindow();
            return;
        }

        ShowMainWindow(centerOnFocusWindow: false, clearSearch: false);
    }

    private void HandleHotKeyAction(HotKeyAction action)
    {
        switch (action)
        {
            case HotKeyAction.ToggleWindow:
                if (IsMainWindowVisible())
                {
                    HideMainWindow();
                    return;
                }

                _lastHotkeyFocus = CaptureFocusSnapshot();
                ShowMainWindow(centerOnFocusWindow: true, clearSearch: true);
                return;
            case HotKeyAction.PasteRecent1:
            case HotKeyAction.PasteRecent2:
            case HotKeyAction.PasteRecent3:
            case HotKeyAction.PasteRecent4:
            case HotKeyAction.PasteRecent5:
            case HotKeyAction.PasteRecent6:
            case HotKeyAction.PasteRecent7:
            case HotKeyAction.PasteRecent8:
            case HotKeyAction.PasteRecent9:
                _ = PasteRecentSlotAsync((int)action - (int)HotKeyAction.PasteRecent1 + 1);
                return;
            case HotKeyAction.ToggleRightPanel:
                _ = ToggleRightPanelAsync();
                return;
        }
    }

    private void HideMainWindow()
    {
        ShowWindow(WindowNative.GetWindowHandle(this), SwHide);
    }

    private void ShowMainWindow(bool centerOnFocusWindow, bool clearSearch)
    {
        if (centerOnFocusWindow)
        {
            CenterWindowOnFocusMonitor(_lastHotkeyFocus.WindowHandle);
        }

        ShowWindow(WindowNative.GetWindowHandle(this), SwShow);
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
        FocusSearchBox(clearSearch);
    }

    private bool IsMainWindowVisible() => IsWindowVisible(WindowNative.GetWindowHandle(this));

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        TitleLeftPanel.Margin = new Thickness(0, 0, 0, 0);
        TitleActionsPanel.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset + SettingsToCaptionButtonsGap, 0);
        UpdateTitleBarPassthroughRegions();
    }

    private void DragRegion_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTitleBarPassthroughRegions();
    }

    private void SettingsButton_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTitleBarPassthroughRegions();
    }

    private void RightPanelToggleButton_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTitleBarPassthroughRegions();
    }

    private void RightPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ToggleRightPanelAsync();
    }

    private void UpdateTitleBarPassthroughRegions()
    {
        if (_isWindowClosed)
        {
            return;
        }

        if (_nonClientPointerSource is null)
        {
            return;
        }

        UIElement? visualRoot;
        try
        {
            visualRoot = Content as UIElement;
        }
        catch (Exception)
        {
            return;
        }

        if (visualRoot is null)
        {
            return;
        }

        var rects = new List<RectInt32>();
        AppendPassthroughRect(SettingsButton);
        if (GetRightPanelToggleButton() is FrameworkElement rightPanelToggleButton)
        {
            AppendPassthroughRect(rightPanelToggleButton);
        }
        if (rects.Count == 0)
        {
            return;
        }

        _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());

        void AppendPassthroughRect(FrameworkElement element)
        {
            if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return;
            }

            var scale = element.XamlRoot.RasterizationScale;
            var topLeft = element.TransformToVisual(visualRoot).TransformPoint(new Windows.Foundation.Point(0, 0));
            rects.Add(new RectInt32(
                (int)Math.Round(topLeft.X * scale),
                (int)Math.Round(topLeft.Y * scale),
                Math.Max(1, (int)Math.Round(element.ActualWidth * scale)),
                Math.Max(1, (int)Math.Round(element.ActualHeight * scale))));
        }
    }

    private void FocusSearchBox(bool clearSearch)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                var textBox = FindVisualChild<TextBox>(SearchBox);
                textBox?.SelectAll();
            });
        });
    }

    private void FocusSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSearchBox(clearSearch: false);
        args.Handled = true;
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
            $"Available Formats: {clip.FormatsJson}",
            $"Tags: {string.Join(", ", ParseTags(clip.Tags))}"
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

    private async void AddTags_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return;
        }

        _tagDialogClipId = clip.Id;
        TagInputTextBox.Text = string.Join(", ", ParseTags(clip.Tags));
        AddTagsDialog.XamlRoot = Root.XamlRoot;
        _ = await AddTagsDialog.ShowAsync();
        _tagDialogClipId = null;
        TagInputTextBox.Text = string.Empty;
    }

    private async void AddTagsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_tagDialogClipId))
        {
            return;
        }

        var tags = ParseTags(TagInputTextBox.Text);
        var tagsValue = tags.Count > 0 ? string.Join(",", tags) : null;
        await _historyService.UpdateTagsAsync(_tagDialogClipId, tagsValue);
        await RefreshViewAndRestoreSelectionAsync(_tagDialogClipId);
        _tagDialogClipId = null;
        TagInputTextBox.Text = string.Empty;
    }

    private void EditClip_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (!CanEditClipText(clip))
        {
            return;
        }

        var selectedClip = clip!;
        _isEditingTextClip = true;
        _isRecordingClipboardText = false;
        _recordingClipId = selectedClip.Id;
        RecordButtonText.Text = "Record";
        SelectedPreviewTextBox.Text = selectedClip.ContentText ?? selectedClip.Preview ?? string.Empty;
        SelectedPreviewTextBox.Visibility = Visibility.Visible;
        SelectedPreviewTextBlock.Visibility = Visibility.Collapsed;
        EditButtonsPanel.Visibility = Visibility.Visible;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            SelectedPreviewTextBox.Focus(FocusState.Programmatic);
            SelectedPreviewTextBox.Select(SelectedPreviewTextBox.Text.Length, 0);
        });
    }

    private async void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditingTextClip)
        {
            return;
        }

        var clip = _viewModel.SelectedClip;
        if (!CanEditClipText(clip))
        {
            ExitEditMode();
            return;
        }

        var updatedText = SelectedPreviewTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(updatedText))
        {
            return;
        }

        updatedText = NormalizeText(updatedText);
        var title = BuildTitleFromText(updatedText, clip!.Kind);
        var preview = BuildPreviewText(updatedText);

        await _historyService.UpdateTextAsync(clip.Id, title, preview, updatedText);
        await RefreshViewAndRestoreSelectionAsync(clip.Id);
        ExitEditMode();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        ExitEditMode();
    }

    private void ToggleRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditingTextClip || _viewModel.SelectedClip is null)
        {
            return;
        }

        if (_recordingClipId is null || !_recordingClipId.Equals(_viewModel.SelectedClip.Id, StringComparison.Ordinal))
        {
            _recordingClipId = _viewModel.SelectedClip.Id;
        }

        _isRecordingClipboardText = !_isRecordingClipboardText;
        _viewModel.SetLiveInsertSuppressed(_isRecordingClipboardText);
        RecordButtonText.Text = _isRecordingClipboardText ? "Stop" : "Record";

        if (!_isRecordingClipboardText)
        {
            _ = _viewModel.RefreshAsync();
        }
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
        await UpdateRecentSlotHintsAsync();
        RenderTagBadges();
    }

    private async void CopyClip_Click(object sender, RoutedEventArgs e)
    {
        await PutSelectedClipOnClipboardAsync();
    }

    private async void PasteClip_Click(object sender, RoutedEventArgs e)
    {
        var clip = _viewModel.SelectedClip;
        if (clip is null)
        {
            return;
        }

        await PasteClipToLastFocusAsync(clip);
    }

    private async Task PasteRecentSlotAsync(int slot)
    {
        var clip = await GetRecentClipBySlotAsync(slot);
        if (clip is null)
        {
            return;
        }

        await PasteClipToLastFocusAsync(clip);
    }

    private async Task PasteClipToLastFocusAsync(ClipboardClip clip)
    {
        if (!await PutClipOnClipboardAsync(clip))
        {
            return;
        }

        var snapshot = _lastHotkeyFocus.WindowHandle != IntPtr.Zero ? _lastHotkeyFocus : CaptureFocusSnapshot();
        HideMainWindow();
        await Task.Delay(90);
        if (snapshot.WindowHandle != IntPtr.Zero)
        {
            RestoreFocusSnapshot(snapshot);
        }

        await Task.Delay(80);
        var targetControl = GetTargetControlHandle(snapshot);
        SendCtrlV();
        if (targetControl != IntPtr.Zero)
        {
            await Task.Delay(50);
            _ = TrySendPasteMessage(targetControl);
        }
    }

    private async Task<ClipboardClip?> GetRecentClipBySlotAsync(int slot)
    {
        if (slot < 1 || slot > RecentHotkeySlotCount)
        {
            return null;
        }

        var ordered = _viewModel.VisibleClips
            .Take(RecentHotkeySlotCount)
            .ToList();

        if (ordered.Count == 0)
        {
            await _viewModel.LoadAsync();
            ordered = _viewModel.VisibleClips
                .Take(RecentHotkeySlotCount)
                .ToList();
        }

        return slot <= ordered.Count ? ordered[slot - 1] : null;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isWindowClosed = true;
        Root.ActualThemeChanged -= Root_ActualThemeChanged;
        _clipboardListenerService.Dispose();
        RemoveTrayIcon();
        _hotKeyService.Dispose();
        _repository.Dispose();
        _historyService.ClipAdded -= HistoryService_ClipAdded;
        _hotKeyService.WindowMessageReceived -= HotKeyService_WindowMessageReceived;
        AppWindow.Changed -= AppWindow_Changed;
        DragRegion.SizeChanged -= DragRegion_SizeChanged;
        SettingsButton.SizeChanged -= SettingsButton_SizeChanged;
        if (GetRightPanelToggleButton() is FrameworkElement rightPanelToggleButton)
        {
            rightPanelToggleButton.SizeChanged -= RightPanelToggleButton_SizeChanged;
        }
    }

    private void TryEnableMica()
    {
        SystemBackdrop = new MicaBackdrop();
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyThemeIcons();
    }

    private void ApplyThemeIcons()
    {
        try
        {
            var iconPath = GetThemeIconPath();
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
                UpdateTrayThemeIcon(iconPath);
            }
        }
        catch
        {
        }
    }

    private string GetThemeIconPath()
    {
        var fileName = Root.ActualTheme == ElementTheme.Dark ? "clipman-light.ico" : "clipman-dark.ico";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "clipman.ico");
        }

        return iconPath;
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

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task RefreshViewAndRestoreSelectionAsync(string? preferredId)
    {
        await _viewModel.RefreshAsync();
        await UpdateRecentSlotHintsAsync();
        if (preferredId is null)
        {
            RenderTagBadges();
            return;
        }

        _viewModel.SelectedClip = _viewModel.VisibleClips.FirstOrDefault(clip => clip.Id == preferredId)
            ?? _viewModel.VisibleClips.FirstOrDefault();
        RenderTagBadges();
    }

    private IReadOnlyList<string> GetTagsForClip() =>
        ParseTags(_viewModel.SelectedClip?.Tags);

    private void RenderTagBadges()
    {
        if (_viewModel.SelectedClip is null)
        {
            TagBadgesItemsControl.ItemsSource = null;
            TagBadgesSection.Visibility = Visibility.Collapsed;
            return;
        }

        var tags = GetTagsForClip();
        TagBadgesItemsControl.ItemsSource = tags;
        TagBadgesSection.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<string> ParseTags(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var tags = input
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().TrimStart('#'))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return tags;
    }

    private Task UpdateRecentSlotHintsAsync()
    {
        var slots = _viewModel.VisibleClips
            .Take(RecentHotkeySlotCount)
            .Select((clip, index) => new { clip.Id, Slot = index + 1 })
            .ToDictionary(item => item.Id, item => item.Slot, StringComparer.Ordinal);

        _recentSlotByClipId.Clear();
        foreach (var item in slots)
        {
            _recentSlotByClipId[item.Key] = item.Value;
        }

        HistoryListView.UpdateLayout();
        return Task.CompletedTask;
    }

    private void ExitEditMode()
    {
        var wasRecording = _isRecordingClipboardText;
        _isEditingTextClip = false;
        _isRecordingClipboardText = false;
        _recordingClipId = null;
        _viewModel.SetLiveInsertSuppressed(false);
        RecordButtonText.Text = "Record";
        SelectedPreviewTextBox.Visibility = Visibility.Collapsed;
        SelectedPreviewTextBlock.Visibility = Visibility.Visible;
        EditButtonsPanel.Visibility = Visibility.Collapsed;

        if (wasRecording)
        {
            _ = _viewModel.RefreshAsync();
        }
    }

    private static bool CanEditClipText(ClipboardClip? clip) =>
        clip is not null &&
        !string.IsNullOrWhiteSpace(clip.ContentText) &&
        (clip.Kind == ClipKind.Text || clip.Kind == ClipKind.Code || clip.Kind == ClipKind.Url || clip.Kind == ClipKind.Html);

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", Environment.NewLine, StringComparison.Ordinal);

    private static string BuildTitleFromText(string text, ClipKind kind)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return kind == ClipKind.Url ? "URL" : "Text";
        }

        return firstLine.Length <= 80 ? firstLine : $"{firstLine[..77]}...";
    }

    private static string BuildPreviewText(string text)
    {
        var compact = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
        return compact.Length <= 240 ? compact : $"{compact[..237]}...";
    }

    private void HistoryService_ClipAdded(object? sender, ClipboardClip clip)
    {
        _ = DispatcherQueue.TryEnqueue(async () => await UpdateRecentSlotHintsAsync());

        if (!_isRecordingClipboardText || !_isEditingTextClip || string.IsNullOrWhiteSpace(_recordingClipId))
        {
            return;
        }

        if (clip.Id.Equals(_recordingClipId, StringComparison.Ordinal))
        {
            return;
        }

        if (!IsRecordableTextClip(clip))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isRecordingClipboardText || !_isEditingTextClip || _viewModel.SelectedClip is null)
            {
                return;
            }

            if (!_viewModel.SelectedClip.Id.Equals(_recordingClipId, StringComparison.Ordinal))
            {
                return;
            }

            var snippet = NormalizeText(clip.ContentText!);
            if (string.IsNullOrWhiteSpace(snippet))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(SelectedPreviewTextBox.Text))
            {
                SelectedPreviewTextBox.Text += $"{Environment.NewLine}{Environment.NewLine}{snippet}";
            }
            else
            {
                SelectedPreviewTextBox.Text = snippet;
            }

            SelectedPreviewTextBox.Select(SelectedPreviewTextBox.Text.Length, 0);
        });
    }

    private static bool IsRecordableTextClip(ClipboardClip clip) =>
        (clip.Kind == ClipKind.Text || clip.Kind == ClipKind.Code || clip.Kind == ClipKind.Url) &&
        !string.IsNullOrWhiteSpace(clip.ContentText);

    private async Task ToggleRightPanelAsync()
    {
        if (_isRightPanelAnimating)
        {
            return;
        }

        _isRightPanelAnimating = true;
        try
        {
            if (_isRightPanelVisible)
            {
                await AnimateRightPanelAsync(show: false);
                RightPanelHost.Visibility = Visibility.Collapsed;
                DetailsColumn.Width = new GridLength(0);
                HistoryColumn.Width = new GridLength(1, GridUnitType.Star);
                _isRightPanelVisible = false;
                UpdateRightPanelToggleIcon();
                return;
            }

            HistoryColumn.Width = new GridLength(420);
            DetailsColumn.Width = new GridLength(1, GridUnitType.Star);
            RightPanelHost.Visibility = Visibility.Visible;
            Root.UpdateLayout();
            await AnimateRightPanelAsync(show: true);
            _isRightPanelVisible = true;
            UpdateRightPanelToggleIcon();
        }
        finally
        {
            _isRightPanelAnimating = false;
        }
    }

    private Task AnimateRightPanelAsync(bool show)
    {
        var completion = new TaskCompletionSource<object?>();
        var storyboard = new Storyboard();

        var opacityAnimation = new DoubleAnimation
        {
            Duration = TimeSpan.FromMilliseconds(RightPanelAnimationDurationMs),
            To = show ? 1 : 0
        };
        Storyboard.SetTarget(opacityAnimation, RightPanelHost);
        Storyboard.SetTargetProperty(opacityAnimation, nameof(UIElement.Opacity));

        var slideAnimation = new DoubleAnimation
        {
            Duration = TimeSpan.FromMilliseconds(RightPanelAnimationDurationMs),
            To = show ? 0 : RightPanelSlideOffset
        };
        Storyboard.SetTarget(slideAnimation, RightPanelTranslateTransform);
        Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.X));

        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(slideAnimation);

        if (show)
        {
            RightPanelHost.Opacity = 0;
            RightPanelTranslateTransform.X = RightPanelSlideOffset;
        }

        storyboard.Completed += (_, _) => completion.TrySetResult(null);
        storyboard.Begin();
        return completion.Task;
    }

    private void UpdateRightPanelToggleIcon()
    {
        if (GetRightPanelToggleIcon() is not FontIcon rightPanelToggleIcon ||
            GetRightPanelToggleButton() is not FrameworkElement rightPanelToggleButton)
        {
            return;
        }

        rightPanelToggleIcon.Glyph = _isRightPanelVisible ? "\uE8A1" : "\uE8A0";
        ToolTipService.SetToolTip(rightPanelToggleButton, _isRightPanelVisible ? "Hide details panel" : "Show details panel");
    }

    private FrameworkElement? GetRightPanelToggleButton() => Root.FindName("RightPanelToggleButton") as FrameworkElement;

    private FontIcon? GetRightPanelToggleIcon() => Root.FindName("RightPanelToggleIcon") as FontIcon;
}
