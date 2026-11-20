using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;

namespace BreakApp.Core.Tests;

public class SchedulerIdleCollisionTests : SchedulerTestBase
{
    private const int Threshold = 180; // default 3 min

    private List<SchedulerEvent> TickOnce(int idleSec = 0) =>
        [.. Sch.Tick(new ActivitySnapshot(TimeSpan.FromSeconds(idleSec)), PauseState.None)];

    [Fact]
    public void Idle_LongerThanDuration_SatisfiesOnce()
    {
        var eye = Rule("Eye rest", 1200, 20);
        Start(eye);
        Run(60);
        var e = Run(Threshold, idleFrom: 1);
        var s = Assert.Single(Sats(e));
        Assert.Equal(BreakOutcome.SatisfiedByIdle, s.Reason);
        Assert.Empty(Run(600, idleFrom: Threshold + 1));
        Assert.Equal(TimeSpan.FromMinutes(20), Sch.TimeUntil(eye.Id));
    }

    [Fact]
    public void Idle_SatisfiesAgainAfterUserReturns()
    {
        var eye = Rule("Eye rest", 1200, 20);
        Start(eye);
        Assert.Single(Sats(Run(Threshold, idleFrom: 1)));
        Run(30); // active again
        Assert.Single(Sats(Run(Threshold, idleFrom: 1)));
    }

    [Fact]
    public void Idle_ShorterThanBreakDuration_DoesNotSatisfyLongRule()
    {
        var stretch = Rule("Stretch", 3600, 300);
        Start(stretch);
        Run(60);
        var e = Run(240, idleFrom: 1);
        Assert.Empty(Sats(e));
        // time without input never counts (the timer pauses after FreezeAfter and gives back what it counted)
        Assert.Equal(TimeSpan.FromSeconds(3600 - 60), Sch.TimeUntil(stretch.Id));
    }

    [Fact]
    public void NaturalBreaksOff_NeverSatisfies()
    {
        Settings.Idle.NaturalBreaks = false;
        Start(Rule("Eye rest", 1200, 20));
        Assert.Empty(Sats(Run(1000, idleFrom: 1)));
    }

    [Fact]
    public void ClockTime_SatisfiedByLongIdle()
    {
        var r = Rule("Clock", 600, 60, TimerMode.ClockTime);
        Start(r);
        Run(100);
        Assert.Single(Sats(Run(Threshold, idleFrom: 1)));
        Assert.Equal(TimeSpan.FromSeconds(600), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void ClockAligned_IdleAtBoundary_Satisfied()
    {
        Clock.Now = new DateTime(2026, 11, 20, 9, 29, 50);
        var r = Rule("Aligned", 1800, 20, TimerMode.ClockAligned);
        Start(r);
        var e = Run(20, idleFrom: Threshold + 100);
        Assert.Empty(Dues(e));
        Assert.Single(Sats(e));
        Assert.Equal(new DateTime(2026, 11, 20, 10, 0, 0) - Clock.Now, Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void ClockAligned_ReturnBeforeDurationSatisfied_FiresOnReturn()
    {
        Clock.Now = new DateTime(2026, 11, 20, 9, 29, 50);
        var r = Rule("Aligned", 1800, 600, TimerMode.ClockAligned);
        Start(r);
        // idle 200 s at the boundary: past the threshold but shorter than the 600 s duration
        var e = Run(20, idleFrom: Threshold);
        Assert.Empty(Dues(e));
        Assert.Single(Dues(Run(1))); // user is back
    }

    [Fact]
    public void SleepGap_LongerThanDuration_Satisfies()
    {
        Settings = Models.AppSettings.CreateDefault();
        Sch = new Scheduler(Settings, Clock);
        Run(10);
        Clock.Advance(TimeSpan.FromHours(2));
        var e = TickOnce();
        Assert.Empty(Dues(e));
        Assert.Equal(2, Sats(e).Count());
        Assert.Empty(Dues(Run(60)));
    }

    [Fact]
    public void SleepGap_ShortOverdue_FiresOnce()
    {
        Start(Rule("Clock", 60, 50, TimerMode.ClockTime));
        Run(50);
        Clock.Advance(TimeSpan.FromSeconds(40)); // gap > 30 s but shorter than the 50 s break
        var e = TickOnce();
        e.AddRange(Run(5));
        Assert.Single(Dues(e));
    }

    [Fact]
    public void ClockGoesBackwards_NoCrashNoFire()
    {
        var r = Rule("A", 600, 10);
        Start(r);
        Run(10);
        Clock.Advance(TimeSpan.FromHours(-1));
        Assert.Empty(Run(5));
        // the first tick after the backwards jump counts nothing, the next four count 1 s each
        Assert.Equal(TimeSpan.FromSeconds(586), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void Collision_BiggerWins_MergesSmall()
    {
        Settings.Idle.NaturalBreaks = false;
        Settings.CollisionWindow = TimeSpan.FromSeconds(5);
        var a = Rule("Eye", 30, 5, TimerMode.ClockTime);
        var b = Rule("Stretch", 60, 10, TimerMode.ClockTime);
        Start(a, b);
        Assert.Equal("Eye", Assert.Single(Dues(Run(30))).Rule.Name);
        Sch.BreakFinished(a.Id, BreakOutcome.Taken);
        var due = Assert.Single(Dues(Run(30)));
        Assert.Equal("Stretch", due.Rule.Name);
        Assert.Equal("Eye", Assert.Single(due.Merged).Name);
        Sch.BreakStarted(b.Id);
        Sch.BreakFinished(b.Id, BreakOutcome.Taken);
        Assert.False(Sch.InBreak);
        Assert.Equal(TimeSpan.FromSeconds(30), Sch.TimeUntil(a.Id));
        Assert.Equal(TimeSpan.FromSeconds(60), Sch.TimeUntil(b.Id));
    }

    [Fact]
    public void Collision_Sequential_EmitsBothShortestFirst()
    {
        Settings.Idle.NaturalBreaks = false;
        Settings.Collision = CollisionPolicy.Sequential;
        Settings.CollisionWindow = TimeSpan.FromSeconds(5);
        var a = Rule("Eye", 30, 5, TimerMode.ClockTime);
        var b = Rule("Stretch", 60, 10, TimerMode.ClockTime);
        Start(b, a);
        Run(30);
        Sch.BreakFinished(a.Id, BreakOutcome.Taken);
        var dues = Dues(Run(30)).ToList();
        Assert.Equal(["Eye", "Stretch"], dues.Select(d => d.Rule.Name));
        Assert.All(dues, d => Assert.Empty(d.Merged));
    }

    [Fact]
    public void Collision_ResetAll_ResetsEveryRule()
    {
        Settings.Idle.NaturalBreaks = false;
        Settings.Collision = CollisionPolicy.ResetAll;
        Settings.CollisionWindow = TimeSpan.FromSeconds(5);
        var a = Rule("A", 30, 5, TimerMode.ClockTime);
        var b = Rule("B", 100, 10, TimerMode.ClockTime);
        Start(a, b);
        Run(30);
        Sch.BreakFinished(a.Id, BreakOutcome.Taken);
        Assert.Equal(TimeSpan.FromSeconds(100), Sch.TimeUntil(b.Id));
    }

    [Fact]
    public void CollisionWindow_IncludesSoonDue()
    {
        Settings.Idle.NaturalBreaks = false;
        var stretch = Rule("Stretch", 60, 10);
        var eye = Rule("Eye", 100, 5);
        Start(eye, stretch);
        var due = Assert.Single(Dues(Run(60)));
        Assert.Equal("Stretch", due.Rule.Name);
        Assert.Equal("Eye", Assert.Single(due.Merged).Name);
    }

    [Fact]
    public void PauseResetPolicy_ResetsOnResume()
    {
        Settings.Pause.TimersDuringPause = PauseTimerPolicy.Reset;
        var r = Rule("A", 600, 10);
        Start(r);
        Run(100);
        Run(10, pause: new PauseState(true, "x"));
        Run(1);
        Assert.Equal(TimeSpan.FromSeconds(599), Sch.TimeUntil(r.Id));
    }
}

public class IdleFreezeTests : SchedulerTestBase
{
    [Fact]
    public void FreezeAfter_DefaultsToOneMinute() => Assert.Equal(TimeSpan.FromMinutes(1), new IdleSettings().FreezeAfter);

    [Fact]
    public void ActiveTime_TimeWithoutInputNeverCounts()
    {
        Settings.Idle.NaturalBreaks = false;
        var r = Rule("A", 600, 10);
        Start(r);
        Run(100);
        Run(200, idleFrom: 1);  // 200 s without input
        Assert.Equal(TimeSpan.FromSeconds(500), Sch.TimeUntil(r.Id));
        Run(50);                // back at the PC
        Assert.Equal(TimeSpan.FromSeconds(450), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void ActiveTime_ShortPausesBelowFreezeAfterStillCount()
    {
        Settings.Idle.NaturalBreaks = false;
        var r = Rule("A", 600, 10);
        Start(r);
        Run(30, idleFrom: 1);   // reading for 30 s: under the 1 min freeze, counts
        Assert.Equal(TimeSpan.FromSeconds(570), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void ClockTime_KeepsCountingWithoutInput()
    {
        Settings.Idle.NaturalBreaks = false;
        var r = Rule("A", 600, 10, TimerMode.ClockTime);
        Start(r);
        Run(200, idleFrom: 1);
        Assert.Equal(TimeSpan.FromSeconds(400), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void IsFrozen_ReportsNoInputPause()
    {
        Settings.Idle.NaturalBreaks = false;
        Start(Rule("A", 600, 10));
        Run(70, idleFrom: 1);
        Assert.True(Sch.TimersFrozen);
        Run(1);
        Assert.False(Sch.TimersFrozen);
    }

    [Fact]
    public void Normalize_ClampsFreezeAfter()
    {
        var s = AppSettings.CreateDefault();
        s.Idle.FreezeAfter = TimeSpan.Zero;
        Core.Settings.SettingsValidator.Normalize(s);
        Assert.Equal(TimeSpan.FromSeconds(5), s.Idle.FreezeAfter);
        s.Idle.FreezeAfter = TimeSpan.FromHours(2);
        Core.Settings.SettingsValidator.Normalize(s);
        Assert.Equal(s.Idle.Threshold, s.Idle.FreezeAfter);
    }
}
