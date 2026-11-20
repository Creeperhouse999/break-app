using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;

namespace BreakApp.Core.Tests;

public class PauseEvaluatorTests
{
    // Monday 2026-11-16 10:15
    private static readonly DateTime MonMorning = new(2026, 11, 16, 10, 15, 0);
    private static PauseSignals None => new(false, null, false, false, null, false);

    private static PauseSettings WithLesson() => new()
    {
        QuietTimes = [new TimeWindow { Days = [DayOfWeek.Monday, DayOfWeek.Wednesday], Start = new TimeOnly(10, 0), End = new TimeOnly(11, 30), Label = "Lesson" }],
    };

    [Fact]
    public void NoSignals_NotPaused() => Assert.False(PauseEvaluator.Evaluate(new PauseSettings(), None, MonMorning).IsPaused);

    [Fact]
    public void Manual_TakesPriority()
    {
        var p = PauseEvaluator.Evaluate(WithLesson(), None with { MeetingActive = true, ManualPauseUntil = MonMorning.AddMinutes(30) }, MonMorning);
        Assert.True(p.IsPaused);
        Assert.Equal("Paused manually until 10:45", p.Reason);
    }

    [Fact]
    public void ManualIndefinite_HasNoTime()
    {
        var p = PauseEvaluator.Evaluate(new PauseSettings(), None with { ManualPauseIndefinite = true }, MonMorning);
        Assert.Equal("Paused manually", p.Reason);
    }

    [Fact]
    public void ManualExpired_NotPaused() =>
        Assert.False(PauseEvaluator.Evaluate(new PauseSettings(), None with { ManualPauseUntil = MonMorning.AddMinutes(-1) }, MonMorning).IsPaused);

    [Fact]
    public void Meeting_PausesWithSource()
    {
        var p = PauseEvaluator.Evaluate(new PauseSettings(), None with { MeetingActive = true, MeetingSource = "chrome" }, MonMorning);
        Assert.Equal("In a meeting (chrome)", p.Reason);
    }

    [Fact]
    public void QuietTime_MatchesDayAndTime()
    {
        var p = PauseEvaluator.Evaluate(WithLesson(), None, MonMorning);
        Assert.True(p.IsPaused);
        Assert.Equal("Quiet time: Lesson", p.Reason);
        Assert.False(PauseEvaluator.Evaluate(WithLesson(), None, MonMorning.AddDays(1)).IsPaused);
    }

    [Fact]
    public void QuietTime_NoLabel()
    {
        var s = WithLesson();
        s.QuietTimes[0].Label = "";
        Assert.Equal("Quiet time", PauseEvaluator.Evaluate(s, None, MonMorning).Reason);
    }

    [Fact]
    public void Dnd_IgnoredUnlessRespect()
    {
        var sig = None with { DoNotDisturb = true };
        Assert.False(PauseEvaluator.Evaluate(new PauseSettings(), sig, MonMorning).IsPaused);
        Assert.Equal("Do Not Disturb", PauseEvaluator.Evaluate(new PauseSettings { RespectDoNotDisturb = true }, sig, MonMorning).Reason);
    }

    [Fact]
    public void Fullscreen_IgnoredByDefault()
    {
        var sig = None with { FullscreenApp = true };
        Assert.False(PauseEvaluator.Evaluate(new PauseSettings(), sig, MonMorning).IsPaused);
        Assert.Equal("Fullscreen app", PauseEvaluator.Evaluate(new PauseSettings { PauseDuringFullscreen = true }, sig, MonMorning).Reason);
    }

    [Fact]
    public void EndRunningBreak_NotForTotal()
    {
        var paused = new PauseState(true, "In a meeting (zoom)");
        Assert.True(PauseEvaluator.ShouldEndRunningBreak(new BreakRule { Lock = LockLevel.Skippable }, paused));
        Assert.True(PauseEvaluator.ShouldEndRunningBreak(new BreakRule { Lock = LockLevel.LockedWithKeybind }, paused));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(new BreakRule { Lock = LockLevel.Total }, paused));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(new BreakRule { EndOnMeeting = false }, paused));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(new BreakRule(), PauseState.None));
    }
}
