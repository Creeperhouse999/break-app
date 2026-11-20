using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;

namespace BreakApp.Core.Commitment;

public enum GuardedAction { SkipNext, Pause, DisableRule, Quit }

/// <summary>
/// Optional "commitment mode" (spec §7): blocks dodging strict rules shortly before they fire,
/// and delays settings changes that weaken them by the cooldown that was in force before the change.
/// </summary>
public sealed class CommitmentGuard(IClock clock)
{
    private AppSettings? _pending;
    public DateTime? PendingAppliesAt { get; private set; }

    public static bool IsStrict(BreakRule r) => !r.AllowSkip || r.Lock == LockLevel.Total;

    public static bool IsActive(AppSettings s, BreakRule r) =>
        (r.CommitmentOverride ?? s.Commitment.Enabled) && IsStrict(r);

    private static IEnumerable<BreakRule> Committed(AppSettings s) => s.Rules.Where(r => r.Enabled && IsActive(s, r));

    /// <summary>Null when the action is allowed; otherwise a short reason to show the user.</summary>
    public string? BlockReason(AppSettings s, Scheduler sch, GuardedAction action, Guid? ruleId = null)
    {
        foreach (var r in Committed(s).Where(r => ruleId is null || r.Id == ruleId))
        {
            var left = sch.TimeUntil(r.Id);
            if (left <= s.Commitment.Window)
                return $"Commitment mode: '{r.Name}' starts in {Format.Duration(left < TimeSpan.Zero ? TimeSpan.Zero : left)}";
        }
        return null;
    }

    private static bool SameWindows(List<TimeWindow> a, List<TimeWindow> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Start == p.Second.Start && p.First.End == p.Second.End
                                                && p.First.Days.OrderBy(d => d).SequenceEqual(p.Second.Days.OrderBy(d => d)));

    /// <summary>Lists the changes from <paramref name="before"/> to <paramref name="after"/> that make a committed rule easier to dodge.</summary>
    public IReadOnlyList<string> WeakeningChanges(AppSettings before, AppSettings after)
    {
        var lines = new List<string>();
        var anyCommitted = false;
        foreach (var b in Committed(before))
        {
            anyCommitted = true;
            var a = after.Rules.FirstOrDefault(r => r.Id == b.Id);
            if (a is null) { lines.Add($"'{b.Name}' deleted"); continue; }
            var rule = new List<string>();
            if (!a.Enabled) rule.Add($"'{b.Name}' disabled");
            if (a.Lock < b.Lock) rule.Add($"'{b.Name}' lock lowered to {a.Lock}");
            if (a.AllowSkip && !b.AllowSkip) rule.Add($"'{b.Name}' skip allowed");
            if (a.Interval > b.Interval) rule.Add($"'{b.Name}' interval raised to {Format.Duration(a.Interval)}");
            if (a.Duration < b.Duration) rule.Add($"'{b.Name}' duration lowered to {Format.Duration(a.Duration)}");
            if (a.Style < b.Style) rule.Add($"'{b.Name}' style changed to {a.Style}");
            if (a.AllowPostpone && !b.AllowPostpone) rule.Add($"'{b.Name}' postpone allowed");
            if (a.MaxPostpones > b.MaxPostpones) rule.Add($"'{b.Name}' postpone limit raised to {a.MaxPostpones}");
            if (a.PostponeLength > b.PostponeLength) rule.Add($"'{b.Name}' postpone length raised to {Format.Duration(a.PostponeLength)}");
            if (!SameWindows(a.ActiveWindows, b.ActiveWindows)) rule.Add($"'{b.Name}' active hours changed");
            if (rule.Count == 0 && a.Enabled && !IsActive(after, a)) rule.Add($"'{b.Name}' commitment turned off");
            lines.AddRange(rule);
        }
        if (before.Commitment.Enabled || anyCommitted)
        {
            if (before.Commitment.Enabled && !after.Commitment.Enabled) lines.Add("Commitment mode turned off");
            if (after.Commitment.Cooldown < before.Commitment.Cooldown) lines.Add($"Commitment cooldown lowered to {Format.Duration(after.Commitment.Cooldown)}");
            if (after.Commitment.Window < before.Commitment.Window) lines.Add($"Commitment window lowered to {Format.Duration(after.Commitment.Window)}");
        }
        return lines;
    }

    /// <summary>
    /// The part of <paramref name="after"/> that may apply right away: everything except committed rules and the
    /// commitment settings, which keep their <paramref name="before"/> values until the cooldown has passed.
    /// </summary>
    public static AppSettings ApplyNowPart(AppSettings before, AppSettings after)
    {
        var now = SettingsStore.Clone(after);
        var committed = Committed(before).ToList();
        for (var i = 0; i < before.Rules.Count; i++)
        {
            var b = before.Rules[i];
            if (!committed.Contains(b)) continue;
            var keep = SettingsStore.CloneRule(b);
            var idx = now.Rules.FindIndex(r => r.Id == b.Id);
            if (idx >= 0) now.Rules[idx] = keep;
            else now.Rules.Insert(Math.Min(i, now.Rules.Count), keep);
        }
        now.Commitment = SettingsStore.Clone(before).Commitment;
        return SettingsValidator.Normalize(now);
    }

    /// <summary>Queue settings to apply after <paramref name="cooldown"/> (the cooldown in force before the change).</summary>
    public void Schedule(AppSettings pending, TimeSpan cooldown)
    {
        _pending = pending;
        PendingAppliesAt = clock.Now + cooldown;
    }

    public void CancelPending()
    {
        _pending = null;
        PendingAppliesAt = null;
    }

    /// <summary>Returns the queued settings once due (and clears them), otherwise null.</summary>
    public AppSettings? DuePending()
    {
        if (_pending is null || PendingAppliesAt is not DateTime at || clock.Now < at) return null;
        var p = _pending;
        CancelPending();
        return p;
    }
}
