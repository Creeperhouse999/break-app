using BreakApp.Core.Models;

namespace BreakApp.Core.Settings;

/// <summary>Clamps every value into a sane range so a hand-edited or old file can never break the scheduler.</summary>
public static class SettingsValidator
{
    private static TimeSpan Clamp(TimeSpan v, TimeSpan min, TimeSpan max) => v < min ? min : v > max ? max : v;
    private static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Clamp(v, min, max);

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

        s.CollisionWindow = Clamp(s.CollisionWindow, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        s.Idle.Threshold = Clamp(s.Idle.Threshold, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(60));
        s.Pause.MeetingProcesses ??= [];
        s.Pause.MeetingTitlePatterns ??= [];
        s.Pause.QuietTimes ??= [];
        foreach (var q in s.Pause.QuietTimes) { q.Days ??= []; q.Label ??= ""; }
        s.Lock.EscapeHotkey = string.IsNullOrWhiteSpace(s.Lock.EscapeHotkey) ? new LockSettings().EscapeHotkey : s.Lock.EscapeHotkey;
        s.Commitment.Window = Clamp(s.Commitment.Window, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2));
        s.Commitment.Cooldown = Clamp(s.Commitment.Cooldown, TimeSpan.Zero, TimeSpan.FromHours(24));
        s.Appearance.Opacity = Clamp(s.Appearance.Opacity, 0.3, 1);
        s.Appearance.FontScale = Clamp(s.Appearance.FontScale, 0.8, 1.6);
        s.Appearance.SpecificMonitors ??= [];
        s.Sound.Volume = Clamp(s.Sound.Volume, 0, 1);
        s.General.PauseHotkey ??= "";
        s.General.BreakNowHotkey ??= "";
        return s;
    }

    public static void NormalizeRule(BreakRule r)
    {
        r.Name = string.IsNullOrWhiteSpace(r.Name) ? "Break" : r.Name.Trim();
        r.Interval = Clamp(r.Interval, TimeSpan.FromSeconds(10), TimeSpan.FromHours(24));
        r.Duration = Clamp(r.Duration, TimeSpan.FromSeconds(5), TimeSpan.FromHours(4));
        if (r.Duration > r.Interval) r.Duration = r.Interval;
        r.Warning ??= new();
        var maxLead = TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(10).Ticks, r.Interval.Ticks / 2));
        r.Warning.LeadTime = Clamp(r.Warning.LeadTime, TimeSpan.Zero, maxLead);
        r.PostponeLength = Clamp(r.PostponeLength, TimeSpan.FromSeconds(30), TimeSpan.FromHours(2));
        r.MaxPostpones = Math.Clamp(r.MaxPostpones, 0, 20);
        r.Messages ??= [];
        r.Messages.RemoveAll(string.IsNullOrWhiteSpace);
        r.Flash ??= new();
        r.Flash.SpeedHz = Clamp(r.Flash.SpeedHz, 0.5, 10);
        r.Flash.Count = Math.Clamp(r.Flash.Count, 0, 100);
        r.Flash.PulseAmount = Clamp(r.Flash.PulseAmount, 1, 10);
        r.ActiveWindows ??= [];
        foreach (var w in r.ActiveWindows) { w.Days ??= []; w.Label ??= ""; }
        if (r.Appearance?.Opacity is double o) r.Appearance.Opacity = Clamp(o, 0.3, 1);
    }
}
