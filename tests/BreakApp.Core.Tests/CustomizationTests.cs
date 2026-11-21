using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class CustomizationTests
{
    // ---------------------------------------------------------------- texts
    [Fact]
    public void Render_ReplacesKnownPlaceholdersCaseInsensitive()
    {
        var s = Templates.Render("{Rule} in {time}!", new Dictionary<string, string> { ["rule"] = "Stretch", ["time"] = "30 s" });
        Assert.Equal("Stretch in 30 s!", s);
    }

    [Fact]
    public void Render_LeavesUnknownPlaceholders() =>
        Assert.Equal("Hi {who}", Templates.Render("Hi {who}", new Dictionary<string, string>()));

    [Fact]
    public void Render_NullTemplate_Empty() => Assert.Equal("", Templates.Render(null, new Dictionary<string, string>()));

    [Fact]
    public void Texts_HaveDefaults()
    {
        var t = new TextSettings();
        Assert.Equal("{rule} in {time}", t.WarningTitle);
        Assert.Equal("Skip break", t.SkipBreakButton);
        Assert.Null(typeof(TextSettings).GetProperty("BrandName")); // the app name is fixed, never editable
    }

    [Fact]
    public void Normalize_BlankTextsFallBackToDefaults()
    {
        var s = AppSettings.CreateDefault();
        s.Texts.SkipButton = "  ";
        s.Texts.WarningTitle = null!;
        s.Texts.HintSkip = "";
        SettingsValidator.Normalize(s);
        Assert.Equal("Skip", s.Texts.SkipButton);
        Assert.Equal("{rule} in {time}", s.Texts.WarningTitle);
        Assert.Equal("", s.Texts.HintSkip); // hints may be blank on purpose (= hidden)
    }

    [Fact]
    public void Texts_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BreakAppTexts_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "s.json");
        var s = AppSettings.CreateDefault();
        s.Texts.WarningTitle = "Przerwa za {time}";
        SettingsStore.Save(s, path);
        Assert.Equal("Przerwa za {time}", SettingsStore.Load(path).Texts.WarningTitle);
        Directory.Delete(dir, true);
    }

    // ---------------------------------------------------------------- colours
    [Theory]
    [InlineData("#2563eb", true)]
    [InlineData("2563EB", true)]
    [InlineData("#abc", true)]
    [InlineData("#12345", false)]
    [InlineData("blue", false)]
    [InlineData("", false)]
    public void ColorHex_Parses(string hex, bool ok) => Assert.Equal(ok, ColorHex.TryParse(hex, out _, out _, out _));

    [Fact]
    public void ColorHex_ShortFormExpands()
    {
        Assert.True(ColorHex.TryParse("#abc", out var r, out var g, out var b));
        Assert.Equal((0xAA, 0xBB, 0xCC), (r, g, b));
    }

    [Fact]
    public void ColorHex_Lighten()
    {
        Assert.Equal("#FFFFFF", ColorHex.Lighten("#000000", 1));
        Assert.Equal("#808080", ColorHex.Lighten("#000000", 0.5));
        Assert.Equal("#2563EB", ColorHex.Lighten("#2563eb", 0));
    }

    [Fact]
    public void Normalize_DropsInvalidColors()
    {
        var s = AppSettings.CreateDefault();
        s.Appearance.CustomAccent = "not a color";
        s.Appearance.BreakBackgroundColor = "#123456";
        s.Rules[0].Flash.Color = "zzz";
        SettingsValidator.Normalize(s);
        Assert.Null(s.Appearance.CustomAccent);
        Assert.Equal("#123456", s.Appearance.BreakBackgroundColor);
        Assert.Null(s.Rules[0].Flash.Color);
    }

    // ---------------------------------------------------------------- exceptions + clock format
    private static readonly DateTime Mon = new(2026, 11, 16, 10, 15, 0);
    private static PauseSignals Sig(string? fg = null, DateTime? manual = null) => new(false, null, false, false, manual, false, fg);

    [Fact]
    public void ForegroundException_Pauses()
    {
        var s = new PauseSettings { ForegroundExceptions = ["obs64", "Photoshop.exe"] };
        Assert.Equal("Exception: obs64 is in front", PauseEvaluator.Evaluate(s, Sig("OBS64"), Mon).Reason);
        Assert.True(PauseEvaluator.Evaluate(s, Sig("photoshop"), Mon).IsPaused);
        Assert.False(PauseEvaluator.Evaluate(s, Sig("chrome"), Mon).IsPaused);
        Assert.False(PauseEvaluator.Evaluate(s, Sig(null), Mon).IsPaused);
    }

    [Fact]
    public void ManualPause_Uses12HourClockWhenAsked() =>
        Assert.Equal("Paused manually until 10:45 AM",
            PauseEvaluator.Evaluate(new PauseSettings(), Sig(manual: Mon.AddMinutes(30)), Mon, use24Hour: false).Reason);

    [Fact]
    public void General_Defaults24Hour() => Assert.True(new GeneralSettings().Use24HourClock);

    [Fact]
    public void FullscreenBreak_SolidByDefault() => Assert.Equal(1.0, new AppearanceSettings().Opacity);

    [Fact]
    public void MicCameraDetection_OffByDefault() => Assert.False(new PauseSettings().DetectMicCamera);

    [Fact]
    public void SetupDone_FalseByDefault_AndNotAUserSetting()
    {
        Assert.False(new GeneralSettings().SetupDone);
        Assert.DoesNotContain("General.SetupDone", SettingsPaths.AllLeaves());
    }
}

public class NeutralDefaultsTests
{
    [Fact]
    public void Defaults_AreShortAndLongBreak_WithNeutralMessages()
    {
        var s = AppSettings.CreateDefault();
        Assert.Equal(["Short break", "Long break"], s.Rules.Select(r => r.Name));
        var words = string.Join(" ", s.Rules.SelectMany(r => r.Messages)).ToLowerInvariant();
        Assert.DoesNotContain("eye", words);
        Assert.DoesNotContain("stretch", words);
        Assert.All(s.Rules, r => Assert.NotEmpty(r.Messages));
    }
}
