namespace BreakApp.Core.Settings;

[Flags]
public enum HotkeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

/// <summary>A key combination like "Ctrl+Shift+Alt+B". Needs at least one modifier and exactly one key.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
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
                    if (key is not null || !part.All(char.IsLetterOrDigit)) return false;
                    if (part.Length > 1 && IsModifierLike(part)) return false;
                    // short names upper-case ("b" → "B", "f12" → "F12", "d1" → "D1"); long ones capitalised ("space" → "Space")
                    key = part.Length <= 3 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..];
                    break;
            }
        }
        if (key is null || mods == HotkeyModifiers.None) return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }

    private static bool IsModifierLike(string s) => s.Equals("hyper", StringComparison.OrdinalIgnoreCase) || s.Equals("meta", StringComparison.OrdinalIgnoreCase)
                                                    || s.Equals("cmd", StringComparison.OrdinalIgnoreCase) || s.Equals("super", StringComparison.OrdinalIgnoreCase);

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
