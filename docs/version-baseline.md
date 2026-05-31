# Version Baseline

Checked on 2026-05-31.

## Chosen stack

- Platform: Windows desktop
- Language: C# 14.0
- Runtime/framework: .NET 10 LTS
- Target framework: `net10.0-windows`
- SDK to install: .NET SDK 10.0.300 or newer 10.0.x SDK
- Desktop UI for MVP: Windows Forms
- Storage for MVP: local JSON, then SQLite after behavior is stable

## Why this stack

.NET 10 is the latest supported LTS line. It gives the app the best Windows desktop integration with a long support window, current C# language support, and a straightforward path to tray, startup, process inspection, icon extraction, and foreground-window tracking.

## Later optional versions

- SQLite package: choose the latest stable `Microsoft.Data.Sqlite` when starting the SQLite task.
- Installer tooling: decide during packaging; likely MSIX or a lightweight installer once the app shape is stable.
- Visual Studio: optional. CLI with .NET SDK is enough for MVP.
