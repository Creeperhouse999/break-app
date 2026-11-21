using BreakApp.Core.Models;
using BreakApp.Core.Updates;

namespace BreakApp.Core.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("v1.0.10", "1.0.9", true)]
    [InlineData("v1.0.9", "1.0.10", false)]
    [InlineData("v2.0", "1.9.9", true)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    public void IsNewer_ComparesVersionsNotText(string latestTag, string current, bool expected) =>
        Assert.Equal(expected, ReleaseInfo.IsNewer(latestTag, current));

    private const string Sample = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/Creeperhouse999/break-app/releases/tag/v1.2.0",
          "body": "Fixes and a new thing.",
          "draft": false, "prerelease": false,
          "assets": [
            { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.invalid/notes.txt" },
            { "name": "BreakApp.exe", "size": 600000, "browser_download_url": "https://github.com/Creeperhouse999/break-app/releases/download/v1.2.0/BreakApp.exe" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsTagExeAndPage()
    {
        var r = ReleaseInfo.Parse(Sample);
        Assert.NotNull(r);
        Assert.Equal("1.2.0", r!.Version);
        Assert.Equal("https://github.com/Creeperhouse999/break-app/releases/download/v1.2.0/BreakApp.exe", r.ExeUrl);
        Assert.Equal(600000, r.ExeSize);
        Assert.Equal("https://github.com/Creeperhouse999/break-app/releases/tag/v1.2.0", r.PageUrl);
        Assert.Equal("Fixes and a new thing.", r.Notes);
    }

    [Fact]
    public void Parse_NoExe_ReturnsNull() =>
        Assert.Null(ReleaseInfo.Parse("""{ "tag_name": "v1.2.0", "html_url": "x", "assets": [] }"""));

    [Fact]
    public void Parse_Prerelease_ReturnsNull() =>
        Assert.Null(ReleaseInfo.Parse(Sample.Replace("\"prerelease\": false", "\"prerelease\": true")));

    [Fact]
    public void Parse_Garbage_ReturnsNull() => Assert.Null(ReleaseInfo.Parse("<html>rate limited</html>"));

    [Fact]
    public void Parse_OnlyTrustsGitHubDownloadUrls() =>
        Assert.Null(ReleaseInfo.Parse(Sample.Replace("https://github.com/Creeperhouse999/break-app/releases/download/v1.2.0/BreakApp.exe", "https://evil.example/BreakApp.exe")));

    [Fact]
    public void CheckForUpdates_OnByDefault() => Assert.True(new GeneralSettings().CheckForUpdates);
}
