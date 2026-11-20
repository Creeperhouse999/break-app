using System.Text.Json;
using System.Text.Json.Serialization;
using BreakApp.Core.Models;

namespace BreakApp.Core.Settings;

public static class SettingsStore
{
    public static readonly string AppDataDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreakApp");

    public static string DefaultPath => System.IO.Path.Combine(AppDataDir, "settings.json");

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads settings. Missing file → defaults. Unreadable file → defaults, and the bad file is kept as settings.bad.json.</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return AppSettings.CreateDefault();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json)
                    ?? throw new JsonException("empty settings");
            return SettingsValidator.Normalize(s);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path) ?? ".";
                File.Copy(path, System.IO.Path.Combine(dir, "settings.bad.json"), overwrite: true);
            }
            catch (IOException) { /* best effort */ }
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>Atomic save: write a temp file, then replace.</summary>
    public static void Save(AppSettings s, string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Import throws on invalid files so the UI can show the error (unlike Load).</summary>
    public static AppSettings Import(string path)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json)
                ?? throw new JsonException("The file does not contain settings.");
        return SettingsValidator.Normalize(s);
    }

    public static void Export(AppSettings s, string path) => File.WriteAllText(path, JsonSerializer.Serialize(s, Json));

    public static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json), Json)!;

    public static BreakRule CloneRule(BreakRule r) =>
        JsonSerializer.Deserialize<BreakRule>(JsonSerializer.Serialize(r, Json), Json)!;
}
