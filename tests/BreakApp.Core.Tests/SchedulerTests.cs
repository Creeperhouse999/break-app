using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;

namespace BreakApp.Core.Tests;

public class SchedulerTests : SchedulerTestBase
{
    public SchedulerTests() => Settings.Idle.NaturalBreaks = false;

    [Fact]
    public void ActiveTime_FiresAfterInterval()
    {
        Start(Rule("A", 60, 10));
        Assert.Empty(Dues(Run(59)));
        var due = Assert.Single(Dues(Run(1)));
        Assert.Equal("A", due.Rule.Name);
        Assert.Empty(due.Merged);
    }

    [Fact]
    public void Due_IsNotRepeatedWhileWaitingForStart()
    {
        Start(Rule("A", 60, 10));
        Assert.Single(Dues(Run(70)));
        Assert.True(Sch.InBreak);
    }

    [Fact]
    public void Warning_FiresOnceAtLeadTime()
    {
        Start(Rule("A", 60, 10, leadSec: 10));
        var e = Run(49);
        Assert.Empty(e.OfType<WarningEvent>());
        var w = Assert.Single(Run(1).OfType<WarningEvent>());
        Assert.Equal(TimeSpan.FromSeconds(10), w.TimeLeft);
        Assert.Empty(Run(9).OfType<WarningEvent>());
    }

    [Fact]
    public void ActiveTime_FreezesWhileIdle()
    {
        Start(Rule("A", 600, 10));
        Run(30);
        var before = Sch.TimeUntil(Settings.Rules[0].Id);
        Run(100, idleFrom: (int)Settings.Idle.Threshold.TotalSeconds);
        Assert.Equal(before, Sch.TimeUntil(Settings.Rules[0].Id));
    }

    [Fact]
    public void ClockTime_CountsWhileIdle()
    {
        Start(Rule("A", 600, 10, TimerMode.ClockTime));
        Run(100, idleFrom: (int)Settings.Idle.Threshold.TotalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(500), Sch.TimeUntil(Settings.Rules[0].Id));
    }

    [Fact]
    public void ClockAligned_FiresOnBoundary()
    {
        Clock.Now = new DateTime(2026, 11, 20, 9, 29, 58);
        Start(Rule("A", 1800, 10, TimerMode.ClockAligned));
        Assert.Equal(TimeSpan.FromSeconds(2), Sch.TimeUntil(Settings.Rules[0].Id));
        Assert.Empty(Dues(Run(1)));
        Assert.Single(Dues(Run(1)));
    }

    [Fact]
    public void Postpone_MovesDueAndRespectsMax()
    {
        var r = Rule("A", 60, 10);
        r.MaxPostpones = 1;
        r.PostponeLength = TimeSpan.FromSeconds(30);
        Start(r);
        Run(40);
        Assert.True(Sch.Postpone(r.Id));
        Assert.Equal(TimeSpan.FromSeconds(30), Sch.TimeUntil(r.Id));
        Assert.False(Sch.Postpone(r.Id));
        Assert.Empty(Dues(Run(29)));
        Assert.Single(Dues(Run(1)));
    }

    [Fact]
    public void Postpone_NotAllowed_ReturnsFalse()
    {
        var r = Rule("A", 60, 10);
        r.AllowPostpone = false;
        Start(r);
        Assert.False(Sch.Postpone(r.Id));
    }

    [Fact]
    public void BreakFinishedPostponed_PostponesInsteadOfReset()
    {
        var r = Rule("A", 600, 10);
        r.PostponeLength = TimeSpan.FromMinutes(5);
        Start(r);
        Run(600);
        Sch.BreakStarted(r.Id);
        Sch.BreakFinished(r.Id, BreakOutcome.Postponed);
        Assert.False(Sch.InBreak);
        Assert.Equal(TimeSpan.FromMinutes(5), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void SkipNext_EmitsSkippedNotBreak()
    {
        var r = Rule("A", 60, 10);
        Start(r);
        Sch.SkipNext(r.Id);
        var e = Run(60);
        Assert.Empty(Dues(e));
        var s = Assert.Single(Sats(e));
        Assert.Equal(BreakOutcome.Skipped, s.Reason);
        Assert.Equal(TimeSpan.FromSeconds(60), Sch.TimeUntil(r.Id));
        Assert.Single(Dues(Run(60)));
    }

    [Fact]
    public void TakeNow_FiresImmediately()
    {
        var r = Rule("A", 600, 10);
        Start(r);
        Sch.TakeNow(r.Id);
        Assert.Single(Dues(Run(1)));
    }

    [Fact]
    public void NothingCountsDuringBreak()
    {
        var a = Rule("A", 60, 10);
        var b = Rule("B", 600, 10);
        Start(a, b);
        Run(60);
        Sch.BreakStarted(a.Id);
        Run(100);
        Assert.Equal(TimeSpan.FromSeconds(540), Sch.TimeUntil(b.Id));
    }

    [Fact]
    public void Paused_NoFire_NoCount()
    {
        var r = Rule("A", 60, 10, leadSec: 10);
        Start(r);
        Assert.Empty(Run(120, pause: new PauseState(true, "test")));
        Assert.Equal(TimeSpan.FromSeconds(60), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void DisabledAndOutOfWindow_NeverFire()
    {
        var off = Rule("Off", 10, 5);
        off.Enabled = false;
        var windowed = Rule("Windowed", 10, 5);
        windowed.ActiveWindows.Add(new TimeWindow { Days = [DayOfWeek.Monday], Start = new TimeOnly(9, 0), End = new TimeOnly(10, 0) });
        Start(off, windowed);
        Assert.Empty(Run(100));
    }

    [Fact]
    public void NextBreak_NullWhenAllDisabled()
    {
        var off = Rule("Off", 10, 5);
        off.Enabled = false;
        Start(off);
        Assert.Null(Sch.NextBreak());
    }

    [Fact]
    public void NextBreak_ReturnsSoonest()
    {
        Start(Rule("Long", 600, 10), Rule("Short", 60, 10));
        Run(10);
        var next = Sch.NextBreak();
        Assert.NotNull(next);
        Assert.Equal("Short", next.Value.Rule.Name);
        Assert.Equal(TimeSpan.FromSeconds(50), next.Value.In);
    }

    [Fact]
    public void TinyInterval_DoesNotFireEveryTick()
    {
        var r = Rule("A", 10, 5);
        Start(r);
        Assert.Single(Dues(Run(10)));
        Sch.BreakStarted(r.Id);
        Run(5);
        Sch.BreakFinished(r.Id, BreakOutcome.Taken);
        Assert.Empty(Dues(Run(9)));
        Assert.Single(Dues(Run(1)));
    }

    [Fact]
    public void UpdateSettings_KeepsStateForExistingRules()
    {
        var r = Rule("A", 600, 10);
        Start(r);
        Run(100);
        var s2 = Core.Settings.SettingsStore.Clone(Settings);
        s2.Rules.Add(Rule("B", 300, 10));
        Sch.UpdateSettings(s2);
        Assert.Equal(TimeSpan.FromSeconds(500), Sch.TimeUntil(r.Id));
        Assert.Equal(TimeSpan.FromSeconds(300), Sch.TimeUntil(s2.Rules[1].Id));
    }
}
