using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Time;

namespace BreakApp.Core.Tests;

public sealed class FakeClock(DateTime start) : IClock
{
    public DateTime Now { get; set; } = start;
    public void Advance(TimeSpan t) => Now += t;
}

public abstract class SchedulerTestBase
{
    // Friday 2026-11-20 12:00:00
    protected readonly FakeClock Clock = new(new DateTime(2026, 11, 20, 12, 0, 0));
    protected AppSettings Settings = new() { Rules = [] };
    protected Scheduler Sch = null!;

    protected static BreakRule Rule(string name, int intervalSec, int durationSec, TimerMode mode = TimerMode.ActiveTime,
        int leadSec = 0, BreakStyle style = BreakStyle.Notification, LockLevel lockLevel = LockLevel.Skippable) => new()
    {
        Name = name,
        Interval = TimeSpan.FromSeconds(intervalSec),
        Duration = TimeSpan.FromSeconds(durationSec),
        TimerMode = mode,
        Style = style,
        Lock = lockLevel,
        Warning = new WarningSettings { ShowNotification = leadSec > 0, PlaySound = false, LeadTime = TimeSpan.FromSeconds(leadSec) },
    };

    protected void Start(params BreakRule[] rules)
    {
        Settings.Rules = [.. rules];
        Sch = new Scheduler(Settings, Clock);
    }

    /// <summary>Advance one second at a time and tick. idleFrom: IdleFor at the first tick (grows by 1 s per tick), null = active.</summary>
    protected List<SchedulerEvent> Run(int seconds, int? idleFrom = null, PauseState? pause = null)
    {
        var events = new List<SchedulerEvent>();
        for (var i = 0; i < seconds; i++)
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            var idle = idleFrom is int f ? TimeSpan.FromSeconds(f + i) : TimeSpan.Zero;
            events.AddRange(Sch.Tick(new ActivitySnapshot(idle), pause ?? PauseState.None));
        }
        return events;
    }

    protected static IEnumerable<BreakDueEvent> Dues(IEnumerable<SchedulerEvent> e) => e.OfType<BreakDueEvent>();
    protected static IEnumerable<RuleSatisfiedEvent> Sats(IEnumerable<SchedulerEvent> e) => e.OfType<RuleSatisfiedEvent>();
}
