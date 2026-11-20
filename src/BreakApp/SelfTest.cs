using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BreakApp.Breaks;
using BreakApp.Core.History;
using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;
using BreakApp.Notifications;
using BreakApp.Services;
using BreakApp.SettingsUI;
using Screen = System.Windows.Forms.Screen;

namespace BreakApp;

/// <summary>Manually driven clock for the self-test.</summary>
internal sealed class ManualClock(DateTime start) : IClock
{
    public DateTime Now { get; set; } = start;
}

/// <summary>
/// `--self-test &lt;report&gt;`: end-to-end scenarios against the real shell (coordinator, presenter, notification cards,
/// break windows, settings window, setup guide) with a scripted clock and scripted OS signals.
/// Meant to run on a hidden desktop (scripts\run-ui-tests.ps1) so nothing appears on the user's screen.
/// Never engages the input lock. Uses a temp folder for settings and history.
/// </summary>
internal static class SelfTest
{
    private sealed class TestFailure(string message) : Exception(message);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new TestFailure(message);
    }

    public static async Task<int> Run(string reportPath)
    {
        var results = new List<(string Name, bool Ok, string Detail)>();
        var tmp = Path.Combine(Path.GetTempPath(), "BreakAppSelfTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var settingsPath = Path.Combine(tmp, "settings.json");
        var historyPath = Path.Combine(tmp, "history.jsonl");

        var clock = new ManualClock(new DateTime(2026, 11, 16, 10, 0, 0)); // a Monday
        var start = AppSettings.CreateDefault();
        start.General.SetupDone = true;
        start.General.StartWithWindows = false;
        Theme.ThemeManager.Apply(start.Appearance);
        var c = new BreakCoordinator(start, settingsPath, clock, 1, historyPath);

        var idle = TimeSpan.Zero;
        var signals = new PauseSignals(false, null, false, false, null, false);
        c.ActivityOverride = _ => new ActivitySnapshot(idle);
        c.PauseOverride = (_, _) => signals;

        HistoryEntry? Last() => c.History.ReadAll().LastOrDefault();
        IEnumerable<BreakWindow> BreakWindows() => Application.Current.Windows.OfType<BreakWindow>().Where(w => w.IsVisible);
        IEnumerable<NotificationWindow> Cards() => Application.Current.Windows.OfType<NotificationWindow>().Where(w => w.IsVisible);

        async Task Advance(TimeSpan span)
        {
            for (var i = 0; i < (int)span.TotalSeconds; i++)
            {
                clock.Now = clock.Now.AddSeconds(1);
                c.TickNow();
                if (i % 30 == 0) await Task.Delay(1);
            }
            await Task.Delay(450); // let the presenter (200 ms) and cards (50 ms) catch up
        }

        void Use(params BreakRule[] rules)
        {
            var s = SettingsStore.Clone(c.Settings);
            s.Rules = [.. rules];
            var msg = c.SaveSettings(s);
            Check(msg is null, "settings were not applied immediately: " + msg);
        }

        static BreakRule R(string name, int every, int length, BreakStyle style, int lead = 0) => new()
        {
            Name = name, Interval = TimeSpan.FromSeconds(every), Duration = TimeSpan.FromSeconds(length), Style = style,
            TimerMode = TimerMode.ClockTime,
            Warning = new WarningSettings { ShowNotification = lead > 0, PlaySound = false, LeadTime = TimeSpan.FromSeconds(lead) },
            StartSound = "", EndSound = "",
        };

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        async Task Cleanup()
        {
            if (c.Presenter.IsRunning) c.Presenter.End(BreakOutcome.Skipped);
            foreach (var card in Cards().ToList()) card.Dismiss("hidden");
            signals = new PauseSignals(false, null, false, false, null, false);
            idle = TimeSpan.Zero;
            await Task.Delay(400);
        }

        async Task Step(string name, Func<Task> body)
        {
            try
            {
                await body();
                results.Add((name, true, ""));
            }
            catch (Exception e)
            {
                results.Add((name, false, e is TestFailure ? e.Message : e.ToString()));
            }
            finally
            {
                try { await Cleanup(); } catch (Exception e) { results.Add((name + " (cleanup)", false, e.Message)); }
            }
        }

        // ------------------------------------------------------------------ scenarios

        await Step("Warning card appears before a break and Postpone moves it", async () =>
        {
            var r = R("Warn", 60, 10, BreakStyle.Notification, lead: 10);
            r.PostponeLength = TimeSpan.FromMinutes(2);
            Use(r);
            await Advance(TimeSpan.FromSeconds(51));
            var card = Cards().FirstOrDefault(w => w.Id == "warn-" + r.Id);
            Check(card is not null, "no warning card");
            var postpone = card!.Actions.Children.OfType<Button>().FirstOrDefault(b => (b.Content as string)?.StartsWith("Postpone") == true);
            Check(postpone is not null, "no Postpone button on the warning");
            Click(postpone!);
            await Task.Delay(100);
            var left = c.Scheduler.TimeUntil(r.Id);
            Check(left > TimeSpan.FromSeconds(100), $"postpone didn't move the break (next in {left})");
        });

        await Step("Notification break counts down and is logged as Taken", async () =>
        {
            var r = R("Note", 30, 10, BreakStyle.Notification);
            Use(r);
            await Advance(TimeSpan.FromSeconds(30));
            Check(Cards().Any(w => w.Id == "break-" + r.Id), "no break card");
            await Advance(TimeSpan.FromSeconds(11));
            Check(!c.Presenter.IsRunning, "break still running after its length");
            Check(Last() is { Outcome: BreakOutcome.Taken } e && e.RuleId == r.Id, "history: " + Last());
            await Task.Delay(400);
            Check(!Cards().Any(w => w.Id == "break-" + r.Id), "break card still visible");
        });

        await Step("Window break: Skip button ends it and logs Skipped", async () =>
        {
            var r = R("Win", 30, 60, BreakStyle.Window);
            Use(r);
            await Advance(TimeSpan.FromSeconds(30));
            var windows = BreakWindows().ToList();
            Check(windows.Count == 1, $"expected 1 break window, got {windows.Count}");
            Check(windows[0].SkipButton.IsVisible, "skip button hidden");
            Click(windows[0].SkipButton);
            await Task.Delay(300);
            Check(!c.Presenter.IsRunning && !BreakWindows().Any(), "break didn't end");
            Check(Last() is { Outcome: BreakOutcome.Skipped }, "history: " + Last());
        });

        await Step("Fullscreen break covers every monitor; Postpone delays it", async () =>
        {
            var r = R("Full", 30, 60, BreakStyle.Fullscreen);
            r.PostponeLength = TimeSpan.FromSeconds(90);
            Use(r);
            await Advance(TimeSpan.FromSeconds(30));
            var windows = BreakWindows().ToList();
            Check(windows.Count == Screen.AllScreens.Length, $"{windows.Count} windows for {Screen.AllScreens.Length} monitors");
            Check(windows[0].PostponeButton.IsVisible, "postpone button hidden");
            Click(windows[0].PostponeButton);
            await Task.Delay(300);
            Check(Last() is { Outcome: BreakOutcome.Postponed }, "history: " + Last());
            var left = c.Scheduler.TimeUntil(r.Id);
            Check(Math.Abs((left - TimeSpan.FromSeconds(90)).TotalSeconds) <= 1, $"next break in {left}, expected 90 s");
        });

        await Step("Break card jumps ahead of another notification", async () =>
        {
            var r = R("Jump", 30, 20, BreakStyle.Notification);
            Use(r);
            c.Notifications.Show(new NotificationRequest("blocker", NotificationKind.Info, "Blocker", "", [], TimeSpan.FromSeconds(60)));
            await Task.Delay(300);
            await Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(500);
            Check(Cards().Any(w => w.Id == "break-" + r.Id), "break card is still waiting behind another card");
        });

        await Step("A meeting starting during a break ends it (not TOTAL LOCK)", async () =>
        {
            var r = R("Meet", 30, 120, BreakStyle.Fullscreen);
            Use(r);
            await Advance(TimeSpan.FromSeconds(30));
            Check(c.Presenter.IsRunning, "break didn't start");
            signals = signals with { MeetingActive = true, MeetingSource = "test" };
            await Advance(TimeSpan.FromSeconds(1));
            Check(!c.Presenter.IsRunning, "break kept running during the meeting");
            Check(Last() is { Outcome: BreakOutcome.InterruptedByMeeting }, "history: " + Last());
        });

        await Step("'Take a break now' during a pause keeps running", async () =>
        {
            var r = R("Manual", 600, 60, BreakStyle.Window);
            Use(r);
            signals = signals with { ManualPauseIndefinite = true };
            await Advance(TimeSpan.FromSeconds(1));
            c.TakeNow(r.Id);
            await Advance(TimeSpan.FromSeconds(5));
            Check(c.Presenter.IsRunning, "manual break was ended by the pause it was started in");
        });

        await Step("Own fullscreen break never pauses itself (fullscreen / Do Not Disturb options on)", async () =>
        {
            var r = R("Self", 30, 120, BreakStyle.Fullscreen);
            var s = SettingsStore.Clone(c.Settings);
            s.Rules = [r];
            s.Pause.PauseDuringFullscreen = true;
            s.Pause.RespectDoNotDisturb = true;
            c.SaveSettings(s);
            await Advance(TimeSpan.FromSeconds(30));
            Check(c.Presenter.IsRunning, "break didn't start");
            signals = signals with { FullscreenApp = true, DoNotDisturb = true };
            await Advance(TimeSpan.FromSeconds(3));
            Check(c.Presenter.IsRunning, "break ended itself because of its own fullscreen window");
            var s2 = SettingsStore.Clone(c.Settings);
            s2.Pause.PauseDuringFullscreen = false;
            s2.Pause.RespectDoNotDisturb = false;
            c.SaveSettings(s2);
        });

        await Step("Deleting the rule of a running break doesn't stop future breaks", async () =>
        {
            var x = R("Doomed", 30, 20, BreakStyle.Window);
            var y = R("Survivor", 40, 5, BreakStyle.Notification);
            Use(x, y);
            await Advance(TimeSpan.FromSeconds(30));
            Check(c.Presenter.Current?.Id == x.Id, "first break didn't start");
            Use(y);
            await Advance(TimeSpan.FromSeconds(25));  // Doomed's window finishes
            await Advance(TimeSpan.FromSeconds(45));  // Survivor must fire
            var fired = c.History.ReadAll().Any(e => e.RuleId == y.Id) || c.Presenter.Current?.Id == y.Id;
            Check(fired, "the remaining rule never fired after deleting the running one");
        });

        await Step("Time away counts as a natural break (no window)", async () =>
        {
            var r = R("Away", 600, 10, BreakStyle.Fullscreen);
            r.TimerMode = TimerMode.ActiveTime;
            Use(r);
            idle = TimeSpan.FromMinutes(4);
            await Advance(TimeSpan.FromSeconds(2));
            Check(Last() is { Outcome: BreakOutcome.SatisfiedByIdle } e && e.RuleId == r.Id, "history: " + Last());
            Check(!BreakWindows().Any(), "a break window appeared");
        });

        await Step("Preview of a locked break never locks input and can be skipped", async () =>
        {
            var r = R("Locked preview", 600, 5, BreakStyle.Fullscreen);
            r.Lock = LockLevel.LockedWithKeybind;
            c.Presenter.Preview(r, c.Settings);
            await Task.Delay(300);
            Check(InputLock.ActiveCount == 0, "the preview engaged the input lock");
            Check(BreakWindows().Any(w => w.SkipButton.IsVisible), "preview has no skip button");
        });

        await Step("Settings window: every page builds, search works, layout is spaced, Save persists", async () =>
        {
            var w = new SettingsWindow(c, null);
            w.Show();
            await Task.Delay(200);
            var cramped = new List<string>();
            var unthemed = new List<string>();
            foreach (var (name, _) in SettingsPages.Categories)
            {
                w.TestNavigate(name);
                await Task.Delay(30);
                cramped.AddRange(Cramped(name, w.TestPanel));
                unthemed.AddRange(FindAll<Button>(w).Where(b => b.Style is null).Select(b => $"{name}: {b.GetType().Name} '{b.Content}'"));
            }
            Check(unthemed.Count == 0, "buttons without the app theme: " + string.Join("; ", unthemed.Distinct()));
            foreach (var t in new[] { typeof(ContextMenu), typeof(MenuItem), typeof(Separator) })
                Check(Application.Current.TryFindResource(t) is Style, $"no themed style for {t.Name} (would look like plain Windows)");
            var rule = w.TestContext.Edit.Rules.First();
            w.TestNavigate("Rules", rule.Id.ToString());
            await Task.Delay(30);
            cramped.AddRange(Cramped("Rule editor", w.TestPanel));
            Check(cramped.Count == 0, "cramped layout: " + string.Join("; ", cramped));

            w.TestSearch("quiet");
            await Task.Delay(50);
            Check(w.TestResultCount > 0, "search for 'quiet' found nothing");
            w.TestSearch("");

            // autosave: no Save click needed
            w.TestContext.Edit.Appearance.Accent = Accent.Teal;
            w.TestContext.MarkDirty();
            await Task.Delay(1500);
            Check(c.Settings.Appearance.Accent == Accent.Teal, "autosave didn't apply the change");
            Check(File.ReadAllText(settingsPath).Contains("Teal"), "autosave didn't write the file");
            // a pending change is flushed when the window closes
            w.TestContext.Edit.Appearance.Accent = Accent.Violet;
            w.TestContext.MarkDirty();
            w.Close();
            await Task.Delay(100);
            Check(c.Settings.Appearance.Accent == Accent.Violet, "closing didn't save the last change");
        });

        await Step("Setup guide: all steps work, mic/camera is off, Finish saves", async () =>
        {
            var s = SettingsStore.Clone(c.Settings);
            s.General.SetupDone = false;
            c.SaveSettings(s);
            var g = new SetupGuideWindow(c);
            g.Show();
            g.GoToStep(0);
            await Task.Delay(100);
            // Preset cards: text must start at the same left edge (not centred with ragged gaps).
            var cards = FindAll<Button>(g.TestBody).Where(b => b.Content is StackPanel).ToList();
            Check(cards.Count == 4, $"expected 4 preset cards, found {cards.Count}");
            var lefts = cards.Select(b => ((FrameworkElement)b.Content).TranslatePoint(new Point(0, 0), b).X).ToList();
            Check(lefts.Max() - lefts.Min() < 1 && lefts.Max() < 30, "preset card text isn't left-aligned: offsets " + string.Join(", ", lefts.Select(x => x.ToString("0"))));
            for (var i = 0; i < 5; i++) { g.GoToStep(i); await Task.Delay(30); }
            Check(!g.TestContext.Edit.Pause.DetectMicCamera, "mic/camera detection is on by default");
            g.TestFinish();
            await Task.Delay(200);
            Check(c.Settings.General.SetupDone, "SetupDone not saved");
        });

        await Step("Commitment mode: harmless changes apply now, weakening waits for the cooldown", async () =>
        {
            var strict = R("Strict", 86400, 60, BreakStyle.Fullscreen);
            strict.AllowSkip = false;
            var s = SettingsStore.Clone(c.Settings);
            s.Rules = [strict];
            s.Commitment.Enabled = true;
            s.Commitment.Cooldown = TimeSpan.FromMinutes(10);
            c.SaveSettings(s);

            var edit = SettingsStore.Clone(c.Settings);
            edit.Appearance.Accent = Accent.Rose;
            edit.Rules[0].AllowSkip = true;
            var msg = c.SaveSettings(edit);
            Check(msg is not null, "no commitment message");
            Check(c.Settings.Appearance.Accent == Accent.Rose, "harmless change didn't apply now");
            Check(!c.Settings.Rules[0].AllowSkip, "weakening applied immediately");
            await Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2));
            Check(c.Settings.Rules[0].AllowSkip, "weakening never applied after the cooldown");
        });

        await Step("App picker: pick from a list of open apps, chips add and remove", async () =>
        {
            var list = new List<string>();
            var changed = 0;
            var editor = new AppListEditor(() => list, v => { list = v; changed++; });
            var apps = new[]
            {
                new RunningApp("obs64", "OBS 30.1 - Profile: Untitled", null),
                new RunningApp("chrome", "YouTube - Google Chrome", null),
                new RunningApp("Discord", "Discord", null),
            };
            var picker = new AppPickerWindow(apps, list);
            picker.Show();
            await Task.Delay(100);
            Check(picker.TestVisibleCount == 3, $"picker shows {picker.TestVisibleCount} apps, expected 3");
            picker.TestFilter("disc");
            await Task.Delay(50);
            Check(picker.TestVisibleCount == 1, "search didn't filter the list");
            picker.TestFilter("");
            picker.TestSelect("obs64");
            Check(picker.Chosen.SequenceEqual(["obs64"]), "picked app not returned: " + string.Join(",", picker.Chosen));
            picker.Close();

            editor.Add(["obs64", "OBS64", "chrome"]); // duplicates ignored, case-insensitive
            Check(list.SequenceEqual(["obs64", "chrome"]), "list after add: " + string.Join(",", list));
            Check(editor.TestChipCount == 2, $"{editor.TestChipCount} chips shown");
            editor.Remove("obs64");
            Check(list.SequenceEqual(["chrome"]) && editor.TestChipCount == 1, "remove didn't work");
            Check(changed == 2, $"change notified {changed}×");

            // the real scan must not crash and never lists Break App itself
            var running = RunningApps.Scan();
            Check(running.All(a => !a.ProcessName.Equals("BreakApp", StringComparison.OrdinalIgnoreCase)), "own process listed");
        });

        await Step("No unhandled errors during the run", () =>
        {
            Check(App.CrashCount == 0, $"{App.CrashCount} unhandled exception(s), see app.log");
            return Task.CompletedTask;
        });

        // ------------------------------------------------------------------ report
        c.Dispose();
        var sb = new StringBuilder();
        foreach (var (name, ok, detail) in results)
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "\n      " + detail.Replace("\n", "\n      "))}");
        var failed = results.Count(r => !r.Ok);
        sb.AppendLine($"\n{results.Count - failed}/{results.Count} passed");
        File.WriteAllText(reportPath, sb.ToString());
        try { Directory.Delete(tmp, true); } catch { }
        return failed;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var x in FindAll<T>(child)) yield return x;
        }
    }

    /// <summary>Loose rows (buttons etc.) directly after a card must have breathing room.</summary>
    private static IEnumerable<string> Cramped(string page, StackPanel? panel)
    {
        if (panel is null) yield break;
        for (var i = 1; i < panel.Children.Count; i++)
        {
            if (panel.Children[i - 1] is Border && panel.Children[i] is FrameworkElement fe and not TextBlock && fe.Margin.Top < 8)
                yield return $"{page}: element {i} ({fe.GetType().Name}) touches the card above";
        }
    }
}
