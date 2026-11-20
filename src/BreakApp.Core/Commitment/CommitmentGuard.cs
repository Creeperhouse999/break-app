using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;

namespace BreakApp.Core.Commitment;

public enum GuardedAction { SkipNext, Pause, DisableRule, Quit }

/// <summary>
/// Optional "commitment mode" (spec §7): blocks dodging strict rules shortly before they fire,
/// and delays settings changes that weaken them by a cooldown.
/// </summary>
public sealed class CommitmentGuard(IClock clock)
{
    private AppSettings? _pending;
    public DateTime? PendingAppliesAt { get; private set; }

    public static bool IsStrict(BreakRule r) => !r.AllowSkip || r.Lock == LockLevel.Total;

    public static bool IsActive(AppSettings s, BreakRule r) =>
        (r.CommitmentOverride ?? s.Commitment.Enabled) && IsStrict(r);

    /// <summary>Null when the action is allowed; otherwise a short reason to show the user.</summary>
    public string? BlockReason(AppSettings s, Scheduler sch, GuardedAction action)
    {
        foreach (var r in s.Rules.Where(r => r.Enabled && IsActive(s, r)))
        {
            var left = sch.TimeUntil(r.Id);
            if (left <= s.Commitment.Window)
                return $"Commitment mode: '{r.Name}' starts in {Format.Duration(left < TimeSpan.Zero ? TimeSpan.Zero : left)}";
        }
        return null;
    }

    /// <summary>Lists the changes from <paramref name="before"/> to <paramref name="after"/> that weaken a committed rule.</summary>
    public IReadOnlyList<string> WeakeningChanges(AppSettings before, AppSettings after)
    {
        var lines = new List<string>();
        foreach (var b in before.Rules.Where(r => r.Enabled && IsActive(before, r)))
        {
            var a = after.Rules.FirstOrDefault(r => r.Id == b.Id);
            if (a is null) { lines.Add($"'{b.Name}' deleted"); continue; }
            if (!a.Enabled) lines.Add($"'{b.Name}' disabled");
            if (a.Lock < b.Lock) lines.Add($"'{b.Name}' lock lowered to {a.Lock}");
            if (a.AllowSkip && !b.AllowSkip) lines.Add($"'{b.Name}' skip allowed");
            if (a.Interval > b.Interval) lines.Add($"'{b.Name}' interval raised to {Format.Duration(a.Interval)}");
            if (a.Duration < b.Duration) lines.Add($"'{b.Name}' duration lowered to {Format.Duration(a.Duration)}");
            if (a.AllowPostpone && !b.AllowPostpone) lines.Add($"'{b.Name}' postpone allowed");
            if (!IsActive(after, a) && a.Enabled && lines.Count == 0) lines.Add($"'{b.Name}' commitment turned off");
        }
        if (before.Commitment.Enabled && !after.Commitment.Enabled) lines.Add("Commitment mode turned off");
        return lines;
    }

    /// <summary>Queue settings to apply once the cooldown (from <paramref name="pending"/>'s commitment settings) has passed.</summary>
    public void Schedule(AppSettings pending)
    {
        _pending = pending;
        PendingAppliesAt = clock.Now + pending.Commitment.Cooldown;
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
