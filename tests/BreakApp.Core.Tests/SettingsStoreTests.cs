using BreakApp.Core.Models;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BreakAppTests_" + Guid.NewGuid().ToString("N"));
    private string PathOf(string name) => System.IO.Path.Combine(_dir, name);

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsStore.Load(PathOf("settings.json"));
        Assert.Equal(2, s.Rules.Count);
        Assert.Equal("Eye rest", s.Rules[0].Name);
        Assert.Equal("Stretch", s.Rules[1].Name);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllGroups()
    {
        var s = AppSettings.CreateDefault();
        s.Collision = CollisionPolicy.Sequential;
        s.Idle.Threshold = TimeSpan.FromMinutes(5);
        s.Pause.QuietTimes.Add(new TimeWindow { Days = [DayOfWeek.Monday], Start = new TimeOnly(10, 0), End = new TimeOnly(11, 30), Label = "Lesson" });
        s.Lock.EscapeHotkey = "Ctrl+Alt+X";
        s.Commitment.Enabled = true;
        s.Appearance.Accent = Accent.Rose;
        s.Sound.Volume = 0.25;
        s.General.AdvancedMode = true;
        s.Rules[0].Flash.SpeedHz = 4;
        s.Rules[1].Lock = LockLevel.LockedWithKeybind;

        var path = PathOf("settings.json");
        SettingsStore.Save(s, path);
        var r = SettingsStore.Load(path);

        Assert.Equal(CollisionPolicy.Sequential, r.Collision);
        Assert.Equal(TimeSpan.FromMinutes(5), r.Idle.Threshold);
        var q = Assert.Single(r.Pause.QuietTimes);
        Assert.Equal("Lesson", q.Label);
        Assert.Equal(new TimeOnly(11, 30), q.End);
        Assert.Equal([DayOfWeek.Monday], q.Days);
        Assert.Equal("Ctrl+Alt+X", r.Lock.EscapeHotkey);
        Assert.True(r.Commitment.Enabled);
        Assert.Equal(Accent.Rose, r.Appearance.Accent);
        Assert.Equal(0.25, r.Sound.Volume);
        Assert.True(r.General.AdvancedMode);
        Assert.Equal(4, r.Rules[0].Flash.SpeedHz);
        Assert.Equal(LockLevel.LockedWithKeybind, r.Rules[1].Lock);
        Assert.Equal(s.Rules[0].Id, r.Rules[0].Id);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndBacksUp()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, "{not json");
        var s = SettingsStore.Load(path);
        Assert.Equal(2, s.Rules.Count);
        Assert.True(File.Exists(PathOf("settings.bad.json")));
    }

    [Fact]
    public void Normalize_ClampsDurationToInterval()
    {
        var s = AppSettings.CreateDefault();
        s.Rules[0].Interval = TimeSpan.FromMinutes(1);
        s.Rules[0].Duration = TimeSpan.FromMinutes(5);
        SettingsValidator.Normalize(s);
        Assert.Equal(TimeSpan.FromMinutes(1), s.Rules[0].Duration);
    }

    [Fact]
    public void Normalize_ClampsZeroInterval()
    {
        var s = AppSettings.CreateDefault();
        s.Rules[0].Interval = TimeSpan.Zero;
        SettingsValidator.Normalize(s);
        Assert.Equal(TimeSpan.FromSeconds(10), s.Rules[0].Interval);
    }

    [Fact]
    public void Normalize_ClampsLeadTime()
    {
        var s = AppSettings.CreateDefault();
        s.Rules[0].Interval = TimeSpan.FromMinutes(1);
        s.Rules[0].Duration = TimeSpan.FromSeconds(10);
        s.Rules[0].Warning.LeadTime = TimeSpan.FromMinutes(5);
        SettingsValidator.Normalize(s);
        Assert.Equal(TimeSpan.FromSeconds(30), s.Rules[0].Warning.LeadTime);
    }

    [Fact]
    public void Normalize_RegeneratesDuplicateIds()
    {
        var s = AppSettings.CreateDefault();
        s.Rules[1].Id = s.Rules[0].Id;
        SettingsValidator.Normalize(s);
        Assert.NotEqual(s.Rules[0].Id, s.Rules[1].Id);
    }

    [Fact]
    public void TimeWindow_WrapsMidnight()
    {
        var w = new TimeWindow { Days = [DayOfWeek.Friday], Start = new TimeOnly(22, 0), End = new TimeOnly(2, 0) };
        // 2026-11-20 is a Friday
        Assert.True(w.Contains(new DateTime(2026, 11, 20, 23, 30, 0)));
        Assert.True(w.Contains(new DateTime(2026, 11, 21, 1, 0, 0)));
        Assert.False(w.Contains(new DateTime(2026, 11, 21, 3, 0, 0)));
        Assert.False(w.Contains(new DateTime(2026, 11, 20, 1, 0, 0)));
    }
}
