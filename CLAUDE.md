# Break App: working rules

Windows tray break reminder. C# / .NET 10 / WPF. Public repo: github.com/Creeperhouse999/break-app.
Design spec: `docs/superpowers/specs/2026-11-20-break-app-design.md`. UX rules: `docs/UX-GUIDELINES.md` (follow them).

## Layout

- `src/BreakApp.Core/`: all decisions (settings, scheduler, pausing, commitment, history). No UI. Unit-tested.
- `src/BreakApp/`: WPF shell (tray, notifications, break windows, input lock, OS signals, settings, setup guide).
- `tests/BreakApp.Core.Tests/`: xUnit tests, including source guards.
- `scripts/`: `build.ps1` (tests + publish `dist\BreakApp.exe`), `run-ui-tests.ps1` (self-test on a hidden desktop), `make-icon.ps1`.

## Rules

- **SDK:** `%LOCALAPPDATA%\dotnet10\dotnet.exe` (there is no global SDK).
- **Before calling anything done:** `dotnet test`, `scripts\run-ui-tests.ps1` (16 scenarios, nothing shows on screen), `--check-settings`. Look at changed screens with `--ux-tour <dir>`, but only when the owner has OK'd things appearing on their screen.
- **Commit and push after every finished task**, authored as Claude (`git -c user.name="Claude" -c user.email="noreply@anthropic.com" commit`), never as the owner. Issue replies and releases go out under the owner's `gh` login, so sign them as Claude.
- **Update README.md in the same commit** as any user-facing change. Re-render screenshots with `--render-screenshots docs\screenshots`, never by capturing the owner's screen.
- **Keep old versions:** before replacing or removing something, tag the old state `keep/<thing>-<date>` and push the tag. The owner changes their mind often.
- **No AI-sounding text** in the app or the README: short plain sentences, no hype words, no em-dash chains.
- **The owner's own app** is installed at `%LOCALAPPDATA%\Programs\BreakApp`, with settings in `%AppData%\BreakApp`:
  - back up their settings before any live test, and restore them afterwards;
  - propose setting changes and let them choose; apply only what they named;
  - say so BEFORE restarting their app;
  - their lessons are quiet times. During a lesson, nothing may pop up or restart.
- **Never edit files with PowerShell `Get/Set-Content`** (cp1250 / BOM mojibake). Use the Edit tool, or .NET `File.WriteAllText` with UTF-8 (no BOM).
- **Max 2 helper agents** at a time. Check for leftover processes after test runs and stop only what you started, by PID.
- **Pictures for the owner:** send them with SendUserFile, not markdown image links.
- **Log each work session** in `PROGRESS.md`: what changed, what was verified, what's next.
