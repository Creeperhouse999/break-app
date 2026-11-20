using System.Globalization;
using System.Text.RegularExpressions;

namespace BreakApp.Core.Settings;

public static partial class Templates
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Replaces {name} placeholders (case-insensitive). Unknown placeholders stay as typed.</summary>
    public static string Render(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return "";
        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return Placeholder().Replace(template, m => lookup.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}

/// <summary>Small helpers for "#RRGGBB" colour strings (Core stays free of WPF).</summary>
public static class ColorHex
{
    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        r = (byte)(v >> 16); g = (byte)(v >> 8); b = (byte)v;
        return true;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Normalised "#RRGGBB", or null when invalid.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var r, out var g, out var b) ? ToHex(r, g, b) : null;

    /// <summary>Mix toward white by <paramref name="amount"/> (0..1).</summary>
    public static string Lighten(string hex, double amount)
    {
        if (!TryParse(hex, out var r, out var g, out var b)) return hex;
        byte Mix(byte c) => (byte)Math.Round(c + (255 - c) * Math.Clamp(amount, 0, 1));
        return ToHex(Mix(r), Mix(g), Mix(b));
    }

    /// <summary>Mix toward black by <paramref name="amount"/> (0..1).</summary>
    public static string Darken(string hex, double amount)
    {
        if (!TryParse(hex, out var r, out var g, out var b)) return hex;
        byte Mix(byte c) => (byte)Math.Round(c * (1 - Math.Clamp(amount, 0, 1)));
        return ToHex(Mix(r), Mix(g), Mix(b));
    }
}
