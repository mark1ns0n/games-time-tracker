# GameTimeTracker

Windows game-time tracker for local play sessions across Steam, EA App, Epic, GOG, Battle.net, standalone games, and anything else that exposes a Windows process.

## Product shape

The core mapping is:

```text
Game profile
- beautiful name
- one or more process names
- optional executable path
- optional icon path
- play intervals
- capacity rules / game-time budget
```

A play interval is stored as a separate record so the app can show totals, per-day stats, per-week stats, and manual adjustments without losing history.

## MVP target

The first usable version should:

- Let you add a game profile manually.
- Map a friendly name to one or more process names, for example `bg3.exe`.
- Poll running processes every few seconds.
- Start an interval when a mapped process appears.
- Close the interval when the process disappears.
- Show current session, total hours, and today/week totals.
- Let you manually add or edit an interval.
- Let you set game-time capacity and calculate remaining time.

## Language choice

C#/.NET 10 LTS is the best default for this app on Windows because it has first-class access to Windows processes, icons, tray behavior, startup integration, and native desktop UI. Rust/Tauri is a strong alternative if cross-platform support becomes important, but it adds more packaging and native API complexity. Python/Electron can work, but both are heavier or less native for a background Windows tracker.

## Local build and launch

The project targets .NET 10 LTS.

Build:

```powershell
dotnet build C:\projects\GameTimeTracker\src\GameTimeTracker\GameTimeTracker.csproj
```

Run without a terminal window:

```text
C:\projects\GameTimeTracker\GameTimeTracker.lnk
```

Direct terminal run is still useful for debugging, but the terminal stays attached while the app is open:

```powershell
dotnet C:\projects\GameTimeTracker\src\GameTimeTracker\bin\Debug\net10.0-windows\GameTimeTracker.dll
```

