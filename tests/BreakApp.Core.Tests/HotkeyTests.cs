using BreakApp.Core.Settings;

namespace BreakApp.Core.Tests;

public class HotkeyTests
{
    [Fact]
    public void Parses_ModifiersAndKey()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt+B", out var h));
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt, h.Modifiers);
        Assert.Equal("B", h.Key);
    }

    [Theory]
    [InlineData("control + alt + p", "Ctrl+Alt+P")]
    [InlineData("Win+Shift+F12", "Shift+Win+F12")]
    [InlineData("alt+ctrl+d1", "Ctrl+Alt+D1")]
    public void Normalizes(string input, string expected)
    {
        Assert.True(Hotkey.TryParse(input, out var h));
        Assert.Equal(expected, h.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("B")]
    [InlineData("Ctrl+B+C")]
    [InlineData("Hyper+B")]
    public void Rejects(string input) => Assert.False(Hotkey.TryParse(input, out _));
}
