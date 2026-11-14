# Daisey Tracker

Windows tray app (C#, .NET 8, WinForms). Tracks active window, AFK and audio locally. No cloud.

## Easy way (no install)

1. Download `DaiseyTracker.exe` once from the repo's **Releases** (latest). Self-contained, no .NET needed.
2. Put it somewhere stable (e.g. `C:\Tools\DaiseyTracker\`), double-click. Tick **Start with Windows** in the tray menu.
3. It **auto-updates**: checks GitHub releases a minute after start and every 6 hours, swaps itself and restarts. Tray: *Check for updates*.
4. Install **Google Drive for desktop**. The app finds `X:\My Drive` and mirrors summaries to `My Drive\DaiseyTracker\daisey-YYYY-MM-DD.json` every 15 min. Other sync folder: put its path in `%LOCALAPPDATA%\DaiseyTracker\sync-folder.txt`.
5. Open the claude.ai dashboard artifact. It reads those files from your Drive (allow the Google Drive connector once).

Local dashboard also at `http://localhost:5757` (double-click the tray icon).
Windows SmartScreen may warn (unsigned exe): More info > Run anyway.

## Build / run from source

Needs Windows + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
cd src\DaiseyTracker
dotnet run
```

Single exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

Run `publish\DaiseyTracker.exe`. Tray icon appears. Menu: Open dashboard, Pause/Resume, Export today,
Open data folder, Start with Windows, Check for updates, Quit.
Local builds are version 0.0.0 and never self-update.

Packages: Microsoft.Data.Sqlite, NAudio. Nothing else.

## Data (`%LOCALAPPDATA%\DaiseyTracker\`)

| Path | What |
|---|---|
| `events.db` | SQLite, table `events(stream, ts, duration, data)`. Streams: `window`, `afk`, `audio`. |
| `rules.json` | Your rules. Copied from the exe folder on first run. |
| `summary\YYYY-MM-DD.json` | Daily summary. Written at local midnight, on quit, and on "Export today". |
| `error.log` | Only if something failed. |

### How it records

- Poll every 1s. Heartbeat: same data as last event in the stream and gap <= 3s extends it, else new row.
- `window`: process name + title, only while not AFK.
- `afk`: AFK after 3 min without input. Event is back-dated to the last input, so those 3 min are cut from window time.
- `audio`: one event per process with a WASAPI session peak > 0 (background Spotify counts). Own stream, never added to project time.

### Summary

```json
{
  "date": "2026-11-14",
  "projectMinutes": { "Claude": 95.4, "Dev": 40.1 },
  "categoryMinutes": { "Deep": 120.0, "Support": 15.5, "Other": 30.0, "Waiting": 9.2 },
  "audioMinutesByProcess": { "spotify.exe": 210.0 }
}
```

No window titles in the summary. Summary is computed from `events.db` on demand, using the
current `rules.json`, so rule edits apply to old days too when re-exported (use "Export today" for today).

## rules.json

Regexes are case-insensitive. Rules checked in order, first match wins. No match = `Other`.
Omit `process` or `title` to match anything. Categories: `Deep`, `Support`, `Other`, `Waiting`.
Comments and trailing commas are allowed. Process is matched as `name.exe`.

```json
{
  "waitingGraceSeconds": 180,
  "waitingTitles": ["YouTube", "Reddit", "Twitter", "/ X$", "Facebook", "Instagram", "TikTok", "Hacker News", "LinkedIn"],
  "rules": [
    { "process": "^claude\\.exe$", "project": "Claude", "category": "Deep" },
    { "title": "Claude", "project": "Claude", "category": "Deep" },
    { "process": "^(Code|devenv|rider64|idea64)\\.exe$", "project": "Dev", "category": "Deep" },
    { "process": "^(WindowsTerminal|powershell|pwsh|cmd)\\.exe$", "project": "Dev", "category": "Support" },
    { "process": "^(chrome|msedge|firefox)\\.exe$", "title": "GitHub|Stack Overflow|docs", "project": "Dev", "category": "Support" },
    { "process": "^(slack|Discord|ms-teams|OUTLOOK)\\.exe$", "project": "Comms", "category": "Support" }
  ]
}
```

Special rules (built in):

- **Claude window** = process `claude.exe` OR title contains `Claude` (case-sensitive). Classified by your rules like any window.
- **Waiting** = window whose title matches `waitingTitles`, within `waitingGraceSeconds` (3 min) after a Claude window was last active. Counts as project `Waiting`, category `Waiting`. After the grace period the window falls back to normal rules. Title only, no scroll detection.

## Notes

- Not compiled/tested on Windows by the author of this commit. Build on your PC first.
- Midnight split uses local time. Summary for the finished day is written by a 30s check, so a PC asleep at midnight writes it on wake.
- Foreground UWP apps may show as `ApplicationFrameHost.exe`.
