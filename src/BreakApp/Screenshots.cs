using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakApp.Breaks;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;
using BreakApp.Notifications;
using BreakApp.Services;
using BreakApp.SettingsUI;
using BreakApp.Theme;

namespace BreakApp;

/// <summary>
/// `--render-screenshots &lt;dir&gt;`: renders README screenshots off-screen (nothing is shown, the user's desktop is never captured),
/// using clean default settings.
/// </summary>
internal static class Screenshots
{
    private const double Scale = 1.5;

    public static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(Path.GetTempPath(), "BreakAppShots_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var settings = AppSettings.CreateDefault();
        settings.Appearance.Theme = ThemeMode.Dark;
        settings.General.SetupDone = true;
        ThemeManager.Apply(settings.Appearance);
        var c = new BreakCoordinator(settings, Path.Combine(tmp, "s.json"), new SystemClock(), 1, Path.Combine(tmp, "h.jsonl"));

        // notification cards
        var warn = new NotificationWindow(new NotificationRequest("w", NotificationKind.Info, "Stretch in 30 s", "5 min break · fullscreen",
            [new("now", "Start now", true), new("postpone", "Postpone 5 min"), new("skip", "Skip")], TimeSpan.FromSeconds(8)), null, settings.Appearance);
        Save(Card(warn, glow: false), Path.Combine(dir, "notification-warning.png"));
        var end = DateTime.Now.AddSeconds(14);
        var brk = new NotificationWindow(new NotificationRequest("b", NotificationKind.Warning, "Short break", "Look away from the screen for a bit.",
            [new("skip", "Skip"), new("postpone", "Postpone 5 min")], TimeSpan.Zero, Remaining: () => end - DateTime.Now, Total: TimeSpan.FromSeconds(20)), null, settings.Appearance);
        Save(Card(brk, glow: true), Path.Combine(dir, "notification-break.png"));

        // fullscreen break
        var rule = RuleTemplates.LongBreak();
        var bw = new BreakWindow(rule, "Also covers: Short break", rule.Messages[0], true, default, settings.Appearance, null, "Skip break", "Postpone 5 min", "Press Esc to skip.");
        bw.Update(TimeSpan.FromSeconds(192), TimeSpan.FromMinutes(5));
        Save(Detach(bw, 1600, 900, opaqueBackdrop: true), Path.Combine(dir, "break-fullscreen.png"));

        // settings window pages
        foreach (var (page, file) in new[] { ("Rules", "settings-rules.png"), ("Appearance", "settings-appearance.png"), ("Texts", "settings-texts.png") })
        {
            var sw = new SettingsWindow(c, page);
            Save(Detach(sw, 1040, 740), Path.Combine(dir, file));
        }
        var editor = new SettingsWindow(c, "Rules");
        editor.ShowRule(settings.Rules[1].Id);
        Save(Detach(editor, 1040, 740), Path.Combine(dir, "settings-rule-editor.png"));

        // setup guide
        var guide = new SetupGuideWindow(c);
        guide.GoToStep(3);
        Save(Detach(guide, 760, 700), Path.Combine(dir, "setup-guide.png"));

        // light theme
        settings.Appearance.Theme = ThemeMode.Light;
        ThemeManager.Apply(settings.Appearance);
        Save(Detach(new SettingsWindow(c, "Pausing & Meetings"), 1040, 740), Path.Combine(dir, "settings-light.png"));

        c.Dispose();
        try { Directory.Delete(tmp, true); } catch { }
    }

    /// <summary>The card of a notification window on a neutral backdrop.</summary>
    private static FrameworkElement Card(NotificationWindow w, bool glow)
    {
        var root = (FrameworkElement)w.Content;
        w.Content = null;
        root.Opacity = 1;
        root.RenderTransform = Transform.Identity;
        if (glow && root is Grid g && g.Children.OfType<Border>().FirstOrDefault(b => b.Name == "Glow") is { } glowBorder)
        {
            glowBorder.BorderBrush = new SolidColorBrush(ThemeManager.Palette.A2);
            glowBorder.Background = new SolidColorBrush(Color.FromArgb(46, ThemeManager.Palette.A2.R, ThemeManager.Palette.A2.G, ThemeManager.Palette.A2.B));
            glowBorder.Opacity = 0.9;
        }
        var host = new Border { Child = root, Width = 428, Padding = new Thickness(10) };
        host.Background = new LinearGradientBrush(ThemeManager.Hex("#1B2A4E"), ThemeManager.Hex("#0B1226"), 90);
        return host;
    }

    private static FrameworkElement Detach(Window w, double width, double height, bool opaqueBackdrop = false)
    {
        var root = (FrameworkElement)w.Content;
        w.Content = null;
        root.Opacity = 1;
        var host = new Border { Child = root, Width = width, Height = height };
        host.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        if (opaqueBackdrop) host.Background = new SolidColorBrush(ThemeManager.Hex("#0B1226"));
        return host;
    }

    private static void Save(FrameworkElement e, string path)
    {
        e.Measure(new Size(e.Width > 0 ? e.Width : double.PositiveInfinity, e.Height > 0 ? e.Height : double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        var w = (int)Math.Ceiling(e.ActualWidth * Scale);
        var h = (int)Math.Ceiling(e.ActualHeight * Scale);
        var bmp = new RenderTargetBitmap(w, h, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        App.Log("screenshot: " + path);
    }
}
