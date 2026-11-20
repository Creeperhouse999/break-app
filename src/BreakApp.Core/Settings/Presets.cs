using BreakApp.Core.Models;

namespace BreakApp.Core.Settings;

/// <summary>A one-click starting point. Apply always returns a modified clone and never touches appearance, sound, general or pause settings.</summary>
public sealed record Preset(string Name, string Description, Func<AppSettings, AppSettings> Apply);

public static class Presets
{
    public static IReadOnlyList<Preset> All { get; } =
    [
        new("Gentle", "Soft reminders only: every rule becomes a skippable notification.", s => Map(s, x =>
        {
            foreach (var r in x.Rules) { r.Style = BreakStyle.Notification; r.Lock = LockLevel.Skippable; r.AllowSkip = true; }
        })),
        new("Balanced", "The defaults: a 20 s eye rest every 20 min and a 5 min stretch every hour.", s => Map(s, x =>
            x.Rules = ReuseIds(x.Rules, AppSettings.CreateDefault().Rules))),
        new("Eye care", "20-20-20 rule: every 20 min look 6 m away for 20 s (flashing notification), plus an hourly stretch window.", s => Map(s, x =>
        {
            var eye = RuleTemplates.EyeRest();
            eye.Flash.Enabled = true;
            var stretch = RuleTemplates.Stretch();
            stretch.Style = BreakStyle.Window;
            stretch.Order = 1;
            x.Rules = ReuseIds(x.Rules, [eye, stretch]);
        })),
        new("Strict", "Discipline: window and fullscreen breaks lock input (escape key still works), no skipping, commitment mode on.", s => Map(s, x =>
        {
            foreach (var r in x.Rules.Where(r => r.Style != BreakStyle.Notification))
            {
                if (r.Lock == LockLevel.Skippable) r.Lock = LockLevel.LockedWithKeybind;
                r.AllowSkip = false;
            }
            x.Commitment.Enabled = true;
        })),
    ];

    private static AppSettings Map(AppSettings s, Action<AppSettings> change)
    {
        var c = SettingsStore.Clone(s);
        change(c);
        return SettingsValidator.Normalize(c);
    }

    /// <summary>Keeps the Id of an existing rule with the same name so history stays linked.</summary>
    private static List<BreakRule> ReuseIds(List<BreakRule> existing, List<BreakRule> fresh)
    {
        foreach (var f in fresh)
        {
            var match = existing.FirstOrDefault(e => string.Equals(e.Name, f.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) f.Id = match.Id;
        }
        return fresh;
    }

    /// <summary>Human-readable list of what changes between two settings (used to confirm a preset).</summary>
    public static IReadOnlyList<string> Diff(AppSettings before, AppSettings after)
    {
        var lines = new List<string>();
        static string OnOff(bool b) => b ? "on" : "off";
        static string Dur(TimeSpan t) => Format.Duration(t);

        foreach (var a in after.Rules)
        {
            var b = before.Rules.FirstOrDefault(r => r.Id == a.Id);
            if (b is null) { lines.Add($"{a.Name}: added (every {Dur(a.Interval)}, {Dur(a.Duration)}, {a.Style})"); continue; }
            void Cmp<T>(string what, T x, T y, Func<T, string>? fmt = null)
            {
                if (!EqualityComparer<T>.Default.Equals(x, y))
                    lines.Add($"{a.Name}: {what} {(fmt is null ? x?.ToString() : fmt(x))} → {(fmt is null ? y?.ToString() : fmt(y))}");
            }
            Cmp("enabled", b.Enabled, a.Enabled, OnOff);
            Cmp("interval", b.Interval, a.Interval, Dur);
            Cmp("duration", b.Duration, a.Duration, Dur);
            Cmp("timer", b.TimerMode, a.TimerMode);
            Cmp("style", b.Style, a.Style);
            Cmp("lock", b.Lock, a.Lock);
            Cmp("skip allowed", b.AllowSkip, a.AllowSkip, OnOff);
            Cmp("postpone allowed", b.AllowPostpone, a.AllowPostpone, OnOff);
            Cmp("flash", b.Flash.Enabled, a.Flash.Enabled, OnOff);
            Cmp("warning", b.Warning.ShowNotification, a.Warning.ShowNotification, OnOff);
        }
        foreach (var b in before.Rules.Where(b => after.Rules.All(a => a.Id != b.Id)))
            lines.Add($"{b.Name}: removed");

        if (before.Commitment.Enabled != after.Commitment.Enabled)
            lines.Add($"Commitment mode: {OnOff(before.Commitment.Enabled)} → {OnOff(after.Commitment.Enabled)}");
        if (before.Collision != after.Collision)
            lines.Add($"Overlapping breaks: {before.Collision} → {after.Collision}");
        return lines;
    }
}

public static class RuleTemplates
{
    public static BreakRule EyeRest() => new()
    {
        Name = "Eye rest", Interval = TimeSpan.FromMinutes(20), Duration = TimeSpan.FromSeconds(20),
        Style = BreakStyle.Notification, Warning = new WarningSettings { ShowNotification = false, PlaySound = false },
        Messages = ["Look at something 6 metres away for 20 seconds.", "Blink slowly a few times and relax your eyes."],
    };

    public static BreakRule Stretch() => new()
    {
        Name = "Stretch", Interval = TimeSpan.FromMinutes(60), Duration = TimeSpan.FromMinutes(5),
        Style = BreakStyle.Fullscreen, Warning = new WarningSettings { LeadTime = TimeSpan.FromSeconds(30) },
        Messages = ["Stand up and stretch your arms over your head.", "Roll your shoulders and neck gently."],
    };

    public static BreakRule Walk() => new()
    {
        Name = "Walk", Interval = TimeSpan.FromHours(2), Duration = TimeSpan.FromMinutes(15),
        Style = BreakStyle.Fullscreen, Lock = LockLevel.LockedWithKeybind,
        Warning = new WarningSettings { LeadTime = TimeSpan.FromMinutes(1) },
        Messages = ["Go for a short walk.", "Get some fresh air."],
    };

    public static BreakRule Hydrate() => new()
    {
        Name = "Hydrate", Interval = TimeSpan.FromMinutes(45), Duration = TimeSpan.FromSeconds(10),
        Style = BreakStyle.Notification, Warning = new WarningSettings { ShowNotification = false, PlaySound = false },
        Messages = ["Drink a glass of water."],
    };

    public static BreakRule Custom() => new()
    {
        Name = "Custom break", Interval = TimeSpan.FromMinutes(30), Duration = TimeSpan.FromMinutes(1),
        Style = BreakStyle.Notification,
    };

    public static IReadOnlyList<(string Name, Func<BreakRule> Create)> All { get; } =
    [
        ("Eye rest", EyeRest), ("Stretch", Stretch), ("Walk", Walk), ("Hydrate", Hydrate), ("Custom", Custom),
    ];
}

public static class Format
{
    /// <summary>"20 s", "5 min", "1 h 30 min".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{(int)Math.Round(t.TotalSeconds)} s";
        if (t.TotalMinutes < 60)
            return t.Seconds == 0 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalMinutes} min {t.Seconds} s";
        return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
    }

    /// <summary>Countdown text: "4:59", "1:02:03".</summary>
    public static string Countdown(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var secs = (int)Math.Ceiling(t.TotalSeconds);
        return secs >= 3600 ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}" : $"{secs / 60}:{secs % 60:00}";
    }
}
