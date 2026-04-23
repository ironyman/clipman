# Clipman

A modern WinUI 3 clipboard manager prototype inspired by Ditto.

## What Is Here

- A C# WinUI 3 desktop app shell.
- Fluent/Mica three-column clipboard UI.
- Search, type filters, pinned/recent list, and detail preview.
- MVVM-friendly models, view model, and clipboard service interface.
- A product spec in `SPEC.md` based on Ditto's public behavior and feature set.

## Requirements

- Windows 10 1809 or newer, Windows 11 recommended.
- Visual Studio with the WinUI application development workload.
- Developer Mode enabled.
- .NET SDK available through Visual Studio or `dotnet` on PATH.

## Run

Open `Clipman.csproj` in Visual Studio and press F5.

This machine does not currently expose `dotnet` on PATH, so the project was scaffolded manually and not compiled from this shell.

## Next Implementation Step

Replace `DesignClipboardHistoryService` with a real clipboard monitor and SQLite-backed history store, then wire global hotkey and tray behavior around the existing UI.
