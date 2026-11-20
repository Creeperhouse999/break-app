using BreakApp.Core.Commitment;
using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

/// <summary>Regression tests for the final-review findings (I1–I8).</summary>
public sealed class ReviewFixTests : SchedulerTestBase, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BreakAppReview_" + Guid.NewGuid().ToString("N"));
    private string P(string n) => Path.Combine(_dir, n);
    public ReviewFixTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ---------------------------------------------------------------- I1: valid JSON that used to crash Load
    [Fact]
    public void Load_NullListEntries_DoNotCrash()
    {
        File.WriteAllText(P("s.json"), """
            { "pause": { "quietTimes": [ null ], "meetingTitlePatterns": [ null, "Meet - " ] },
              "rules": [ { "name": "A", "activeWindows": [ null ], "messages": [ null, "hi" ] }, null ] }
            """);
        var s = SettingsStore.Load(P("s.json"));
        Assert.Empty(s.Pause.QuietTimes);
        Assert.Equal(["Meet - "], s.Pause.MeetingTitlePatterns);
        var r = Assert.Single(s.Rules);
        Assert.Empty(r.ActiveWindows);
        Assert.Equal(["hi"], r.Messages);
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefaults()
    {
        File.WriteAllText(P("s.json"), "{}");
        using var locked = new FileStream(P("s.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(2, SettingsStore.Load(P("s.json")).Rules.Count);
    }

    // ---------------------------------------------------------------- I8 / M2: enums and escape hotkey
    [Fact]
    public void Load_OutOfRangeEnums_FallBackToDefaults()
    {
        File.WriteAllText(P("s.json"), """{ "collision": 9, "rules": [ { "name": "A", "lock": 7, "style": 42, "timerMode": -1 } ], "appearance": { "theme": 5 } }""");
        var s = SettingsStore.Load(P("s.json"));
        Assert.Equal(CollisionPolicy.BiggerWins, s.Collision);
        Assert.Equal(LockLevel.Skippable, s.Rules[0].Lock);
        Assert.Equal(BreakStyle.Notification, s.Rules[0].Style);
        Assert.Equal(TimerMode.ActiveTime, s.Rules[0].TimerMode);
        Assert.Equal(ThemeMode.Auto, s.Appearance.Theme);
    }

    [Theory]
    [InlineData("Ctrl+Shift+1", "Ctrl+Shift+D1")]
    [InlineData("Ctrl+Alt+f5", "Ctrl+Alt+F5")]
    [InlineData("Ctrl+Space", "Ctrl+Space")]
    [InlineData("Alt+numpad7", "Alt+NumPad7")]
    public void Hotkey_MapsToRealKeyNames(string input, string expected)
    {
        Assert.True(Hotkey.TryParse(input, out var h));
        Assert.Equal(expected, h.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+F99")]
    [InlineData("Ctrl+Cancel")]
    public void Hotkey_RejectsUnknownKeys(string input) => Assert.False(Hotkey.TryParse(input, out _));

    [Fact]
    public void Normalize_InvalidEscapeHotkey_ResetsToDefault()
    {
        var s = AppSettings.CreateDefault();
        s.Lock.EscapeHotkey = "Ctrl+Foo";
        SettingsValidator.Normalize(s);
        Assert.Equal(new LockSettings().EscapeHotkey, s.Lock.EscapeHotkey);
    }

    [Fact]
    public void Normalize_InvalidGlobalHotkeys_Cleared()
    {
        var s = AppSettings.CreateDefault();
        s.General.PauseHotkey = "Ctrl+Nope";
        SettingsValidator.Normalize(s);
        Assert.Equal("", s.General.PauseHotkey);
    }

    // ---------------------------------------------------------------- I2: commitment bypasses
    private AppSettings Committed()
    {
        var s = AppSettings.CreateDefault();
        s.Commitment.Enabled = true;
        s.Rules[1].AllowSkip = false;   // Stretch is strict
        return s;
    }

    [Fact]
    public void Weakening_LoweringCooldownOrWindowCounts()
    {
        var before = Committed();
        var after = SettingsStore.Clone(before);
        after.Commitment.Cooldown = TimeSpan.Zero;
        after.Commitment.Window = TimeSpan.FromMinutes(1);
        var lines = new CommitmentGuard(Clock).WeakeningChanges(before, after);
        Assert.Contains(lines, l => l.Contains("cooldown"));
        Assert.Contains(lines, l => l.Contains("window"));
    }

    [Fact]
    public void Weakening_StyleScheduleAndPostponeCount()
    {
        var before = Committed();
        var g = new CommitmentGuard(Clock);
        var style = SettingsStore.Clone(before); style.Rules[1].Style = BreakStyle.Notification;
        var sched = SettingsStore.Clone(before); sched.Rules[1].ActiveWindows.Add(new TimeWindow { Days = [DayOfWeek.Sunday] });
        var post = SettingsStore.Clone(before); post.Rules[1].MaxPostpones = 10;
        var len = SettingsStore.Clone(before); len.Rules[1].PostponeLength = TimeSpan.FromHours(1);
        Assert.NotEmpty(g.WeakeningChanges(before, style));
        Assert.NotEmpty(g.WeakeningChanges(before, sched));
        Assert.NotEmpty(g.WeakeningChanges(before, post));
        Assert.NotEmpty(g.WeakeningChanges(before, len));
    }

    [Fact]
    public void Weakening_ReportsEveryRule()
    {
        var before = Committed();
        before.Rules[0].AllowSkip = false; // both strict
        var after = SettingsStore.Clone(before);
        after.Rules[0].AllowSkip = true;
        after.Rules[1].AllowSkip = true;
        var lines = new CommitmentGuard(Clock).WeakeningChanges(before, after);
        Assert.Contains(lines, l => l.Contains("Eye rest"));
        Assert.Contains(lines, l => l.Contains("Stretch"));
    }

    [Fact]
    public void Schedule_UsesCooldownFromCurrentSettings()
    {
        var before = Committed();
        var after = SettingsStore.Clone(before);
        after.Commitment.Enabled = false;
        after.Commitment.Cooldown = TimeSpan.Zero;
        var g = new CommitmentGuard(Clock);
        g.Schedule(after, before.Commitment.Cooldown);
        Assert.Equal(Clock.Now + before.Commitment.Cooldown, g.PendingAppliesAt);
        Assert.Null(g.DuePending());
    }

    [Fact]
    public void Split_AppliesHarmlessChangesNow_KeepsCommittedParts()
    {
        var before = Committed();
        var after = SettingsStore.Clone(before);
        after.Appearance.Accent = Accent.Rose;           // harmless
        after.Rules[1].Lock = LockLevel.Skippable;       // committed rule: unchanged... but weakening anyway
        after.Rules[1].AllowSkip = true;                 // weakening
        after.Rules[0].Interval = TimeSpan.FromMinutes(30); // Eye rest is not strict: harmless
        after.Commitment.Enabled = false;                // weakening
        var now = CommitmentGuard.ApplyNowPart(before, after);
        Assert.Equal(Accent.Rose, now.Appearance.Accent);
        Assert.Equal(TimeSpan.FromMinutes(30), now.Rules[0].Interval);
        Assert.False(now.Rules[1].AllowSkip);
        Assert.True(now.Commitment.Enabled);
    }

    [Fact]
    public void Split_DeletedCommittedRuleStays()
    {
        var before = Committed();
        var after = SettingsStore.Clone(before);
        after.Rules.RemoveAt(1);
        var now = CommitmentGuard.ApplyNowPart(before, after);
        Assert.Contains(now.Rules, r => r.Id == before.Rules[1].Id);
    }

    // ---------------------------------------------------------------- I3: stuck "in break"
    [Fact]
    public void DeletingPrimaryMidBreak_ReleasesMergedRules()
    {
        Settings.Idle.NaturalBreaks = false;
        Settings.CollisionWindow = TimeSpan.FromSeconds(5);
        var a = Rule("Eye", 30, 5, TimerMode.ClockTime);
        var b = Rule("Stretch", 60, 10, TimerMode.ClockTime);
        Start(a, b);
        Run(30);
        Sch.BreakFinished(a.Id, BreakOutcome.Taken);
        Run(30); // Stretch primary, Eye merged
        Sch.BreakStarted(b.Id);
        var s2 = SettingsStore.Clone(Settings);
        s2.Rules.RemoveAll(r => r.Id == b.Id);
        Sch.UpdateSettings(s2);
        Assert.False(Sch.InBreak);
        Assert.Single(Dues(Run(30)));
    }

    [Fact]
    public void ReleaseOrphans_ResetsPendingRulesNobodyIsShowing()
    {
        var r = Rule("A", 10, 5);
        Start(r);
        Run(10); // pending, but the shell never started it
        Assert.True(Sch.InBreak);
        Sch.ReleaseOrphans([]);
        Assert.False(Sch.InBreak);
        Assert.Equal(TimeSpan.FromSeconds(10), Sch.TimeUntil(r.Id));
    }

    [Fact]
    public void ReleaseOrphans_KeepsRunningBreak()
    {
        var r = Rule("A", 10, 5);
        Start(r);
        Run(10);
        Sch.BreakStarted(r.Id);
        Sch.ReleaseOrphans([r.Id]);
        Assert.True(Sch.InBreak);
    }

    // ---------------------------------------------------------------- I4: pauses only end breaks they interrupt
    [Fact]
    public void TakeNow_IsMarkedManual()
    {
        var r = Rule("A", 600, 10);
        Start(r);
        Sch.TakeNow(r.Id);
        Assert.True(Assert.Single(Dues(Run(1))).Manual);
        Sch.BreakFinished(r.Id, BreakOutcome.Taken);
        Assert.False(Assert.Single(Dues(Run(600))).Manual);
    }

    [Fact]
    public void ShouldEndRunningBreak_OnlyWhenPauseStartsDuringIt()
    {
        var rule = new BreakRule();
        var paused = new PauseState(true, "In a meeting");
        Assert.True(PauseEvaluator.ShouldEndRunningBreak(rule, pausedAtStart: false, paused));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(rule, pausedAtStart: true, paused));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(rule, pausedAtStart: false, PauseState.None));
        Assert.False(PauseEvaluator.ShouldEndRunningBreak(new BreakRule { Lock = LockLevel.Total }, false, paused));
    }
}
