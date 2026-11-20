using BreakApp.Core.Models;

namespace BreakApp.Core.Settings;

/// <summary>
/// Repairs and clamps every value so a hand-edited, imported or old file can never break the app:
/// null groups/entries removed, out-of-range enums reset, times and numbers clamped, colours and hotkeys validated.
/// </summary>
public static class SettingsValidator
{
    private static TimeSpan Clamp(TimeSpan v, TimeSpan min, TimeSpan max) => v < min ? min : v > max ? max : v;
    private static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Clamp(v, min, max);
    private static T E<T>(T v, T fallback) where T : struct, Enum => Enum.IsDefined(v) ? v : fallback;
    private static T? E<T>(T? v) where T : struct, Enum => v is { } x && Enum.IsDefined(x) ? x : null;

    private static List<string> Strings(List<string>? list) =>
        (list ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

    private static List<TimeWindow> Windows(List<TimeWindow>? list)
    {
        var result = (list ?? []).Where(w => w is not null).ToList();
        foreach (var w in result)
        {
            w.Days = (w.Days ?? []).Where(Enum.IsDefined).Distinct().ToList();
            w.Label ??= "";
        }
        return result;
    }

    public static AppSettings Normalize(AppSettings s)
    {
        s.Rules ??= [];
        s.Rules.RemoveAll(r => r is null);
        s.Idle ??= new();
        s.Pause ??= new();
        s.Lock ??= new();
        s.Commitment ??= new();
        s.Appearance ??= new();
        s.Sound ??= new();
        s.General ??= new();

        var seen = new HashSet<Guid>();
        foreach (var r in s.Rules)
        {
            NormalizeRule(r);
            if (r.Id == Guid.Empty || !seen.Add(r.Id))
            {
                r.Id = Guid.NewGuid();
                seen.Add(r.Id);
            }
        }

        s.Collision = E(s.Collision, CollisionPolicy.BiggerWins);
        s.CollisionWindow = Clamp(s.CollisionWindow, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        s.Idle.Threshold = Clamp(s.Idle.Threshold, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(60));
        s.Idle.FreezeAfter = Clamp(s.Idle.FreezeAfter, TimeSpan.FromSeconds(5), s.Idle.Threshold);

        s.Pause.MeetingProcesses = Strings(s.Pause.MeetingProcesses);
        s.Pause.MeetingTitlePatterns = (s.Pause.MeetingTitlePatterns ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); // keep spaces: "Meet - "
        s.Pause.ForegroundExceptions = Strings(s.Pause.ForegroundExceptions);
        s.Pause.QuietTimes = Windows(s.Pause.QuietTimes);
        s.Pause.TimersDuringPause = E(s.Pause.TimersDuringPause, PauseTimerPolicy.Freeze);

        s.Lock.EscapeHotkey = Hotkey.Normalize(s.Lock.EscapeHotkey) ?? new LockSettings().EscapeHotkey;
        s.Commitment.Window = Clamp(s.Commitment.Window, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2));
        s.Commitment.Cooldown = Clamp(s.Commitment.Cooldown, TimeSpan.Zero, TimeSpan.FromHours(24));

        var a = s.Appearance;
        a.Theme = E(a.Theme, ThemeMode.Auto);
        a.Accent = E(a.Accent, Accent.Blue);
        a.NotificationCorner = E(a.NotificationCorner, Corner.BottomRight);
        a.Countdown = E(a.Countdown, CountdownFormat.Ring);
        a.Monitors = E(a.Monitors, MonitorMode.All);
        a.Opacity = Clamp(a.Opacity, 0.3, 1);
        a.FontScale = Clamp(a.FontScale, 0.8, 1.6);
        a.SpecificMonitors = Strings(a.SpecificMonitors);
        a.CustomAccent = ColorHex.Normalize(a.CustomAccent);
        a.BreakBackgroundColor = ColorHex.Normalize(a.BreakBackgroundColor);
        a.BreakTextColor = ColorHex.Normalize(a.BreakTextColor);
        a.NotificationBackgroundColor = ColorHex.Normalize(a.NotificationBackgroundColor);

        s.Sound.Volume = Clamp(s.Sound.Volume, 0, 1);
        // Global shortcuts may be empty (= off); invalid text is cleared rather than registered wrongly.
        s.General.PauseHotkey = Hotkey.Normalize(s.General.PauseHotkey) ?? "";
        s.General.BreakNowHotkey = Hotkey.Normalize(s.General.BreakNowHotkey) ?? "";
        NormalizeTexts(s.Texts ??= new());
        return s;
    }

    private static void NormalizeTexts(TextSettings t)
    {
        var defaults = new TextSettings();
        foreach (var p in typeof(TextSettings).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite))
        {
            var v = (string?)p.GetValue(t);
            if (v is null || (string.IsNullOrWhiteSpace(v) && !TextSettings.MayBeBlank.Contains(p.Name)))
                p.SetValue(t, p.GetValue(defaults));
        }
    }

    public static void NormalizeRule(BreakRule r)
    {
        r.Name = string.IsNullOrWhiteSpace(r.Name) ? "Break" : r.Name.Trim();
        r.Interval = Clamp(r.Interval, TimeSpan.FromSeconds(10), TimeSpan.FromHours(24));
        r.Duration = Clamp(r.Duration, TimeSpan.FromSeconds(5), TimeSpan.FromHours(4));
        if (r.Duration > r.Interval) r.Duration = r.Interval;
        r.TimerMode = E(r.TimerMode, TimerMode.ActiveTime);
        r.Style = E(r.Style, BreakStyle.Notification);
        r.Lock = E(r.Lock, LockLevel.Skippable);
        r.MessageOrder = E(r.MessageOrder, MessageOrder.Random);
        r.Warning ??= new();
        var maxLead = TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(10).Ticks, r.Interval.Ticks / 2));
        r.Warning.LeadTime = Clamp(r.Warning.LeadTime, TimeSpan.Zero, maxLead);
        r.PostponeLength = Clamp(r.PostponeLength, TimeSpan.FromSeconds(30), TimeSpan.FromHours(2));
        r.MaxPostpones = Math.Clamp(r.MaxPostpones, 0, 20);
        r.Messages = Strings(r.Messages);
        r.Flash ??= new();
        r.Flash.Style = E(r.Flash.Style, FlashStyle.GlowAndPulse);
        r.Flash.SpeedHz = Clamp(r.Flash.SpeedHz, 0.5, 10);
        r.Flash.Count = Math.Clamp(r.Flash.Count, 0, 100);
        r.Flash.PulseAmount = Clamp(r.Flash.PulseAmount, 1, 10);
        r.Flash.Color = ColorHex.Normalize(r.Flash.Color);
        r.ActiveWindows = Windows(r.ActiveWindows);
        if (r.Appearance is { } o)
        {
            o.Accent = E(o.Accent);
            o.Theme = E(o.Theme);
            if (o.Opacity is double op) o.Opacity = Clamp(op, 0.3, 1);
        }
    }
}
