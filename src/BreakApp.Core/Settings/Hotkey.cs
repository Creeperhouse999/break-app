namespace BreakApp.Core.Settings;

[Flags]
public enum HotkeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

/// <summary>
/// A key combination like "Ctrl+Shift+Alt+B". Needs at least one modifier and exactly one known key.
/// Key names follow WPF's Key enum ("D1", "F5", "NumPad7", "Space", "OemPlus"…) so the shell can map them safely.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, string> KnownKeys = BuildKnownKeys();

    private static Dictionary<string, string> BuildKnownKeys()
    {
        var names = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++) names.Add(c.ToString());
        for (var d = 0; d <= 9; d++) { names.Add($"D{d}"); names.Add($"NumPad{d}"); }
        for (var f = 1; f <= 24; f++) names.Add($"F{f}");
        names.AddRange([
            "Space", "Enter", "Return", "Tab", "Escape", "Back", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
            "Up", "Down", "Left", "Right", "Pause", "Scroll", "Snapshot", "Multiply", "Add", "Subtract", "Divide", "Decimal",
            "OemPlus", "OemMinus", "OemComma", "OemPeriod", "OemSemicolon", "OemQuestion", "OemTilde", "OemOpenBrackets",
            "OemPipe", "OemCloseBrackets", "OemQuotes", "OemBackslash",
        ]);
        var map = names.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);
        for (var d = 0; d <= 9; d++) map[d.ToString()] = $"D{d}";  // "1" → "D1"
        map["Esc"] = "Escape";
        return map;
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return false;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null || !KnownKeys.TryGetValue(part, out var canonical)) return false;
                    key = canonical;
                    break;
            }
        }
        if (key is null || mods == HotkeyModifiers.None) return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }

    /// <summary>Canonical text, or null when <paramref name="text"/> is not a valid hotkey.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var h) ? h.ToString() : null;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
