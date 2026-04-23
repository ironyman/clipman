# Clipman: Modern Ditto-Inspired Clipboard Manager Spec

## Reference Baseline

Ditto is a Windows clipboard extension that records each item copied to the clipboard and lets the user retrieve it later. Its official usage model is simple: run in the background, copy normally, open the history from the tray or the default `Ctrl + \`` hotkey, then double-click or press Enter to paste into the previous window. It supports text, images, HTML, custom clipboard formats, local-first operation, and no cloud login.

Recent public descriptions of Ditto emphasize the same core strengths: persistent history beyond Windows' small clipboard stack, instant search, pinned or sticky clips, groups, image thumbnails, drag-and-drop paste, hotkeys, optional encrypted local-network sync, settings, backup/restore, and stats.

## Product Direction

Clipman should feel like a native Windows 11 productivity surface rather than a legacy popup. It keeps Ditto's speed-first workflow but gives it a modern WinUI 3 shell:

- Fast command-palette style open with `Ctrl + \``.
- Local-first storage by default.
- Searchable chronological clipboard history.
- Pinned clips that stay above transient history.
- Type filters for text, code, URL, image, HTML, and files.
- Detail preview for richer clips.
- Compact navigation for History, Pinned, Groups, Sync, Stats, and Options.
- Keyboard-first paste flow with mouse and touch affordances.
- Fluent styling with Mica, rounded 8px surfaces, clear icon actions, and dark/light theme support.

## Primary Users

- Developers reusing code snippets, terminal commands, URLs, and stack traces.
- Writers and researchers collecting quotes, references, and source material.
- Operators and support teams pasting repeated responses or file paths.
- Power users who want local persistence without a cloud account.

## Core Workflows

### Open And Paste

1. User presses `Ctrl + \`` from any app.
2. Clipman opens as a compact floating window near the active monitor.
3. Search field is focused.
4. User arrows to a clip or types to filter.
5. Enter pastes the selected clip into the previously focused app.

### Search History

1. User opens Clipman.
2. User types any title, preview text, source app, URL, or optional regex.
3. Results update immediately with pinned items first.
4. User can filter by type without losing the query.

### Manage Clips

1. User selects a clip.
2. Detail preview shows full text or a thumbnail/metadata placeholder.
3. User can paste, copy back to clipboard, pin, edit text clips, delete, or move to group.

### Groups

1. User creates groups for reusable snippets.
2. Clips can be moved into groups manually or by rule.
3. Groups appear in the left navigation and support search within group.

### Privacy And Retention

1. User can pause capture.
2. User can exclude apps such as password managers.
3. User can clear history by age, count, type, or source.
4. Sensitive formats can be ignored or stored ephemerally.

## Information Architecture

- History: chronological stream, newest first, pinned first.
- Pinned: sticky clips and templates.
- Groups: user-defined collections.
- Sync: local-network pairing, status, encryption keys.
- Stats: copied/pasted counts, top source apps, storage size.
- Options: hotkeys, retention, excluded apps, database path, theme, import/export.

## UI Implementation In This Repo

The current implementation is a WinUI 3 shell with mock clipboard data:

- `MainWindow.xaml`: modern three-column Fluent layout.
- `ViewModels/MainViewModel.cs`: filtering, selection, counts, and sample command boundary.
- `Services/IClipboardHistoryService.cs`: seam for real clipboard history.
- `Services/DesignClipboardHistoryService.cs`: realistic sample data for UI development.
- `Models/ClipboardClip.cs`: clip summary model for list/detail views.

## Engineering Plan

1. Replace `DesignClipboardHistoryService` with `ClipboardMonitorService`.
2. Listen for clipboard changes with `AddClipboardFormatListener` on the WinUI window handle.
3. Extract standard formats: Unicode text, HTML, bitmap, file drop list, and URLs.
4. Store clip metadata and payloads in SQLite.
5. Add a paste coordinator that restores the selected clip to the clipboard, reactivates the previous foreground window, and sends the configured paste shortcut.
6. Add global hotkey registration for open/close and direct paste slots.
7. Add tray integration for show, pause capture, clear history, and quit.
8. Add settings persistence and excluded-app rules.
9. Add thumbnail generation for images and rich HTML preview.
10. Package as MSIX once the background/tray behavior is complete.

## Non-Goals For The First Slice

- Cloud sync.
- Full custom clipboard format round-tripping.
- Plugin API.
- OCR or AI classification.
- Cross-platform support.

## Build Requirements

Microsoft's current WinUI guidance recommends Visual Studio with the WinUI application development workload, Developer Mode enabled, and the Windows App SDK templates. This repo targets Windows App SDK `1.8.260317003` and `net8.0-windows10.0.19041.0`.
