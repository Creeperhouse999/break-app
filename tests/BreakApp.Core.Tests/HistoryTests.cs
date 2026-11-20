using BreakApp.Core.History;
using BreakApp.Core.Models;

namespace BreakApp.Core.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BreakAppHist_" + Guid.NewGuid().ToString("N"));
    private string P(string n) => Path.Combine(_dir, n);
    public HistoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static readonly Guid Eye = Guid.NewGuid();
    private static HistoryEntry E(int day, int hour, BreakOutcome o, string name = "Eye rest") =>
        new(new DateTime(2026, 11, day, hour, 0, 0), Eye, name, o, TimeSpan.FromSeconds(20));

    [Fact]
    public void AppendRead_RoundTrip()
    {
        var log = new HistoryLog(P("history.jsonl"));
        log.Append(E(20, 10, BreakOutcome.Taken));
        log.Append(E(20, 11, BreakOutcome.Skipped));
        var all = log.ReadAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(E(20, 11, BreakOutcome.Skipped), all[1]);
    }

    [Fact]
    public void ReadAll_MissingFile_Empty() => Assert.Empty(new HistoryLog(P("none.jsonl")).ReadAll());

    [Fact]
    public void MalformedLine_Skipped()
    {
        var log = new HistoryLog(P("history.jsonl"));
        log.Append(E(20, 10, BreakOutcome.Taken));
        File.AppendAllText(P("history.jsonl"), "garbage{\n");
        log.Append(E(20, 11, BreakOutcome.Taken));
        Assert.Equal(2, log.ReadAll().Count);
    }

    [Fact]
    public void Clear_Empties()
    {
        var log = new HistoryLog(P("history.jsonl"));
        log.Append(E(20, 10, BreakOutcome.Taken));
        log.Clear();
        Assert.Empty(log.ReadAll());
    }

    [Fact]
    public void Csv_HasHeaderAndQuotes()
    {
        var log = new HistoryLog(P("history.jsonl"));
        log.Append(E(20, 10, BreakOutcome.Taken, "Eye, \"rest\""));
        log.ExportCsv(P("out.csv"));
        var lines = File.ReadAllLines(P("out.csv"));
        Assert.Equal("Time,Rule,Outcome,DurationSeconds", lines[0]);
        Assert.Equal("2026-11-20T10:00:00,\"Eye, \"\"rest\"\"\",Taken,20", lines[1]);
    }

    [Fact]
    public void PerDay_CountsOutcomes()
    {
        var entries = new[]
        {
            E(19, 9, BreakOutcome.Taken), E(19, 10, BreakOutcome.EndedByKeybind), E(19, 11, BreakOutcome.Skipped),
            E(19, 12, BreakOutcome.Postponed), E(19, 13, BreakOutcome.SatisfiedByIdle), E(20, 9, BreakOutcome.Taken),
        };
        var days = Stats.PerDay(entries, new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 20));
        Assert.Equal(3, days.Count);
        Assert.Equal(new DayStat(new DateOnly(2026, 11, 18), 0, 0, 0, 0), days[0]);
        Assert.Equal(new DayStat(new DateOnly(2026, 11, 19), 2, 1, 1, 1), days[1]);
        Assert.Equal(1, days[2].Taken);
    }

    [Fact]
    public void PerRule_Groups()
    {
        var entries = new[] { E(19, 9, BreakOutcome.Taken), E(19, 10, BreakOutcome.Skipped, "Stretch"), E(20, 9, BreakOutcome.Taken) };
        var rules = Stats.PerRule(entries);
        Assert.Contains(new RuleStat("Eye rest", 2, 0, 0), rules);
        Assert.Contains(new RuleStat("Stretch", 0, 1, 0), rules);
    }

    [Fact]
    public void Streak_BreaksOnSkipDay()
    {
        var entries = new[]
        {
            E(17, 9, BreakOutcome.Taken), E(18, 9, BreakOutcome.Taken), E(18, 10, BreakOutcome.Skipped),
            E(19, 9, BreakOutcome.Taken), E(20, 9, BreakOutcome.Taken),
        };
        Assert.Equal(2, Stats.CurrentStreakDays(entries, new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Streak_TodayEmptyCountsFromYesterday()
    {
        var entries = new[] { E(18, 9, BreakOutcome.Taken), E(19, 9, BreakOutcome.Taken) };
        Assert.Equal(2, Stats.CurrentStreakDays(entries, new DateOnly(2026, 11, 20)));
        Assert.Equal(0, Stats.CurrentStreakDays(entries, new DateOnly(2026, 11, 22)));
    }
}
