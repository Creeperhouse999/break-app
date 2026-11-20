using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class SettingsPathsTests
{
    [Fact]
    public void Leaves_IncludeNestedAndRuleFields()
    {
        var all = SettingsPaths.AllLeaves();
        Assert.Contains("Idle.Threshold", all);
        Assert.Contains("Rules[].Flash.PulseAmount", all);
        Assert.Contains("Rules[].Warning.LeadTime", all);
        Assert.Contains("Rules[].ActiveWindows", all);
        Assert.Contains("Pause.QuietTimes", all);
        Assert.Contains("Texts.WarningTitle", all);
        Assert.Contains("Appearance.CustomAccent", all);
        Assert.Contains("Rules[].Appearance.Accent", all);
    }

    [Fact]
    public void Leaves_ExcludeInternalFields()
    {
        var all = SettingsPaths.AllLeaves();
        Assert.DoesNotContain("Version", all);
        Assert.DoesNotContain("Rules[].Id", all);
        Assert.DoesNotContain("Rules[].Order", all);
        Assert.DoesNotContain("Pause.QuietTimes[].Label", all); // time windows are edited as a whole
    }

    [Fact]
    public void Missing_ReportsUncovered() =>
        Assert.Equal(["Idle.Threshold"], SettingsPaths.Missing(SettingsPaths.AllLeaves().Where(p => p != "Idle.Threshold").ToHashSet()));
}
