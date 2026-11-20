# Break App

A lightweight Windows tray app that reminds you to take breaks — on your schedule, in the style you want.
Inspired by [Stretchly](https://hovancik.net/stretchly/), but native (C# / WPF), lighter, with a real input lock and a lot more customization.

## Features

- **Unlimited break rules** — "every X, break for Y". Each rule has its own style, warning, lock level, sounds, messages and schedule.
- **Timer modes per rule** — *active time* (only while you use the PC), *clock time*, or *fixed clock times* (:00, :20, :40…).
- **Break styles**
  - **Notification** — a card in the corner (same look as HiSync's notifications) with a live countdown. It can **glow**, **pulse** (grow and shrink) or both, so you notice it.
  - **Window** — a centered card with a countdown.
  - **Fullscreen** — covers every (or chosen) monitor; ring, bar or big-number countdown.
- **Lock levels** — Skippable · Locked (keyboard + mouse blocked, Alt+Tab and Win key too; your escape combo ends it) · **TOTAL LOCK** (no way out; asks "ARE YOU SURE?" first).
- **Warnings** — a notification and/or sound before each break, with Start now / Postpone / Skip buttons (each can be turned off).
- **Smart pausing** — pauses during calls (mic or camera in use: Google Meet, Zoom, Teams, Discord…), quiet times (e.g. lessons), apps you list as exceptions, Do Not Disturb (optional), or manually from the tray. Games do **not** pause breaks unless you add them as an exception.
- **Natural breaks** — time away from the PC counts as a break. Gamepad input and watching video count as activity.
- **Overlapping rules** — bigger break wins (default), run both, or reset all timers.
- **Commitment mode** (optional) — blocks skipping/pausing/quitting shortly before strict breaks; weakening strict rules waits for a cooldown.
- **Customize almost everything** — every text the app shows (with `{rule}`, `{time}`… placeholders), theme, accent presets or any custom colour, break-screen colours, opacity, background image, text size, notification corner, sounds, shortcuts, 12/24-hour clock.
- **Stats** — taken / skipped / postponed / natural breaks per day and per rule, streaks, CSV export.
- **Presets** — Gentle · Balanced · Eye care · Strict (shows exactly what will change before applying).

## Settings

Tray icon → **Settings…** (or double-click / left-click the icon). Categories:

Rules · Warnings · Lock & Commitment · Pausing & Meetings · Activity & Idle · Appearance · Texts · Sounds · Stats · General

Every setting has a one-line description and an ⓘ tip. **Search** (Ctrl+F) finds any setting; **Advanced** shows the rarely used ones. Changes apply when you press **Save** (Ctrl+S); appearance changes preview live.

## About the input lock

The lock is *best effort* and needs no admin rights. These always get you out, by Windows design:

- **Ctrl+Alt+Del** → Task Manager (no app can block it)
- admin windows that were already open, and UAC prompts

## Build & run

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, .NET 10 Desktop Runtime to run.

```powershell
scripts\build.ps1                 # runs the tests, publishes dist\BreakApp.exe
scripts\build.ps1 -SelfContained  # bigger exe that doesn't need the runtime installed
```

Data lives in `%AppData%\BreakApp\` (`settings.json`, `history.jsonl`, `app.log`).

### Testing switches

| Switch | What it does |
|---|---|
| `BREAKAPP_FAST=1` (env) | Time runs 60× faster: a 20 min rule fires after 20 s. |
| `BREAKAPP_NO_AUTOSTART=1` (env) | Don't touch the Windows startup list. |
| `--demo-notification` | Show a sample warning card, then exit. |
| `--demo-break-notification` | Show a sample notification break with countdown + pulse. |
| `--demo-break window\|fullscreen\|locked\|total [seconds]` | Show a sample break (locked/total really lock input!). |
| `--check-settings` | Headless check that every setting has a control (`%AppData%\BreakApp\coverage.txt`). |

Tray → **Diagnostics…** (Advanced mode) shows live idle time, gamepad/video/meeting/DND detection and every rule's timer.

## Project layout

```
src/BreakApp.Core     all logic: models, settings, scheduler, pausing, commitment, history (no UI, fully unit-tested)
src/BreakApp          WPF shell: tray, notifications, break windows, input lock, OS signals, settings window
tests/                xUnit tests for Core
docs/superpowers/     design spec and implementation plan
```
