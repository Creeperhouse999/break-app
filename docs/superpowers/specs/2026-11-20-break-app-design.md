# Break App — Design

Date: 2026-11-20
Status: approved 2026-11-20

## 1. Intent

A lightweight Windows tray app that reminds the user to take breaks on schedules they define. Inspired by Stretchly, built because:

- Stretchly is **missing features**: no flash-only breaks, no real input lock (Alt-Tab / Win key still work in strict mode), only two fixed break types, limited customization.
- Stretchly is **too heavy** (Electron, ~150 MB RAM).
- The user **wants to build their own** and control it fully.

The defining trait: **heavy customization**. Almost every behavior is a setting, with sensible defaults so it works out of the box.

## 2. Goals, non-goals, anti-goals

**Goals**
- Unlimited user-defined break rules: "every X, break for Y", each with its own style, warning, lock level, and timer mode.
- Multiple break presentations, from a subtle flashing side notification to a full-screen, input-blocking lock.
- Real input blocking (including Alt-Tab and Win key) when the user opts in.
- Smart pausing: meetings (Google Meet etc.), scheduled quiet hours (e.g. lessons), manual pause.
- Stats/history of breaks.
- Light: target idle RAM well under Stretchly (aim < 60 MB), near-zero CPU when idle.

**Non-goals**
- macOS / Linux.
- Cloud sync, accounts, mobile companion.
- Localization beyond English (UI is English only).

**Anti-goals** (failure even if it "works")
- A user locked out of their PC with no warning about what they enabled. Total lock must be an explicit, confirmed choice.
- Breaks silently not firing (timer drift, lost after sleep, swallowed by pause logic without the user knowing).
- Settings that exist in the file but not in the UI. If it's customizable, it's in the settings window.
- Noticeable CPU/RAM use while idle.

## 3. Constraints

- Windows 10 and 11.
- C# / .NET 10 (LTS) / WPF. Tray-resident app. (.NET 10 instead of 8: Claude's call, it is the current LTS and its runtime is already installed.) Ships as a single .exe (self-contained or framework-dependent: builder's call).
- **Portable, no admin rights.** The app runs as a normal user process. The input lock is therefore **best effort** (user decision). Known escape routes, all accepted:
  - Ctrl+Alt+Del (Windows never lets apps block it) → Task Manager.
  - Elevated (admin) windows already open, and UAC prompts, can't be blocked or reliably covered by a non-admin process.
- Start with Windows uses the per-user Run registry key.
- Games must **not** pause breaks. Exclusive-fullscreen games may cover or fight a topmost window; borderless games should be fine. Must be tested; best effort.

## 4. Approaches considered

| Approach | Trade-off | Verdict |
|---|---|---|
| **C# .NET 10 + WPF** | Native Windows, ~30–50 MB RAM, direct Win32 access for low-level input hooks and toasts, fast to build a rich settings UI. Windows only. | **Picked.** Best fit for "light" + "deep OS control" + "lots of settings UI". |
| Tauri (Rust + web UI) | Slick HTML UI, cross-platform possible, similar RAM. Input lock and Windows integration in Rust = more work. | Rejected: cross-platform isn't wanted, OS work harder. |
| Python + Qt | Easy to hack. Heavier RAM, clunky .exe packaging, antivirus false positives. | Rejected: conflicts with "too heavy". |

## 5. Core model: break rules

The user maintains a list of rules. Each rule:

| Field | Options |
|---|---|
| Name | free text (e.g. "Eye rest") |
| Enabled | on/off |
| Interval | every X (seconds / minutes / hours) |
| Duration | for Y (seconds / minutes) |
| Timer mode | **Active time** (counts only while user is active; idle resets) · **Clock time** (counts regardless) · **Clock-aligned** (fires at fixed clock points, e.g. every :00 / :30) |
| Style | see §6 |
| Lock level | see §7 (only for window/fullscreen styles) |
| Pre-break warning | see §8 |
| Postpone | allowed on/off · postpone length · max postpones in a row |
| Skip | allowed on/off |
| Messages | list of break messages/ideas, shown randomly or in order |
| Sounds | break start / break end sound (or none), volume |
| Appearance overrides | optional per-rule overrides of global appearance (§11) |
| Active days/hours | optional: rule only runs on certain days/time windows |

**Defaults shipped** (builder may tune):
- "Eye rest": every 20 min, 20 s, side notification with flash, active time.
- "Stretch": every 60 min, 5 min, fullscreen countdown, skippable, toast warning 30 s before.

**Collision handling** (global setting):
- **Bigger break wins** (default): when rules come due at the same time (or within a configurable window, default 1 min), the longer/stricter one runs; the smaller one is counted as satisfied.
- Run both, one after another.
- Any break resets all timers.

## 6. Break styles

1. **App notification**: the app's own notification card (§6a), **not** Windows toasts. For a break it shows the message plus a live countdown, and it can **flash** to grab attention.
   - Settings: flash on/off, flash color (default = accent), flash speed, number of flashes or continuous, corner, auto-dismiss vs stays for the break duration.
2. **Window**: centered, non-fullscreen window with countdown and message.
3. **Fullscreen countdown**: covers the screen with time remaining, message, and buttons per lock level.

### 6a. Notification card: same look as HiSync

The user wants the app's own notifications, matching the custom toast from their HiSync project (`Downloads/CURSOR/HiBy R1 Downloader/ui/toast.html` + `hisync/app.py` `Toasts` class). Treat that file as the visual reference. **Port the look natively to WPF XAML**; do not embed WebView2. (Claude's call: an embedded browser per popup would bring back the memory weight the user left Stretchly to escape.)

What to carry over:
- **Window:** ~380 px wide, height fits content (max ~320). Frameless, topmost, never takes focus (`WS_EX_NOACTIVATE`), hidden from taskbar and Alt-Tab (`WS_EX_TOOLWINDOW`), Windows 11 rounded corners (DWM corner preference). Slides in from the chosen side and slides out. One at a time, queued.
- **Card layout:** kind icon in a 34 px tinted rounded square (pops in) · small brand line (gradient square + "Break App") · bold title (one line, ellipsis) · body (up to 4 lines) · up to 3 buttons (primary = accent gradient with glow, others = outlined) · close �s� top-right · 3 px accent stripe on the left edge · 3 px "life" progress bar at the bottom that pauses while hovered.
- **Kinds:** info / success / warning / error with HiSync's colors. The break app mainly uses info (warnings) and warning (break now).
- **Theme:** auto / light / dark (light = white→pale-blue gradient, dark = deep navy gradient, soft blue shadow). Accents: blue (default), indigo, teal, violet, rose. Font: Segoe UI Variable.
- **Sound:** HiSync's two-tone chime (880 Hz → 1318.5 Hz sine, ~0.6 s) as the built-in default, synthesized or shipped as a WAV.
- **Keys/motion:** Esc closes, Enter presses the primary button, reduce-motion setting.
- **Added for Break App:** a live countdown line for break notifications, the flash effect (accent pulse of the card/stripe/glow, toggleable), and per-rule buttons (Postpone, Skip next break, Start break now).

The same accent and theme carry over to the settings window, window break and fullscreen break, so the whole app looks like one family. (Claude's call.)

Monitor setting (global, overridable per rule): all monitors / primary only / specific monitors.

## 7. Lock levels (window and fullscreen styles)

1. **Skippable**: Skip / Postpone buttons (subject to the rule's skip/postpone settings). Normal topmost window.
2. **Locked + escape keybind**: low-level keyboard and mouse hooks block input (best effort, see §3), including Alt-Tab, Win key, Alt-F4, and mouse clicks outside the break UI. A user-configured key combo ends the break early.
3. **TOTAL LOCK**: same blocking, no keybind, no buttons. Ends only when the timer finishes.
   - Selecting it in settings shows a prominent confirmation: "ARE YOU SURE? You will not be able to use your PC until the break ends." Must be confirmed explicitly.
   - Ctrl+Alt+Del can't be blocked (see §3).

Safety: hooks must be released when the break ends, when the app exits, or when it crashes (hooks die with the process, but the overlay must not linger).

**Meeting starts during a running break** (mic or camera turns on, user decision): Skippable and Locked+keybind breaks end immediately, logged as "interrupted by meeting". TOTAL LOCK keeps going. Quiet-time windows and manual pause behave the same way.

### Commitment mode (optional, off by default)

The user can decide whether strict rules (no-skip or TOTAL LOCK) can be dodged *before* they fire. Global setting, overridable per rule:
- **Off** (default): tray Skip next, Pause, disabling a rule and Quit always work.
- **On**: within a configurable window before a strict rule fires (default 10 min), tray Skip next / Pause / Disable rule / Quit are blocked for that rule. Weakening a strict rule in settings (lowering the lock, enabling skip, deleting it) takes effect only after a cooldown (default 10 min), shown in the UI. Turning commitment mode off is itself subject to the cooldown.

## 8. Pre-break warning

Per rule, any combination of:
- **App notification** (§6a card), e.g. "Stretch break in 30 s", with action buttons such as **Postpone** and **Skip next break**. Each button can be enabled/disabled per rule.
- **Sound**: built-in chime or a custom sound file.
- None.

Warning lead time per rule (e.g. 10 s, 30 s, 1 min, 5 min).

**Windows Do Not Disturb:** the app never uses Windows toasts, so Do Not Disturb / Focus Assist doesn't silently swallow warnings. A setting "Respect Windows Do Not Disturb" (off by default) lets the user hold breaks while it's on. Meetings are already covered by §9.

## 9. Pausing

- **Manual pause** from the tray: 15 min / 30 min / 1 h / 2 h / until tomorrow / until resumed / custom.
- **Meeting detection**: pause while the mic or camera is in use (covers Google Meet in a browser, Zoom, Teams, Discord calls). Implementation hint: Windows CapabilityAccessManager ConsentStore registry entries show which apps are currently using mic/webcam. Backup: user-editable list of process names / window title patterns (e.g. "Meet -"). Each detector toggleable.
- **Scheduled quiet times**: weekly schedule entries (days + start/end time, e.g. "Mon/Wed 10:00–11:30 Lesson"), with optional label.
- **Games do NOT pause breaks.** An optional "pause during fullscreen apps" setting exists but is **off** by default.
- **During/after pause** (setting): timers freeze and continue · timers reset when pause ends.
- Tray icon shows paused state and why (manual / meeting / schedule).

## 10. Activity, idle and natural breaks

**What counts as activity** (each detector toggleable, all on by default):
- Keyboard and mouse input.
- Gamepad input (XInput polling), so gaming counts as active.
- Video/media playback (apps asking Windows to keep the display on), so watching a video counts as active.

**Idle** = no activity for the idle threshold (default 3 min). Sleep, screen lock and logoff count as idle.

**Rules per timer mode** (when the "natural breaks" setting is on, which is the default):
- **Active time:** the timer counts only while active and freezes while idle. Once total idle time reaches the rule's break duration, the rule counts as satisfied ("satisfied by idle" in history) and its timer resets to zero.
- **Clock time:** the timer keeps counting while idle. If idle time reaches the break duration, the rule counts as satisfied and resets, the same as active time.
- **Clock-aligned:** fires at fixed clock points. If the user is idle at that point and stays idle for the break duration, it counts as satisfied. Otherwise it fires when they come back, if it's still within the rule's interval.

**Wake from sleep / unlock:** any rule whose break was due during sleep counts as satisfied if the sleep lasted at least the break duration. Otherwise it fires right away (after its warning). A break never fires "twice" for the same due time.

With natural breaks off, idle only freezes active-time timers and never satisfies anything.

## 11. Appearance and sound (global, with per-rule overrides)

- Theme: background color, text color, accent/flash color, opacity, font size; light/dark presets.
- Optional background image for fullscreen breaks.
- Countdown format (mm:ss, progress ring, progress bar).
- Message lists (break ideas) per rule.
- Sounds: warning, break start, break end; built-in set plus custom files; volume; global mute.
- Monitors (see §6).

## 12. Stats and history

- Log every break event: rule, scheduled time, outcome (taken / skipped / postponed / satisfied by idle / suppressed by pause), actual duration.
- Stats tab: breaks taken vs skipped per day/week, per-rule breakdown, streaks.
- Export history to CSV. Clear history.

## 13. App shell

- **Tray icon**: shows next break countdown on hover; menu: next break info, pause submenu, "take break now" (per rule), skip next, settings, stats, quit.
- **Settings window: lots of settings, never a wall of them** (user requirement):
  - **Categories** in a left sidebar: Rules · Warnings · Lock & Commitment · Pausing & Meetings · Activity & Idle · Appearance · Sounds · Stats · General.
  - Within each category, settings are grouped under headings, with the most-used ones first.
  - **Basic / Advanced** toggle: Basic shows the essentials, Advanced shows everything.
  - **Tips:** every setting has a one-line description under it, plus a hover tip or �? with an example of what it does.
  - **Search box** that finds a setting across all categories.
  - **Presets:**
    - Global presets: *Gentle* (flash reminders only), *Balanced* (default), *Eye care* (20-20-20 focus), *Strict* (locked breaks + commitment mode). Applying one shows what will change before applying it.
    - Rule templates when adding a rule: Eye rest, Stretch, Walk, Hydrate, Custom.
  - Dangerous options (TOTAL LOCK, commitment mode) are marked in red with a confirmation step.
- **General**: start with Windows, global hotkeys (pause/resume, take break now), import/export settings (JSON), reset to defaults.
- Storage: `%AppData%\BreakApp\` — settings JSON, history (SQLite or JSON lines: builder's call).
- Single instance.

## 14. Left to the builder

- Internal architecture (scheduler, services, MVVM or not), libraries (tray icon, toast, SQLite).
- Exact default values beyond those listed.
- Test strategy (scheduler/timer logic should be unit-testable with an injectable clock).
- Packaging (self-contained vs framework-dependent, installer or portable).
- Visual design of the settings window and break screens, within the settings above.

## 15. Calls made by Claude (user asked Claude to decide; listed for review)

- Meeting detection via mic/camera in-use state, with process/window-title list as backup.
- Collision window of 1 min.
- Default rules listed in §5.
- "Pause during fullscreen apps" exists as an optional setting, off by default.
- Settings import/export, global hotkeys, single instance, start with Windows.
- Per-rule active days/hours.
- Idle/sleep behavior per timer mode (§10).
- Gamepad and video playback count as activity (§10).
- Notification card ported natively to WPF, not embedded HTML (§6a). HiSync's accent/theme used app-wide.
- "Respect Windows Do Not Disturb" setting, off by default (§8).
- Commitment mode is optional, off by default, 10 min window and cooldown (§7).
- Settings organization: categories, Basic/Advanced, tips, search, presets (§13).

