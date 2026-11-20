using System.Windows.Threading;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Notifications;
using BreakApp.Services;
using BreakApp.Theme;
using Screen = System.Windows.Forms.Screen;

namespace BreakApp.Breaks;

/// <summary>Shows a running break in the rule's style (notification, window, fullscreen) and locks input when asked.</summary>
public sealed class BreakPresenter(BreakCoordinator c)
{
    private readonly List<BreakWindow> _windows = [];
    private DispatcherTimer? _timer;
    private InputLock? _lock;
    private Action<BreakOutcome>? _done;
    private DateTime _end;
    private TimeSpan _total;
    private int _ticks;

    public bool IsRunning => _done is not null;
    public BreakRule? Current { get; private set; }
    public TimeSpan Remaining => IsRunning ? _end - c.Clock.Now : TimeSpan.Zero;

    private static string NotificationId(BreakRule r) => "break-" + r.Id;

    public void Present(BreakRule rule, IReadOnlyList<BreakRule> merged, string message, Action<BreakOutcome> done) =>
        PresentCore(rule, merged, message, done, c.Settings, preview: false);

    /// <summary>Show a break using unsaved settings. Never locks input, always skippable, not recorded.</summary>
    public void Preview(BreakRule rule, AppSettings settings)
    {
        if (IsRunning) return;
        var message = rule.Messages.FirstOrDefault() ?? settings.Texts.DefaultMessage;
        PresentCore(rule, [], message, _ => { }, settings, preview: true);
    }

    private void PresentCore(BreakRule rule, IReadOnlyList<BreakRule> merged, string message, Action<BreakOutcome> done, AppSettings s, bool preview)
    {
        if (IsRunning) Finish(BreakOutcome.Skipped);
        Current = rule;
        _done = done;
        _total = rule.Duration;
        _end = c.Clock.Now + rule.Duration;
        c.Sound.Play(rule.StartSound);

        var t = s.Texts;
        var postponeLabel = !preview && c.Scheduler.CanPostpone(rule.Id)
            ? Templates.Render(t.PostponeButton, new Dictionary<string, string> { ["time"] = Format.Duration(rule.PostponeLength) })
            : null;
        var canSkip = preview || (rule.AllowSkip && rule.Lock == LockLevel.Skippable);
        var mergedText = merged.Count > 0
            ? Templates.Render(t.AlsoCovers, new Dictionary<string, string> { ["rules"] = string.Join(", ", merged.Select(m => m.Name)) })
            : null;

        if (rule.Style == BreakStyle.Notification)
        {
            var actions = new List<NotificationAction>();
            if (canSkip) actions.Add(new("skip", t.SkipButton));
            if (postponeLabel is not null && rule.Lock == LockLevel.Skippable) actions.Add(new("postpone", postponeLabel));
            var body = mergedText is null ? message : $"{message}\n{mergedText}";
            c.Notifications.Show(new NotificationRequest(NotificationId(rule), NotificationKind.Warning, rule.Name, body, actions,
                TimeSpan.Zero, Remaining: () => _end - c.Clock.Now, Total: rule.Duration,
                Flash: rule.Flash.Enabled ? rule.Flash : null, Closable: canSkip),
                id =>
                {
                    switch (id)
                    {
                        case "timeout": Finish(BreakOutcome.Taken); break;
                        case "postpone": Finish(BreakOutcome.Postponed); break;
                        case "skip" or "closed": Finish(BreakOutcome.Skipped); break;
                    }
                }, s.Appearance);
        }
        else
        {
            ShowWindows(rule, mergedText, message, canSkip ? t.SkipBreakButton : null, rule.Lock == LockLevel.Skippable ? postponeLabel : null, s);
            if (!preview && rule.Lock != LockLevel.Skippable)
            {
                var escape = rule.Lock == LockLevel.LockedWithKeybind ? s.Lock.EscapeHotkey : null;
                var realMax = TimeSpan.FromTicks((long)(rule.Duration.Ticks / c.TimeScale)) + TimeSpan.FromSeconds(5);
                _lock = InputLock.Engage(escape, () => Finish(BreakOutcome.EndedByKeybind), realMax);
            }
        }

        _ticks = 0;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    private void ShowWindows(BreakRule rule, string? mergedText, string message, string? skipLabel, string? postponeLabel, AppSettings s)
    {
        var a = s.Appearance;
        var t = s.Texts;
        var theme = ThemeManager.OverrideFor(a, rule.Appearance) ?? ThemeManager.CreateDictionary(ThemeManager.ResolveDark(a.Theme), ThemeManager.PaletteFor(a), a);
        var hint = rule.Lock switch
        {
            LockLevel.LockedWithKeybind => Templates.Render(t.HintLocked, new Dictionary<string, string> { ["hotkey"] = s.Lock.EscapeHotkey }),
            LockLevel.Total => t.HintTotal,
            _ => skipLabel is not null ? t.HintSkip : "",
        };

        if (rule.Style == BreakStyle.Window)
            Hook(new BreakWindow(rule, mergedText, message, false, default, a, theme, skipLabel, postponeLabel, hint));
        else
        {
            // Screens are enumerated at show time so unplugged/added monitors are always handled.
            var screens = a.Monitors switch
            {
                MonitorMode.Primary => Screen.AllScreens.Where(x => x.Primary).ToList(),
                MonitorMode.Specific => Screen.AllScreens.Where(x => a.SpecificMonitors.Contains(x.DeviceName)).ToList(),
                _ => Screen.AllScreens.ToList(),
            };
            if (screens.Count == 0) screens = [Screen.PrimaryScreen ?? Screen.AllScreens[0]];
            foreach (var scr in screens)
                Hook(new BreakWindow(rule, mergedText, message, true, scr.Bounds, a, theme, skipLabel, postponeLabel, hint));
        }
        foreach (var w in _windows) w.Show();
        _windows.FirstOrDefault()?.Activate();
    }

    private void Hook(BreakWindow w)
    {
        w.SkipClicked += () => Finish(BreakOutcome.Skipped);
        w.PostponeClicked += () => Finish(BreakOutcome.Postponed);
        _windows.Add(w);
    }

    private void OnTick()
    {
        if (!IsRunning) return;
        var left = _end - c.Clock.Now;
        foreach (var w in _windows) w.Update(left < TimeSpan.Zero ? TimeSpan.Zero : left, _total);
        if (++_ticks % 5 == 0) foreach (var w in _windows) w.BringToTop();
        if (left <= TimeSpan.Zero) Finish(BreakOutcome.Taken);
    }

    public void End(BreakOutcome outcome) => Finish(outcome);

    private void Finish(BreakOutcome outcome)
    {
        var done = _done;
        if (done is null) return;
        _done = null;
        var rule = Current!;
        Current = null;
        _timer?.Stop();
        _timer = null;
        _lock?.Release();
        _lock = null;
        foreach (var w in _windows) w.Close();
        _windows.Clear();
        c.Notifications.Close(NotificationId(rule));
        if (outcome is BreakOutcome.Taken) c.Sound.Play(rule.EndSound);
        done(outcome);
    }
}
