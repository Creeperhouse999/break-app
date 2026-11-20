using System.Windows;
using System.Windows.Media;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using Microsoft.Win32;

namespace BreakApp.Theme;

/// <summary>Accent palette ported from HiSync's toast.html (a1, a2, a3, accent text light/dark).</summary>
public sealed record AccentPalette(Color A1, Color A2, Color A3, Color TextLight, Color TextDark);

/// <summary>
/// Builds the app's brushes (HiSync look) in code and swaps them live. Every UI element uses DynamicResource keys
/// like "Text1Brush" so a theme or accent change applies everywhere immediately.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;

    public static bool IsDark { get; private set; }
    public static AccentPalette Palette { get; private set; } = PaletteFor(Accent.Blue);
    public static double FontScale { get; private set; } = 1;
    public static event Action? Changed;

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

    public static AccentPalette PaletteFor(Accent accent) => accent switch
    {
        Accent.Indigo => new(Hex("#4f46e5"), Hex("#6366f1"), Hex("#818cf8"), Hex("#4338ca"), Hex("#a5b4fc")),
        Accent.Teal => new(Hex("#0d9488"), Hex("#14b8a6"), Hex("#2dd4bf"), Hex("#0f766e"), Hex("#5eead4")),
        Accent.Violet => new(Hex("#7c3aed"), Hex("#8b5cf6"), Hex("#a78bfa"), Hex("#6d28d9"), Hex("#c4b5fd")),
        Accent.Rose => new(Hex("#e11d48"), Hex("#f43f5e"), Hex("#fb7185"), Hex("#be123c"), Hex("#fda4af")),
        _ => new(Hex("#2563eb"), Hex("#3b82f6"), Hex("#60a5fa"), Hex("#1d4ed8"), Hex("#93c5fd")),
    };

    public static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static bool ResolveDark(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemUsesDark(),
    };

    /// <summary>Apply the global appearance to the whole app.</summary>
    public static void Apply(AppearanceSettings a)
    {
        IsDark = ResolveDark(a.Theme);
        FontScale = a.FontScale;
        Palette = PaletteFor(a);
        var dict = CreateDictionary(IsDark, Palette, a);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Changed?.Invoke();
    }

    /// <summary>A dictionary for one window that overrides the global look (per-rule appearance override).</summary>
    public static ResourceDictionary? OverrideFor(AppearanceSettings global, AppearanceOverride? o)
    {
        if (o is null || (o.Accent is null && o.Theme is null)) return null;
        var palette = o.Accent is { } acc ? PaletteFor(acc) : PaletteFor(global);
        return CreateDictionary(ResolveDark(o.Theme ?? global.Theme), palette, global);
    }

    private static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static SolidColorBrush B(string hex) => B(Hex(hex));

    private static LinearGradientBrush Grad(Color a, Color b, Point start, Point end)
    {
        var g = new LinearGradientBrush(a, b, start, end);
        g.Freeze();
        return g;
    }

    /// <summary>Custom hex accent wins over the preset accent.</summary>
    public static AccentPalette PaletteFor(AppearanceSettings a) => a.CustomAccent is { } hex ? FromHex(hex) : PaletteFor(a.Accent);

    private static AccentPalette FromHex(string hex) => new(Hex(hex), Hex(ColorHex.Lighten(hex, 0.15)), Hex(ColorHex.Lighten(hex, 0.35)),
        Hex(ColorHex.Darken(hex, 0.2)), Hex(ColorHex.Lighten(hex, 0.45)));

    public static ResourceDictionary CreateDictionary(bool dark, AccentPalette p, AppearanceSettings? a = null)
    {
        var d = new ResourceDictionary
        {
            ["Accent1Color"] = p.A1,
            ["Accent2Color"] = p.A2,
            ["Accent3Color"] = p.A3,
            ["Accent1Brush"] = B(p.A1),
            ["Accent2Brush"] = B(p.A2),
            ["Accent3Brush"] = B(p.A3),
            ["AccentGradientBrush"] = Grad(p.A1, p.A2, new Point(0, 0), new Point(1, 1)),
            ["AccentGlowColor"] = WithAlpha(p.A1, 0.35),
            ["OnAccentBrush"] = B(Colors.White),
        };

        if (!dark)
        {
            d["CardBackgroundBrush"] = Grad(Hex("#FFFFFFFF"), Hex("#FFEFF6FF"), new Point(0.2, 0), new Point(0.8, 1));
            d["WindowBackgroundBrush"] = B("#F3F8FF");
            d["SidebarBrush"] = B("#E8F0FC");
            d["SurfaceBrush"] = B("#FFFFFF");
            d["InputBackgroundBrush"] = B("#FFFFFF");
            d["CardBorderBrush"] = B("#F2FFFFFF");
            d["RingBrush"] = B("#1F1E40AF");
            d["DividerBrush"] = B("#1A1E40AF");
            d["ShadowColor"] = Hex("#1E40AF");
            d["Text1Brush"] = B("#0F2340");
            d["Text2Brush"] = B("#36507A");
            d["Text3Brush"] = B("#5A6F94");
            d["ButtonBackgroundBrush"] = B("#E6FFFFFF");
            d["ButtonBorderBrush"] = B("#D5E3FA");
            d["AccentTextBrush"] = B(p.TextLight);
            d["HoverBrush"] = B(WithAlpha(p.A2, 0.10));
            d["SelectedBrush"] = B(WithAlpha(p.A2, 0.16));
            d["TrackBrush"] = B(WithAlpha(p.A2, 0.14));
            d["SuccessBrush"] = B("#16A34A"); d["SuccessBackgroundBrush"] = B("#DCFCE7");
            d["InfoBrush"] = B(p.A1); d["InfoBackgroundBrush"] = B(WithAlpha(p.A2, 0.16));
            d["WarningBrush"] = B("#D97706"); d["WarningBackgroundBrush"] = B("#FEF3C7");
            d["ErrorBrush"] = B("#DC2626"); d["ErrorBackgroundBrush"] = B("#FEE2E2");
            d["BreakOverlayColor"] = Hex("#EEF4FF");
        }
        else
        {
            d["CardBackgroundBrush"] = Grad(Hex("#FF142248"), Hex("#FF0B1838"), new Point(0.2, 0), new Point(0.8, 1));
            d["WindowBackgroundBrush"] = B("#0F1D40");
            d["SidebarBrush"] = B("#0B1734");
            d["SurfaceBrush"] = B("#15264F");
            d["InputBackgroundBrush"] = B("#1A2D5C");
            d["CardBorderBrush"] = B("#2E94B4FF");
            d["RingBrush"] = B("#1F94B4FF");
            d["DividerBrush"] = B("#2494B4FF");
            d["ShadowColor"] = Colors.Black;
            d["Text1Brush"] = B("#E6EEFF");
            d["Text2Brush"] = B("#B9CBEF");
            d["Text3Brush"] = B("#93A8D0");
            d["ButtonBackgroundBrush"] = B("#B31E3264");
            d["ButtonBorderBrush"] = B("#3894B4FF");
            d["AccentTextBrush"] = B(p.TextDark);
            d["HoverBrush"] = B(WithAlpha(p.A2, 0.14));
            d["SelectedBrush"] = B(WithAlpha(p.A2, 0.22));
            d["TrackBrush"] = B("#2494B4FF");
            d["SuccessBrush"] = B("#4ADE80"); d["SuccessBackgroundBrush"] = B("#2922C55E");
            d["InfoBrush"] = B(p.A3); d["InfoBackgroundBrush"] = B(WithAlpha(p.A2, 0.20));
            d["WarningBrush"] = B("#FBBF24"); d["WarningBackgroundBrush"] = B("#2BF59E0B");
            d["ErrorBrush"] = B("#F87171"); d["ErrorBackgroundBrush"] = B("#2EEF4444");
            d["BreakOverlayColor"] = Hex("#0B1838");
        }

        // user colour overrides
        d["BreakBackgroundBrush"] = a?.BreakBackgroundColor is { } bb ? B(bb) : d["WindowBackgroundBrush"];
        d["BreakTextBrush"] = a?.BreakTextColor is { } bt ? B(bt) : d["Text1Brush"];
        if (a?.NotificationBackgroundColor is { } nb) d["CardBackgroundBrush"] = B(WithAlpha(Hex(nb), 0.98));
        return d;
    }
}
