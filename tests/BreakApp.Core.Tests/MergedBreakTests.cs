using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;

namespace BreakApp.Core.Tests;

public class MergedBreakTests
{
    private static BreakRule R(string name, int seconds, LockLevel lockLevel, bool skip = true, bool postpone = true) => new()
    {
        Name = name, Duration = TimeSpan.FromSeconds(seconds), Lock = lockLevel, AllowSkip = skip, AllowPostpone = postpone,
        Style = BreakStyle.Fullscreen,
    };

    [Fact]
    public void NoMerged_ReturnsPrimaryUnchanged()
    {
        var p = R("A", 60, LockLevel.Skippable);
        Assert.Same(p, MergedBreak.Effective(p, []));
    }

    [Fact]
    public void MergingNeverWeakensTheLock()
    {
        var locked = R("Locked", 15, LockLevel.LockedWithKeybind);
        var total = R("Total", 6, LockLevel.Total);
        var e = MergedBreak.Effective(locked, [total]);
        Assert.Equal(LockLevel.Total, e.Lock);
        Assert.Equal(TimeSpan.FromSeconds(15), e.Duration);
        Assert.Equal("Locked", e.Name);
        Assert.Equal(locked.Id, e.Id);
    }

    [Fact]
    public void MergingKeepsStrictestSkipAndPostpone()
    {
        var e = MergedBreak.Effective(R("A", 60, LockLevel.Skippable), [R("B", 5, LockLevel.Skippable, skip: false, postpone: false)]);
        Assert.False(e.AllowSkip);
        Assert.False(e.AllowPostpone);
    }

    [Fact]
    public void MergingTakesLongestDuration()
    {
        var e = MergedBreak.Effective(R("A", 30, LockLevel.Skippable), [R("B", 90, LockLevel.Skippable)]);
        Assert.Equal(TimeSpan.FromSeconds(90), e.Duration);
    }

    [Fact]
    public void Effective_DoesNotMutateRules()
    {
        var p = R("A", 30, LockLevel.Skippable);
        MergedBreak.Effective(p, [R("B", 90, LockLevel.Total)]);
        Assert.Equal(LockLevel.Skippable, p.Lock);
        Assert.Equal(TimeSpan.FromSeconds(30), p.Duration);
    }
}
