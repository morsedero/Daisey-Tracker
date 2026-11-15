# Daisey Tracker

Windows tray app (C#, .NET 8, WinForms). Tracks which apps you use, when you're away, and what's playing audio.

## Use it

1. Download `DaiseyTracker.exe` from **Releases > latest**. Self-contained, no .NET needed.
2. Put it in a fixed folder (e.g. `C:\Tools\DaiseyTracker\`), double-click. Tray menu: tick **Start with Windows**.
   Don't move the exe while it runs.
3. Install **Google Drive for desktop**. Summaries go to `My Drive\DaiseyTracker\daisey-YYYY-MM-DD.json` every 1 min.
4. Open the claude.ai dashboard artifact (reads those files from your Drive).

It auto-updates from GitHub releases (a minute after start, then every minute; tray: *Check for updates*).
Local dashboard: `http://localhost:5757` (double-click the tray icon).
SmartScreen may warn (unsigned exe): More info > Run anyway.

## What it records

Polls every 1s into `%LOCALAPPDATA%\DaiseyTracker\events.db` (SQLite, `events(stream, ts, duration, data)`):

- `window`: foreground process + title, only while not AFK.
- `afk`: AFK after 3 min without input, back-dated to the last input.
- `audio`: processes with a WASAPI session peak > 0 (background Spotify counts). Separate from active time.

Heartbeat merge: same data as the stream's last event and gap <= 3s extends it, else a new row.

## Daily summary

`summary\YYYY-MM-DD.json` in the data folder (copied to Drive). Written every 1 min, at midnight, on quit and via *Export today*. No window titles: browser time is grouped by a site name guessed from the end of the tab title ("... - YouTube" -> YouTube), else "Other". `detailMinutes` is the project/file open inside each app, guessed from "item - App" titles (top 6 per app, name only).

```json
{
  "date": "2026-11-15",
  "activeMinutes": 312.4,
  "appMinutes": { "chrome.exe": 140.2, "Code.exe": 98.0 },
  "siteMinutes": { "YouTube": 40.0, "GitHub": 25.5, "Other": 30.1 },
  "detailMinutes": { "Code.exe": { "DaiseyTracker": 80.0 }, "EXCEL.EXE": { "Budget.xlsx": 12.0 } },
  "timeline": [[540, 585, "Code.exe"], [585, 600, "chrome.exe"]],
  "hourlyMinutes": [0, 0, 0, 0, 0, 0, 0, 0, 0, 42.1, 55.0, 48.3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  "audioMinutesByProcess": { "Spotify.exe": 210.0 }
}
```

Errors: `error.log` in the data folder, also copied to `My Drive\DaiseyTracker\daisey-error.log`.
Other sync folder: put its path in `%LOCALAPPDATA%\DaiseyTracker\sync-folder.txt`.

## Build from source

Windows + .NET 8 SDK:

```
cd src\DaiseyTracker
dotnet run
```

Local builds are version 0.0.0 and never self-update. CI (`.github/workflows/build.yml`) builds a single-file exe and publishes release `v1.0.<run>`.
Packages: Microsoft.Data.Sqlite, NAudio.
