using Clipman.Models;
using Clipman.Services;
using Clipman.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;

namespace Clipman;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly EsentClipboardHistoryService _historyService = new();
    private readonly MainViewModel _viewModel;
    private readonly GlobalHotKeyService _hotKeyService;
    private HotKeySettings _hotKeySettings;
    private SearchPopupWindow? _searchPopupWindow;

    public MainWindow()
    {
        InitializeComponent();

        _hotKeySettings = _settingsService.LoadHotKey();
        _viewModel = new MainViewModel(_historyService);
        Root.DataContext = _viewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        TryEnableMica();

        _hotKeyService = new GlobalHotKeyService(WindowNative.GetWindowHandle(this));
        _hotKeyService.Pressed += HotKeyService_Pressed;
        _hotKeyService.Register(_hotKeySettings);
        Clipboard.ContentChanged += Clipboard_ContentChanged;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        await _viewModel.LoadAsync();
        await _historyService.CaptureCurrentClipboardAsync();
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

    private async void Clipboard_ContentChanged(object? sender, object e)
    {
        await _historyService.CaptureCurrentClipboardAsync();
    }

    private void HotKeyService_Pressed(object? sender, EventArgs e)
    {
        ShowSearchPopup();
    }

    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ShowSearchPopup();
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

    private void ShowSearchPopup()
    {
        if (_searchPopupWindow is null)
        {
            _searchPopupWindow = new SearchPopupWindow(_viewModel);
            _searchPopupWindow.Closed += SearchPopupWindow_Closed;
        }

        _searchPopupWindow.Activate();
    }

    private void SearchPopupWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_searchPopupWindow is not null)
        {
            _searchPopupWindow.Closed -= SearchPopupWindow_Closed;
            _searchPopupWindow = null;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Clipboard.ContentChanged -= Clipboard_ContentChanged;
        _hotKeyService.Dispose();
        _historyService.Dispose();
    }

    private void TryEnableMica()
    {
        SystemBackdrop = new MicaBackdrop();
    }
}
