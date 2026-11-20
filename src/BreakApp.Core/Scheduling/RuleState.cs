namespace BreakApp.Core.Scheduling;

internal enum RulePhase { Counting, Pending, Running }

/// <summary>Mutable per-rule timer state owned by the Scheduler.</summary>
internal sealed class RuleState
{
    /// <summary>Counted time this cycle (ActiveTime / ClockTime).</summary>
    public TimeSpan Elapsed;
    /// <summary>Next fire time for ClockAligned rules (also used after a postpone).</summary>
    public DateTime NextAligned;
    public bool Warned;
    public int PostponeCount;
    public bool SkipNextFlag;
    public bool TakeNowFlag;
    public RulePhase Phase = RulePhase.Counting;
    /// <summary>Already satisfied by the current idle stretch; cleared when the user is active again.</summary>
    public bool IdleSatisfied;
    /// <summary>Rules merged into this one when it is the primary of a bigger-wins break.</summary>
    public List<Guid> MergedIds = [];
}
