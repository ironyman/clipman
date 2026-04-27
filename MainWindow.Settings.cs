using Clipman.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.System;

namespace Clipman;

public sealed partial class MainWindow
{
    private const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRunValueName = "Clipman";

    private async Task ShowSettingsDialogAsync()
    {
        var working = new HotKeySettings
        {
            ToggleWindow = CloneBinding(_hotKeySettings.ToggleWindow),
            ToggleRightPanel = CloneBinding(_hotKeySettings.ToggleRightPanel),
            PasteSelected = CloneBinding(_hotKeySettings.PasteSelected),
            TogglePin = CloneBinding(_hotKeySettings.TogglePin),
            FileSearchMode = CloneBinding(_hotKeySettings.FileSearchMode),
            PasteRecent = _hotKeySettings.PasteRecent.Select(CloneBinding).ToList(),
            StartOnWindowsBoot = _hotKeySettings.StartOnWindowsBoot,
            FileSearchServiceEnabled = _hotKeySettings.FileSearchServiceEnabled,
            DetailsPanelExpanded = _hotKeySettings.DetailsPanelExpanded
        };

        while (working.PasteRecent.Count < RecentHotkeySlotCount)
        {
            var idx = working.PasteRecent.Count + 1;
            working.PasteRecent.Add(new HotKeyBinding { Modifier = "Alt", Key = idx.ToString(), IsGlobal = false });
        }

        var bindings = new Dictionary<string, HotKeyBinding>(StringComparer.Ordinal)
        {
            ["toggle_window"] = working.ToggleWindow,
            ["toggle_right_panel"] = working.ToggleRightPanel,
            ["paste_selected"] = working.PasteSelected,
            ["toggle_pin"] = working.TogglePin,
            ["file_search_mode"] = working.FileSearchMode
        };

        for (var i = 0; i < RecentHotkeySlotCount; i++)
        {
            bindings[$"paste_recent_{i + 1}"] = working.PasteRecent[i];
        }

        var displayBoxes = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        string? captureTarget = null;

        var rowsPanel = new StackPanel { Spacing = 10 };

        var captureHint = new TextBlock
        {
            Text = "Click Set or the shortcut box, then press shortcut.",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };

        var startOnBootCheckBox = new CheckBox
        {
            Content = "Start on Windows boot",
            IsChecked = working.StartOnWindowsBoot
        };
        var detailsPanelExpandedCheckBox = new CheckBox
        {
            Content = "Show details panel",
            IsChecked = working.DetailsPanelExpanded
        };
        var fileSearchServiceEnabledCheckBox = new CheckBox
        {
            Content = "Enable file search indexing service",
            IsChecked = working.FileSearchServiceEnabled
        };
        var behaviorPanel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 6)
        };
        behaviorPanel.Children.Add(startOnBootCheckBox);
        behaviorPanel.Children.Add(fileSearchServiceEnabledCheckBox);
        behaviorPanel.Children.Add(detailsPanelExpandedCheckBox);

        var contentRoot = new Grid
        {
            Width = 760,
            MinWidth = 760,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        Grid.SetRow(captureHint, 0);
        contentRoot.Children.Add(captureHint);
        Grid.SetRow(behaviorPanel, 1);
        contentRoot.Children.Add(behaviorPanel);
        Grid.SetRow(rowsPanel, 2);
        contentRoot.Children.Add(rowsPanel);

        contentRoot.KeyDown += (_, e) =>
        {
            if (captureTarget is null)
            {
                return;
            }

            if (e.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            {
                e.Handled = true;
                return;
            }

            if (!bindings.TryGetValue(captureTarget, out var binding))
            {
                StopCaptureMode();
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                binding.Modifier = string.Empty;
                binding.Key = string.Empty;
            }
            else
            {
                binding.Modifier = ModifierFlagsToString(GetCurrentModifierFlags());
                binding.Key = VirtualKeyToHotKeyString(e.Key);
            }

            displayBoxes[captureTarget].Text = binding.ToString();
            StopCaptureMode();
            e.Handled = true;
        };

        AddHotKeyRow(rowsPanel, "Open/Toggle Window", "toggle_window");
        AddHotKeyRow(rowsPanel, "Show/Hide Details Panel", "toggle_right_panel");
        AddHotKeyRow(rowsPanel, "Paste Selected Clip", "paste_selected");
        AddHotKeyRow(rowsPanel, "Toggle Pin", "toggle_pin");
        AddHotKeyRow(rowsPanel, "Toggle File Search Mode", "file_search_mode");
        for (var i = 0; i < RecentHotkeySlotCount; i++)
        {
            AddHotKeyRow(rowsPanel, $"Paste {i + 1} (Recent)", $"paste_recent_{i + 1}");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Settings",
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = contentRoot
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            StopCaptureMode();
            return;
        }

        StopCaptureMode();

        _hotKeySettings = new HotKeySettings
        {
            ToggleWindow = bindings["toggle_window"],
            ToggleRightPanel = bindings["toggle_right_panel"],
            PasteSelected = bindings["paste_selected"],
            TogglePin = bindings["toggle_pin"],
            FileSearchMode = bindings["file_search_mode"],
            PasteRecent = Enumerable.Range(1, RecentHotkeySlotCount)
                .Select(index => bindings[$"paste_recent_{index}"])
                .Select(CloneBinding)
                .ToList(),
            StartOnWindowsBoot = startOnBootCheckBox.IsChecked == true,
            FileSearchServiceEnabled = fileSearchServiceEnabledCheckBox.IsChecked == true,
            DetailsPanelExpanded = detailsPanelExpandedCheckBox.IsChecked == true
        };
        _settingsService.SaveHotKey(_hotKeySettings);
        _hotKeyService.Register(_hotKeySettings);
        ApplyStartupSetting(_hotKeySettings.StartOnWindowsBoot);
        ApplyFileSearchServiceSetting(_hotKeySettings.FileSearchServiceEnabled);
        if (_isRightPanelVisible != _hotKeySettings.DetailsPanelExpanded)
        {
            await SetRightPanelVisibilityAsync(_hotKeySettings.DetailsPanelExpanded, animate: false);
        }

        void AddHotKeyRow(Panel panel, string label, string id)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(250) },
                    new ColumnDefinition { Width = new GridLength(260) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(labelBlock);

            var valueBox = new TextBox
            {
                Text = bindings[id].ToString(),
                IsReadOnly = true,
                Width = 260
            };
            valueBox.PointerPressed += (_, _) => StartCaptureMode(id, label);
            valueBox.GotFocus += (_, _) => StartCaptureMode(id, label);
            displayBoxes[id] = valueBox;
            Grid.SetColumn(valueBox, 1);
            row.Children.Add(valueBox);

            var setButton = new Button
            {
                Content = "Set",
                VerticalAlignment = VerticalAlignment.Center
            };
            setButton.Click += (_, _) => StartCaptureMode(id, label);
            Grid.SetColumn(setButton, 2);
            row.Children.Add(setButton);

            var clearButton = new Button
            {
                Content = "Clear",
                VerticalAlignment = VerticalAlignment.Center
            };
            clearButton.Click += (_, _) =>
            {
                bindings[id].Modifier = string.Empty;
                bindings[id].Key = string.Empty;
                displayBoxes[id].Text = "None";
                if (captureTarget == id)
                {
                    StopCaptureMode();
                }
            };
            Grid.SetColumn(clearButton, 3);
            row.Children.Add(clearButton);

            var globalCheckBox = new CheckBox
            {
                Content = "Global",
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = bindings[id].IsGlobal == true
            };
            globalCheckBox.Checked += (_, _) => bindings[id].IsGlobal = true;
            globalCheckBox.Unchecked += (_, _) => bindings[id].IsGlobal = false;
            Grid.SetColumn(globalCheckBox, 4);
            row.Children.Add(globalCheckBox);

            panel.Children.Add(row);
        }

        void StartCaptureMode(string id, string label)
        {
            captureTarget = id;
            captureHint.Text = $"Press new shortcut for {label} (Esc to clear).";
            _ = contentRoot.Focus(FocusState.Programmatic);
            _hotKeyService.BeginConfigurationCapture();
        }

        void StopCaptureMode()
        {
            captureTarget = null;
            captureHint.Text = "Click Set or the shortcut box, then press shortcut.";
            _hotKeyService.EndConfigurationCapture();
        }
    }

    private static HotKeyBinding CloneBinding(HotKeyBinding binding) =>
        new()
        {
            Modifier = binding.Modifier,
            Key = binding.Key,
            IsGlobal = binding.IsGlobal
        };

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath, false);
        var value = key?.GetValue(StartupRunValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void ApplyStartupSetting(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRunKeyPath, true);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(StartupRunValueName, false);
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return;
        }

        key.SetValue(StartupRunValueName, $"\"{processPath}\"");
    }
}
