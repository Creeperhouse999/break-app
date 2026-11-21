namespace BreakApp.Core.Tests;

/// <summary>Guards for the UX guidelines that a unit test can check by reading the app's source.</summary>
public class SourceGuardTests
{
    private static string AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BreakApp.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "BreakApp");
    }

    [Fact]
    public void NoPlainWindowsMessageBoxes()
    {
        // UX guideline 5: no plain Windows controls; use SettingsUI.ThemedDialog instead.
        var offenders = Directory.EnumerateFiles(AppSource(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains("MessageBox.Show"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(offenders);
    }
}
