using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Services;

namespace BreakApp.Breaks;

/// <summary>Break screen. Fullscreen: one per monitor, covers it completely. Window: a centered card.</summary>
public partial class BreakWindow : Window
{
    private readonly bool _fullscreen;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly CountdownFormat _format;
    private readonly bool _canSkip;

    public event Action? SkipClicked;
    public event Action? PostponeClicked;

    /// <param name="skipLabel">null = no skip button (and Esc does nothing).</param>
    /// <param name="postponeLabel">null = no postpone button.</param>
    public BreakWindow(BreakRule rule, string? mergedText, string message, bool fullscreen,
        System.Drawing.Rectangle bounds, AppearanceSettings a, ResourceDictionary? themeOverride,
        string? skipLabel, string? postponeLabel, string hint)
    {
        InitializeComponent();
        if (themeOverride is not null) Resources.MergedDictionaries.Add(themeOverride);
        _fullscreen = fullscreen;
        _bounds = bounds;
        _format = a.Countdown;
        var canSkip = _canSkip = skipLabel is not null;
        FontSize = 13 * a.FontScale;

        RuleName.Text = rule.Name;
        MessageText.Text = message;
        MergedText.Text = mergedText ?? "";
        MergedText.Visibility = string.IsNullOrEmpty(mergedText) ? Visibility.Collapsed : Visibility.Visible;
        HintText.Text = hint;
        HintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;

        SkipButton.Content = skipLabel;
        SkipButton.Visibility = canSkip ? Visibility.Visible : Visibility.Collapsed;
        PostponeButton.Visibility = postponeLabel is null ? Visibility.Collapsed : Visibility.Visible;
        PostponeButton.Content = postponeLabel;
        PostponeButton.SetResourceReference(StyleProperty, "PrimaryButton");
        Buttons.Visibility = canSkip || postponeLabel is not null ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Click += (_, _) => SkipClicked?.Invoke();
        PostponeButton.Click += (_, _) => PostponeClicked?.Invoke();

        RingHost.Visibility = _format == CountdownFormat.Ring ? Visibility.Visible : Visibility.Collapsed;
        BigText.Visibility = _format == CountdownFormat.MinSec ? Visibility.Visible : Visibility.Collapsed;
        BarHost.Visibility = _format == CountdownFormat.Bar ? Visibility.Visible : Visibility.Collapsed;

        var opacity = rule.Appearance?.Opacity ?? a.Opacity;
        var image = rule.Appearance?.BackgroundImage ?? a.BackgroundImage;
        if (fullscreen)
        {
            Backdrop.Opacity = opacity;
            if (!string.IsNullOrWhiteSpace(image) && File.Exists(image))
            {
                try { BgImage.Source = new BitmapImage(new Uri(image, UriKind.Absolute)); }
                catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException) { App.Log("background image failed: " + e.Message); }
            }
        }
        else
        {
            Backdrop.Visibility = Visibility.Collapsed;
            Card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            Card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            Card.BorderThickness = new Thickness(1);
            Card.Margin = new Thickness(30);
            Card.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.28, Color = Colors.Black };
            RingHost.Width = RingHost.Height = 180;
            RingText.FontSize = 40;
            MessageText.FontSize = 20;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST);
            if (_fullscreen)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, Native.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) =>
        {
            if (a.ReduceMotion) Root.Opacity = 1;
            else Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.35)));
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _canSkip) SkipClicked?.Invoke(); };
    }

    public void BringToTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Native.MakeTopmost(hwnd);
    }

    public void Update(TimeSpan left, TimeSpan total)
    {
        var text = Format.Countdown(left);
        var f = total.TotalMilliseconds > 0 ? Math.Clamp(left.TotalMilliseconds / total.TotalMilliseconds, 0, 1) : 0;
        switch (_format)
        {
            case CountdownFormat.Ring:
                RingText.Text = text;
                RingArc.Data = Arc(RingHost.Width, 12, f);
                break;
            case CountdownFormat.Bar:
                BarText.Text = text;
                BarFill.Width = BarHost.Width * f;
                break;
            default:
                BigText.Text = text;
                break;
        }
    }

    /// <summary>Clockwise arc from 12 o'clock covering fraction f of the circle.</summary>
    internal static Geometry Arc(double size, double stroke, double f)
    {
        // match the track Ellipse: 6 px margin, stroke drawn inside its bounds
        var r = size / 2 - 6 - stroke / 2;
        var c = new Point(size / 2, size / 2);
        if (f >= 0.9995) return new EllipseGeometry(c, r, r);
        if (f <= 0.0005) return Geometry.Empty;
        var angle = 2 * Math.PI * f;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }
}
