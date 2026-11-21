# Progress log

## 2026-11-21

- Default breaks are now **Short break** (20 min / 20 s, notification) and **Long break** (60 min / 5 min, fullscreen), with neutral messages. The old "Eye rest" / "Stretch" defaults are kept in tag `keep/default-names-2026-11-21`.
- All popups use the app's own themed dialog; a source-guard test fails if `MessageBox.Show` comes back.
- Icon: one stopwatch icon everywhere, using the sharpest frame (no blurry 16 px upscale).
- Texts tab starts with each break's name and messages.
- Repo: issue templates, area labels, README "report a bug" section, `docs/UX-GUIDELINES.md`, `CLAUDE.md`. History rewritten so every commit except the first is authored by Claude.
- Owner's PC: lesson times added as quiet times; break messages set to "Stand up" / "Look away and make a bit of stretching"; fullscreen opacity 100 %.
- Verified: 161 unit tests and self-test 16/16 (as of this entry).

- Owner OK'd clearing the Windows icon cache: Explorer restarted and the cache was deleted.
- **Auto-updater (v1.1.0):**
  - checks the GitHub latest release 30 s after start and then daily;
  - the card offers Update now / What's new / Later;
  - Update now downloads, checks the file is a real exe and matches the release size, renames the running exe to `.old`, swaps the new one in and restarts with `--updated-from`;
  - only trusts download URLs from this repo's releases;
  - Settings › General has the switch and a "Check now" button, and the tray menu shows "Update to …" when one is available.

- Verified the update end to end: a 1.0.9 copy found v1.1.0, "Update now" swapped the exe (byte-identical to the release) and restarted, then showed "Updated to 1.1.0"; `.old` was removed.
- README: note that the project is maintained by Claude with human review.
- Owner's PC: installed v1.1.0 from the release; tray icon set to always visible (Windows' NotifyIconSettings IsPromoted).

**Next:**
- A broader settings UX rework: the owner finds the settings unintuitive. Follow `docs/UX-GUIDELINES.md`.

## 2026-11-20

First version: scheduler, notification/window/fullscreen breaks, input lock, pausing, idle, commitment mode, stats, settings window, setup guide, self-test, live tests with short rules (found and fixed a merged-lock bug). Public on GitHub with release v1.0.0.
