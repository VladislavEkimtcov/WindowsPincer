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

Requirements: Windows 10 or 11 (x64). Nothing else — no runtime, no SDK, no installer.
Git is optional and only needed for backups.

```powershell
.\build.ps1
```

That runs the tests and produces a single `dist\CreditPincher.exe` of about 140 KB.
Copy that one file anywhere and run it.

### The build needs nothing installed

Everything is compiled by the tools that already live inside Windows, under
`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`:

- `MSBuild.exe` (with `Microsoft.WinFx.targets`) compiles the XAML and the app,
- `csc.exe` compiles the test suite,
- the output targets **.NET Framework 4.8**, which ships with every Windows 10/11 install.

This is deliberate. The app was originally written on macOS against .NET 8 and an
SDK-style project, which cannot be built — or run — on a machine without the .NET SDK
installed. Locked-down and industrial Windows images routinely have neither the SDK
nor a package manager to fetch one, so the project was moved onto the in-box
toolchain instead. Two consequences worth knowing:

- **The source is C# 5.** That is the newest language version the in-box compiler
  accepts. The .NET-8-only pieces the code relied on (`DateOnly`, `TimeProvider`,
  `System.Text.Json`, `Math.Clamp`, `MaxBy`, …) are reimplemented in
  `src/CreditPincher.Core/Compat/`.
- **`MSB3644` during the build is expected.** Without the .NET SDK there are no
  targeting packs, so MSBuild resolves references from the GAC. The build is fine.

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
src/CreditPincher.Core/     Logic only; no UI
    Models/                 CreditUsageEntry, UsageStats, YearMonth
    Services/               Storage, statistics, formatting, git, budget alerts, settings
    Compat/                 Stand-ins for .NET 8 types absent from .NET Framework 4.8
src/CreditPincher.App/      WPF + tray shell, and the only project file in the repo
    Tray/                   Notification-area icon, menu, balloons, auto-backup
    Views/                  Dashboard, quick-log box, conflict resolver
    Controls/               UsageBarChart
    Platform/               Single instance, Run-key startup, global hotkey
tests/CreditPincher.Tests/  Tests over Core, plus a ~150-line xUnit stand-in
```

`CreditPincher.Core` has no project file: its sources are compiled directly into both
the app and the test runner. That is what keeps the shipped product a single
dependency-free executable.

The tests keep their original `[Fact]` / `[Theory]` / `Assert` shape. xUnit itself
arrives through NuGet, which a machine without the .NET SDK cannot use, so
`tests/CreditPincher.Tests/MiniXunit.cs` reimplements the handful of attributes and
assertions the suite needs and runs them as a console executable:

```powershell
.\build.ps1                 # compiles and runs the tests, then builds the app
tests\CreditPincher.Tests\bin\CreditPincher.Tests.exe   # just the tests
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

## Installing

There is nothing to install, but the tidy place for the executable is:

```powershell
$dir = "$env:LOCALAPPDATA\Programs\CreditPincher"
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item dist\CreditPincher.exe $dir -Force
& "$dir\CreditPincher.exe"
```

Then turn on *Start with Windows* from the tray menu. To do the same without opening
the app (it writes exactly the value the tray menu would):

```powershell
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name CreditPincher `
    -Value "`"$env:LOCALAPPDATA\Programs\CreditPincher\CreditPincher.exe`" --tray"
```

The `--tray` flag suppresses the dashboard at sign-in even when *Open the dashboard on
startup* is enabled, so an auto-start is always silent.
