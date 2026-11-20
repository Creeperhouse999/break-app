using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(20, "20 s")]
    [InlineData(300, "5 min")]
    [InlineData(330, "5 min 30 s")]
    [InlineData(3600, "1 h")]
    [InlineData(5400, "1 h 30 min")]
    public void Duration_Formats(int seconds, string expected) =>
        Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(299.2, "5:00")]
    [InlineData(59, "0:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Countdown_RoundsUp(double seconds, string expected) =>
        Assert.Equal(expected, Format.Countdown(TimeSpan.FromSeconds(seconds)));
}
