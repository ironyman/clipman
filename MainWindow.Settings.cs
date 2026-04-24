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
            PasteSelected = CloneBinding(_hotKeySettings.PasteSelected),
            TogglePin = CloneBinding(_hotKeySettings.TogglePin),
            PasteRecent = _hotKeySettings.PasteRecent.Select(CloneBinding).ToList(),
            StartOnWindowsBoot = _hotKeySettings.StartOnWindowsBoot
        };

        while (working.PasteRecent.Count < RecentHotkeySlotCount)
        {
            var idx = working.PasteRecent.Count + 1;
            working.PasteRecent.Add(new HotKeyBinding { Modifier = "Alt", Key = idx.ToString() });
        }

        var bindings = new Dictionary<string, HotKeyBinding>(StringComparer.Ordinal)
        {
            ["toggle_window"] = working.ToggleWindow,
            ["paste_selected"] = working.PasteSelected,
            ["toggle_pin"] = working.TogglePin
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
            Text = "Click Set, then press shortcut.",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };

        var startOnBootCheckBox = new CheckBox
        {
            Content = "Start on Windows boot",
            IsChecked = working.StartOnWindowsBoot
        };

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
        Grid.SetRow(startOnBootCheckBox, 1);
        startOnBootCheckBox.Margin = new Thickness(0, 10, 0, 6);
        contentRoot.Children.Add(startOnBootCheckBox);
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
                captureTarget = null;
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
            captureHint.Text = "Click Set, then press shortcut.";
            captureTarget = null;
            e.Handled = true;
        };

        AddHotKeyRow(rowsPanel, "Open/Toggle Window", "toggle_window");
        AddHotKeyRow(rowsPanel, "Paste Selected Clip", "paste_selected");
        AddHotKeyRow(rowsPanel, "Toggle Pin", "toggle_pin");
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
            return;
        }

        _hotKeySettings = new HotKeySettings
        {
            ToggleWindow = bindings["toggle_window"],
            PasteSelected = bindings["paste_selected"],
            TogglePin = bindings["toggle_pin"],
            PasteRecent = Enumerable.Range(1, RecentHotkeySlotCount)
                .Select(index => bindings[$"paste_recent_{index}"])
                .Select(CloneBinding)
                .ToList(),
            StartOnWindowsBoot = startOnBootCheckBox.IsChecked == true
        };
        _settingsService.SaveHotKey(_hotKeySettings);
        _hotKeyService.Register(_hotKeySettings);
        ApplyStartupSetting(_hotKeySettings.StartOnWindowsBoot);

        void AddHotKeyRow(Panel panel, string label, string id)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var left = new StackPanel { Spacing = 4 };
            left.Children.Add(new TextBlock { Text = label });
            var valueBox = new TextBox
            {
                Text = bindings[id].ToString(),
                IsReadOnly = true,
                Width = 260
            };
            left.Children.Add(valueBox);
            displayBoxes[id] = valueBox;
            row.Children.Add(left);

            var setButton = new Button
            {
                Content = "Set",
                VerticalAlignment = VerticalAlignment.Bottom
            };
            setButton.Click += (_, _) =>
            {
                captureTarget = id;
                captureHint.Text = $"Press new shortcut for {label} (Esc to clear).";
                _ = contentRoot.Focus(FocusState.Programmatic);
            };
            Grid.SetColumn(setButton, 1);
            row.Children.Add(setButton);

            var clearButton = new Button
            {
                Content = "Clear",
                VerticalAlignment = VerticalAlignment.Bottom
            };
            clearButton.Click += (_, _) =>
            {
                bindings[id].Modifier = string.Empty;
                bindings[id].Key = string.Empty;
                displayBoxes[id].Text = "None";
                captureTarget = null;
                captureHint.Text = "Click Set, then press shortcut.";
            };
            Grid.SetColumn(clearButton, 2);
            row.Children.Add(clearButton);

            panel.Children.Add(row);
        }
    }

    private static HotKeyBinding CloneBinding(HotKeyBinding binding) =>
        new()
        {
            Modifier = binding.Modifier,
            Key = binding.Key
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
