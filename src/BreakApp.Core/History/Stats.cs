using BreakApp.Core.Models;

namespace BreakApp.Core.History;

public sealed record DayStat(DateOnly Day, int Taken, int Skipped, int Postponed, int Natural);
public sealed record RuleStat(string Rule, int Taken, int Skipped, int Postponed);

public static class Stats
{
    private static bool IsTaken(BreakOutcome o) => o is BreakOutcome.Taken or BreakOutcome.EndedByKeybind;

    public static IReadOnlyList<DayStat> PerDay(IEnumerable<HistoryEntry> entries, DateOnly from, DateOnly to)
    {
        var byDay = entries.GroupBy(e => DateOnly.FromDateTime(e.Time)).ToDictionary(g => g.Key, g => g.ToList());
        var list = new List<DayStat>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var es = byDay.GetValueOrDefault(d) ?? [];
            list.Add(new DayStat(d,
                es.Count(e => IsTaken(e.Outcome)),
                es.Count(e => e.Outcome == BreakOutcome.Skipped),
                es.Count(e => e.Outcome == BreakOutcome.Postponed),
                es.Count(e => e.Outcome == BreakOutcome.SatisfiedByIdle)));
        }
        return list;
    }

    public static IReadOnlyList<RuleStat> PerRule(IEnumerable<HistoryEntry> entries) =>
        entries.GroupBy(e => e.RuleName)
               .Select(g => new RuleStat(g.Key,
                   g.Count(e => IsTaken(e.Outcome)),
                   g.Count(e => e.Outcome == BreakOutcome.Skipped),
                   g.Count(e => e.Outcome == BreakOutcome.Postponed)))
               .OrderBy(r => r.Rule, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>Consecutive days ending today (or yesterday when today has nothing yet) with at least one break taken and none skipped.</summary>
    public static int CurrentStreakDays(IEnumerable<HistoryEntry> entries, DateOnly today)
    {
        var byDay = entries.GroupBy(e => DateOnly.FromDateTime(e.Time)).ToDictionary(g => g.Key, g => g.ToList());
        static bool Good(List<HistoryEntry>? es) =>
            es is not null && es.Any(e => IsTaken(e.Outcome)) && es.All(e => e.Outcome != BreakOutcome.Skipped);

        var day = byDay.ContainsKey(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (Good(byDay.GetValueOrDefault(day)))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
