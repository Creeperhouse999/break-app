using BreakApp.Core.Commitment;
using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class CommitmentGuardTests : SchedulerTestBase
{
    private CommitmentGuard Guard => new(Clock);

    private BreakRule Walk(int intervalSec)
    {
        var w = Rule("Walk", intervalSec, 60, style: BreakStyle.Fullscreen, lockLevel: LockLevel.LockedWithKeybind);
        w.AllowSkip = false;
        return w;
    }

    [Fact]
    public void IsStrict_NoSkipOrTotal()
    {
        Assert.True(CommitmentGuard.IsStrict(new BreakRule { AllowSkip = false }));
        Assert.True(CommitmentGuard.IsStrict(new BreakRule { Lock = LockLevel.Total }));
        Assert.False(CommitmentGuard.IsStrict(new BreakRule()));
    }

    [Fact]
    public void Off_NeverBlocks()
    {
        Start(Walk(240));
        Assert.Null(Guard.BlockReason(Settings, Sch, GuardedAction.Quit));
    }

    [Fact]
    public void BlocksWithinWindowOnly()
    {
        Settings.Commitment.Enabled = true;
        Start(Walk(240));
        var reason = Guard.BlockReason(Settings, Sch, GuardedAction.Pause);
        Assert.Equal("Commitment mode: 'Walk' starts in 4 min", reason);

        Start(Walk(1800));
        Assert.Null(Guard.BlockReason(Settings, Sch, GuardedAction.Pause));
    }

    [Fact]
    public void NonStrictRule_NeverBlocks()
    {
        Settings.Commitment.Enabled = true;
        Start(Rule("Soft", 60, 10));
        Assert.Null(Guard.BlockReason(Settings, Sch, GuardedAction.SkipNext));
    }

    [Fact]
    public void PerRuleOverride_Wins()
    {
        var w = Walk(240);
        w.CommitmentOverride = true;
        Start(w);
        Assert.NotNull(Guard.BlockReason(Settings, Sch, GuardedAction.DisableRule));

        Settings.Commitment.Enabled = true;
        w.CommitmentOverride = false;
        Assert.Null(Guard.BlockReason(Settings, Sch, GuardedAction.DisableRule));
    }

    [Fact]
    public void Weakening_DetectsLockLowered()
    {
        Settings.Commitment.Enabled = true;
        Start(Walk(240));
        var after = SettingsStore.Clone(Settings);
        after.Rules[0].Lock = LockLevel.Skippable;
        after.Rules[0].AllowSkip = true;
        var lines = Guard.WeakeningChanges(Settings, after);
        Assert.Contains(lines, l => l.Contains("Walk") && l.Contains("lock"));
        Assert.Contains(lines, l => l.Contains("Walk") && l.Contains("skip"));
    }

    [Fact]
    public void Weakening_DetectsDeleteDisableAndTurnOff()
    {
        Settings.Commitment.Enabled = true;
        Start(Walk(240));
        var deleted = SettingsStore.Clone(Settings);
        deleted.Rules.Clear();
        Assert.NotEmpty(Guard.WeakeningChanges(Settings, deleted));

        var disabled = SettingsStore.Clone(Settings);
        disabled.Rules[0].Enabled = false;
        Assert.NotEmpty(Guard.WeakeningChanges(Settings, disabled));

        var longer = SettingsStore.Clone(Settings);
        longer.Rules[0].Interval *= 2;
        Assert.NotEmpty(Guard.WeakeningChanges(Settings, longer));

        var off = SettingsStore.Clone(Settings);
        off.Commitment.Enabled = false;
        Assert.Contains("Commitment mode turned off", Guard.WeakeningChanges(Settings, off));
    }

    [Fact]
    public void Weakening_IgnoresUnrelatedChanges()
    {
        Settings.Commitment.Enabled = true;
        Start(Walk(240));
        var after = SettingsStore.Clone(Settings);
        after.Appearance.Accent = Accent.Rose;
        after.Rules[0].Messages.Add("New idea");
        Assert.Empty(Guard.WeakeningChanges(Settings, after));
    }

    [Fact]
    public void Weakening_EmptyWhenCommitmentOff()
    {
        Start(Walk(240));
        var after = SettingsStore.Clone(Settings);
        after.Rules[0].Lock = LockLevel.Skippable;
        Assert.Empty(Guard.WeakeningChanges(Settings, after));
    }

    [Fact]
    public void Pending_AppliesAfterCooldown()
    {
        var g = Guard;
        var pending = AppSettings.CreateDefault();
        pending.Commitment.Cooldown = TimeSpan.FromMinutes(10);
        g.Schedule(pending, pending.Commitment.Cooldown);
        Assert.Equal(Clock.Now.AddMinutes(10), g.PendingAppliesAt);
        Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Null(g.DuePending());
        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Same(pending, g.DuePending());
        Assert.Null(g.DuePending());
        Assert.Null(g.PendingAppliesAt);
    }
}
