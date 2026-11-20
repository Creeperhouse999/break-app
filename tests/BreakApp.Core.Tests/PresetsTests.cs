using BreakApp.Core.Models;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class PresetsTests
{
    private static Preset Get(string name) => Presets.All.Single(p => p.Name == name);

    [Fact]
    public void All_HasFourPresetsInOrder() =>
        Assert.Equal(["Gentle", "Balanced", "Eye care", "Strict"], Presets.All.Select(p => p.Name));

    [Fact]
    public void Gentle_MakesAllRulesNotificationSkippable()
    {
        var s = AppSettings.CreateDefault();
        s.Rules[1].Lock = LockLevel.Total;
        var r = Get("Gentle").Apply(s);
        Assert.All(r.Rules, x => Assert.Equal(BreakStyle.Notification, x.Style));
        Assert.All(r.Rules, x => Assert.Equal(LockLevel.Skippable, x.Lock));
    }

    [Fact]
    public void Strict_EnablesCommitmentAndLocks()
    {
        var r = Get("Strict").Apply(AppSettings.CreateDefault());
        Assert.True(r.Commitment.Enabled);
        var stretch = r.Rules.Single(x => x.Name == "Stretch");
        Assert.Equal(LockLevel.LockedWithKeybind, stretch.Lock);
        Assert.False(stretch.AllowSkip);
        // Notification rules keep their style and stay unlocked
        Assert.Equal(BreakStyle.Notification, r.Rules.Single(x => x.Name == "Eye rest").Style);
    }

    [Fact]
    public void Apply_DoesNotMutateInput()
    {
        var s = AppSettings.CreateDefault();
        Get("Gentle").Apply(s);
        Assert.Equal(BreakStyle.Fullscreen, s.Rules[1].Style);
    }

    [Fact]
    public void Apply_KeepsAppearance()
    {
        var s = AppSettings.CreateDefault();
        s.Appearance.Accent = Accent.Teal;
        s.Pause.RespectDoNotDisturb = true;
        foreach (var p in Presets.All)
        {
            var r = p.Apply(s);
            Assert.Equal(Accent.Teal, r.Appearance.Accent);
            Assert.True(r.Pause.RespectDoNotDisturb);
        }
    }

    [Fact]
    public void Diff_ListsChangedFields()
    {
        var s = AppSettings.CreateDefault();
        var lines = Presets.Diff(s, Get("Gentle").Apply(s));
        Assert.Contains(lines, l => l.Contains("Stretch") && l.Contains("Notification"));
        Assert.Empty(Presets.Diff(s, SettingsStore.Clone(s)));
    }

    [Fact]
    public void Diff_ReportsCommitmentChange()
    {
        var s = AppSettings.CreateDefault();
        var lines = Presets.Diff(s, Get("Strict").Apply(s));
        Assert.Contains("Commitment mode: off → on", lines);
    }

    [Fact]
    public void Templates_AllProduceValidRules()
    {
        Assert.Equal(["Eye rest", "Stretch", "Walk", "Hydrate", "Custom"], RuleTemplates.All.Select(t => t.Name));
        foreach (var (_, create) in RuleTemplates.All)
        {
            var r = create();
            var before = System.Text.Json.JsonSerializer.Serialize(r);
            SettingsValidator.NormalizeRule(r);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(r));
        }
    }

    [Fact]
    public void Templates_CreateFreshIds()
    {
        var create = RuleTemplates.All[0].Create;
        Assert.NotEqual(create().Id, create().Id);
    }
}
