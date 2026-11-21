using System.Text.Json;

namespace BreakApp.Core.Updates;

/// <summary>The latest GitHub release, as far as the updater cares.</summary>
public sealed record ReleaseInfo(string Version, string ExeUrl, long ExeSize, string PageUrl, string Notes)
{
    public const string Repo = "Creeperhouse999/break-app";
    public const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    private const string TrustedDownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    /// <summary>"v1.2.0" / "1.2" → System.Version, or null.</summary>
    public static System.Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        return System.Version.TryParse(t.Contains('.') ? t : t + ".0", out var v) ? v : null;
    }

    /// <summary>True when <paramref name="latestTag"/> is a higher version than <paramref name="current"/> (numeric, not text, comparison).</summary>
    public static bool IsNewer(string? latestTag, string? current)
    {
        var latest = ParseVersion(latestTag);
        var now = ParseVersion(current);
        if (latest is null || now is null) return false;
        static System.Version Norm(System.Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        return Norm(latest) > Norm(now);
    }

    /// <summary>
    /// Reads GitHub's "latest release" JSON. Returns null for drafts, pre-releases, missing BreakApp.exe,
    /// download URLs outside this repo's releases, or anything unreadable.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            var tag = Str(root, "tag_name");
            var version = ParseVersion(tag);
            if (version is null || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(Str(a, "name"), "BreakApp.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var url = Str(a, "browser_download_url");
                if (url is null || !url.StartsWith(TrustedDownloadPrefix, StringComparison.OrdinalIgnoreCase)) return null;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                return new ReleaseInfo(tag!.Trim().TrimStart('v', 'V'), url, size, Str(root, "html_url") ?? "", Str(root, "body") ?? "");
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
