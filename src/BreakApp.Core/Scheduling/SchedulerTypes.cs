using BreakApp.Core.Models;

namespace BreakApp.Core.Scheduling;

/// <summary>How long the user has been inactive, combined over every enabled activity detector.</summary>
public sealed record ActivitySnapshot(TimeSpan IdleFor);

public sealed record PauseState(bool IsPaused, string? Reason)
{
    public static PauseState None { get; } = new(false, null);
}

public abstract record SchedulerEvent;

/// <summary>A break for <paramref name="Rule"/> starts in <paramref name="TimeLeft"/>.</summary>
public sealed record WarningEvent(BreakRule Rule, TimeSpan TimeLeft) : SchedulerEvent;

/// <summary>Start a break for <paramref name="Rule"/>. <paramref name="Merged"/> are smaller rules it covers (bigger-wins collision).</summary>
/// <paramref name="Manual"/> = started by the user ("take a break now"), so pauses don't hold it back.
public sealed record BreakDueEvent(BreakRule Rule, IReadOnlyList<BreakRule> Merged, bool Manual = false) : SchedulerEvent;

/// <summary>A rule's cycle ended without a break being shown (skipped, satisfied by idle, merged...).</summary>
public sealed record RuleSatisfiedEvent(BreakRule Rule, BreakOutcome Reason) : SchedulerEvent;
