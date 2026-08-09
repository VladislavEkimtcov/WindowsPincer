# CreditPincher for Windows

A system-tray utility for tracking AI inference-credit spending on machines that do not
have a JetBrains IDE installed. It is a re-imagining of the
[CreditPincher IntelliJ plugin](https://github.com/VladislavEkimtcov/CreditPincher): same
data, same maths, same storage folder — but it lives in the notification area instead of
a tool window.

## What it does

- **Logs usage in two keystrokes.** `Ctrl+Alt+U` anywhere in Windows opens a one-box
  prompt; type an amount, press Enter, done.
- **Shows the month at a glance.** The tray icon turns blue → amber → red as you burn
  through the monthly budget, and the tooltip carries the month-to-date total.
- **Warns before you overspend.** Balloon notifications at configurable thresholds
  (80% and 100% by default), once per threshold per month.
- **Full dashboard on demand.** Date-range stats, a daily bar chart, editable history,
  and the budget — everything the IDE tool window showed.
- **Backs up and syncs over git.** Commit and push the storage folder on demand or on a
  timer; conflicts between two machines are merged entry-by-entry rather than
  one side winning.

## Shared storage with the plugin

Both apps read and write the same plain-text files:

```
%USERPROFILE%\.creditpincher\
    usage-log.csv        timestamp,amount   (ISO-8601 UTC + a number)
    monthly-budget.txt   one number, or empty
```

The formats are byte-compatible with the Kotlin plugin's output, so one machine can run
the IDE plugin, another can run the tray app, and a git remote can keep them in sync.
The tray app watches the folder, so a `git pull` (or the plugin writing an entry) shows
up in an open dashboard without a restart.

Per-machine preferences live separately, in
`%APPDATA%\CreditPincher\settings.json`, and are deliberately **not** synced.

## Running it

Requirements: Windows 10 (x64) and the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — or use a
self-contained build, which needs nothing installed. Git is optional and only needed for
backups.

```powershell
.\build.ps1
```

That runs the tests and publishes a single `dist\CreditPincher.exe`. Copy that one file
anywhere and run it. To bundle the runtime as well:

```powershell
.\build.ps1 -SelfContained
```

There is no installer and nothing is written outside your user profile. "Start with
Windows" (tray menu, or Settings) is a per-user `Run` registry value, so it never needs
administrator rights.

## Using it

| Action | How |
| --- | --- |
| Log usage | `Ctrl+Alt+U`, tray menu → *Log usage…*, or middle-click the tray icon |
| Open the dashboard | Left-click or double-click the tray icon |
| Back up now | Tray menu → *Back up now*, or the Backup tab |
| Quit | Tray menu → *Exit* |

The dashboard has four tabs:

- **Dashboard** — month total, budget progress, logging box, date range, chart, the full
  statistics table, and the monthly budget.
- **History** — every entry, newest first, with delete.
- **Backup** — storage path, git connect/commit/pull, automatic backup interval, and the
  raw git output when something goes wrong.
- **Settings** — credits vs dollars (and the conversion rate), start with Windows,
  notification thresholds, and the hotkey.

## Layout

```
src/CreditPincher.Core/     Platform-agnostic logic; no UI, no Windows APIs
    Models/                 CreditUsageEntry, UsageStats, YearMonth
    Services/               Storage, statistics, formatting, git, budget alerts, settings
src/CreditPincher.App/      WPF + tray shell (Windows only)
    Tray/                   Notification-area icon, menu, balloons, auto-backup
    Views/                  Dashboard, quick-log box, conflict resolver
    Controls/               UsageBarChart
    Platform/               Single instance, Run-key startup, global hotkey
tests/CreditPincher.Tests/  xUnit tests over Core
```

Everything worth testing lives in `CreditPincher.Core`, which targets plain `net8.0` — so
the test suite builds and runs on any OS, not just Windows:

```powershell
dotnet test
```

## Differences from the IDE plugin

| | Plugin | Tray app |
| --- | --- | --- |
| Entry point | Tool window in the IDE | Notification area, always available |
| Logging | Tool window text field | Global hotkey, tray menu, or dashboard |
| Budget warnings | Read them in the panel | Balloon notifications as thresholds pass |
| Backup | Manual button | Manual button plus an optional timer |
| Dollar conversion | Fixed at 100 credits = $1 | Configurable rate |
| History | Last 10 entries, read-only | Full list, deletable |
