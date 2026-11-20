using System.Globalization;
using System.Text;
using System.Text.Json;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;

namespace BreakApp.Core.History;

public sealed record HistoryEntry(DateTime Time, Guid RuleId, string RuleName, BreakOutcome Outcome, TimeSpan ActualDuration);

/// <summary>Append-only JSON-lines log of every break event.</summary>
public sealed class HistoryLog(string path)
{
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Compact = new(SettingsStore.Json) { WriteIndented = false };

    public static string DefaultPath => Path.Combine(SettingsStore.AppDataDir, "history.jsonl");

    public void Append(HistoryEntry e)
    {
        var line = JsonSerializer.Serialize(e, Compact);
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path, line + "\n");
        }
    }

    public IReadOnlyList<HistoryEntry> ReadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(path)) return [];
            var list = new List<HistoryEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<HistoryEntry>(line, SettingsStore.Json) is { } e) list.Add(e);
                }
                catch (JsonException) { /* skip damaged line */ }
            }
            return list;
        }
    }

    public void Clear()
    {
        lock (_lock) { if (File.Exists(path)) File.Delete(path); }
    }

    public void ExportCsv(string csvPath)
    {
        var sb = new StringBuilder("Time,Rule,Outcome,DurationSeconds\n");
        foreach (var e in ReadAll())
        {
            sb.Append(e.Time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append('"').Append(e.RuleName.Replace("\"", "\"\"")).Append("\",")
              .Append(e.Outcome).Append(',')
              .Append(((int)Math.Round(e.ActualDuration.TotalSeconds)).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        File.WriteAllText(csvPath, sb.ToString());
    }
}
