using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Services;
using BreakApp.Theme;

namespace BreakApp.Notifications;

/// <summary>
/// One notification card (port of HiSync's toast.html): slides in from the chosen corner, never takes focus,
/// hidden from the taskbar and Alt-Tab, optional flash, life bar that pauses on hover.
/// </summary>
public partial class NotificationWindow : Window
{
    private const double Gap = 12;            // distance from the work-area edge to the card
    private const double ShadowMargin = 24;   // Root margin left/right (room for shadow + pulse)
    private const double MarginTop = 18, MarginBottom = 24;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Corner _corner;
    private readonly bool _reduceMotion;
    private NotificationRequest _request;
    private Action<string>? _callback;
    private TimeSpan _left;
    private DateTime _lastFrame;
    private bool _hover, _closing, _reported;

    public string Id => _request.Id;
    public NotificationRequest Request => _request;
    public Action<string>? Callback => _callback;
    public event Action<NotificationWindow>? Finished;

    public NotificationWindow(NotificationRequest request, Action<string>? callback, AppearanceSettings appearance)
    {
        InitializeComponent();
        _request = request;
        _callback = callback;
        _corner = appearance.NotificationCorner;
        _reduceMotion = appearance.ReduceMotion;
        FontSize = 13 * appearance.FontScale;
        BrandText.Text = App.Coordinator?.Settings.Texts.BrandName ?? "Break App";
        if (IsLeft) Slide.X = -46;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(hwnd, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST);
        };
        SizeChanged += (_, _) => Position();
        Loaded += (_, _) => { Position(); AnimateIn(); };
        MouseEnter += (_, _) => _hover = true;
        MouseLeave += (_, _) => _hover = false;
        CloseButton.Click += (_, _) => Dismiss("closed");
        KeyDown += OnKey;
        _timer.Tick += (_, _) => OnFrame();
        Apply(request);
    }

    private bool IsLeft => _corner is Corner.BottomLeft or Corner.TopLeft;
    private bool IsTop => _corner is Corner.TopLeft or Corner.TopRight;

    /// <summary>Replace the content in place (same notification id).</summary>
    public void Update(NotificationRequest r, Action<string>? callback)
    {
        _request = r;
        _callback = callback;
        Apply(r);
    }

    private void Apply(NotificationRequest r)
    {
        var (kindKey, glyph) = r.Kind switch
        {
            NotificationKind.Success => ("Success", ""),
            NotificationKind.Warning => ("Warning", r.Remaining is null ? "" : ""),
            NotificationKind.Error => ("Error", ""),
            _ => ("Info", r.Remaining is null ? "" : ""),
        };
        SetResourceReference(KindBrushProperty, kindKey + "Brush");
        Stripe.SetResourceReference(Border.BackgroundProperty, kindKey + "Brush");
        LifeFill.SetResourceReference(Border.BackgroundProperty, kindKey + "Brush");
        IconChip.SetResourceReference(Border.BackgroundProperty, kindKey + "BackgroundBrush");
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, kindKey + "Brush");
        IconGlyph.Text = glyph;

        TitleText.Text = r.Title;
        BodyText.Text = r.Body;
        BodyText.Visibility = string.IsNullOrWhiteSpace(r.Body) ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = r.Closable ? Visibility.Visible : Visibility.Collapsed;

        Actions.Children.Clear();
        foreach (var a in r.Actions.Take(3))
        {
            var b = new Button
            {
                Content = a.Label,
                Margin = new Thickness(0, 0, 6, 0),
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                IsDefault = a.Primary,
            };
            b.SetResourceReference(StyleProperty, a.Primary ? "PrimaryButton" : "BaseButton");
            var id = a.Id;
            b.Click += (_, _) => Dismiss(id);
            Actions.Children.Add(b);
        }
        Actions.Visibility = Actions.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _left = r.Duration;
        _lastFrame = DateTime.UtcNow;
        var timed = r.Remaining is not null || r.Duration > TimeSpan.Zero;
        LifeBar.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        CountdownText.Visibility = r.Remaining is not null ? Visibility.Visible : Visibility.Collapsed;
        OnFrame();
        StartFlash(r.Flash);
        _timer.Start();
    }

    // Kind colour, used by the flash glow.
    public static readonly DependencyProperty KindBrushProperty =
        DependencyProperty.Register(nameof(KindBrush), typeof(Brush), typeof(NotificationWindow));
    public Brush? KindBrush { get => (Brush?)GetValue(KindBrushProperty); set => SetValue(KindBrushProperty, value); }

    private void StartFlash(FlashSettings? flash)
    {
        Glow.BeginAnimation(OpacityProperty, null);
        Glow.Opacity = 0;
        Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Pulse.ScaleX = Pulse.ScaleY = 1;
        if (flash is null || !flash.Enabled || _reduceMotion) return;

        var half = TimeSpan.FromSeconds(1 / (2 * Math.Max(0.5, flash.SpeedHz)));
        var repeat = flash.Count <= 0 ? RepeatBehavior.Forever : new RepeatBehavior(flash.Count);
        DoubleAnimation Wave(double from, double to) => new(from, to, half)
        {
            AutoReverse = true,
            RepeatBehavior = repeat,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        if (flash.Style != FlashStyle.Pulse)
        {
            var color = flash.Color is { Length: > 0 } hex ? SafeColor(hex) : ThemeManager.Palette.A2;
            Glow.BorderBrush = new SolidColorBrush(color);
            Glow.Background = new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B));
            Glow.BeginAnimation(OpacityProperty, Wave(0, 1));
        }

        if (flash.Style != FlashStyle.Glow)
        {
            // The card grows and shrinks around its centre; the window margin leaves room for up to +10 %.
            var scale = 1 + Math.Clamp(flash.PulseAmount, 1, 10) / 100;
            Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, Wave(1, scale));
            Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, Wave(1, scale));
        }
    }

    private static Color SafeColor(string hex)
    {
        try { return ThemeManager.Hex(hex); }
        catch (FormatException) { return ThemeManager.Palette.A2; }
    }

    private void OnFrame()
    {
        var now = DateTime.UtcNow;
        var dt = now - _lastFrame;
        _lastFrame = now;
        if (_closing) return;

        if (_request.Remaining is { } remaining)
        {
            var left = remaining();
            CountdownText.Text = Format.Countdown(left);
            var total = _request.Total > TimeSpan.Zero ? _request.Total : TimeSpan.FromSeconds(1);
            LifeScale.ScaleX = Math.Clamp(left.TotalMilliseconds / total.TotalMilliseconds, 0, 1);
            if (left <= TimeSpan.Zero) Dismiss("timeout");
            return;
        }

        if (_request.Duration <= TimeSpan.Zero) return;
        if (!_hover) _left -= dt;
        LifeScale.ScaleX = Math.Clamp(_left.TotalMilliseconds / _request.Duration.TotalMilliseconds, 0, 1);
        if (_left <= TimeSpan.Zero) Dismiss("timeout");
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _request.Closable) Dismiss("closed");
        else if (e.Key == Key.Enter && _request.Actions.FirstOrDefault(a => a.Primary) is { } p) Dismiss(p.Id);
    }

    private void Position()
    {
        var wa = SystemParameters.WorkArea;
        var cardW = Width - 2 * ShadowMargin;
        Left = IsLeft ? wa.Left + Gap - ShadowMargin : wa.Right - Gap - cardW - ShadowMargin;
        Top = IsTop ? wa.Top + Gap - MarginTop : wa.Bottom - Gap - ActualHeight + MarginBottom;
    }

    private void AnimateIn()
    {
        if (_reduceMotion)
        {
            Root.Opacity = 1; Slide.X = 0; IconScale.ScaleX = IconScale.ScaleY = 1;
            return;
        }
        var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.48)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.3)));
        var pop = new DoubleAnimation(1, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.12), EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>Report the result once and animate out. reason: action id, "closed", "timeout" or "hidden".</summary>
    public void Dismiss(string reason)
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        if (!_reported && reason != "hidden")
        {
            _reported = true;
            try { _callback?.Invoke(reason); }
            catch (Exception e) { App.Log("notification callback failed: " + e); }
        }
        if (_reduceMotion) { CloseNow(); return; }
        var dur = TimeSpan.FromSeconds(0.26);
        var fade = new DoubleAnimation(0, dur) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => CloseNow();
        Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(IsLeft ? -26 : 26, dur));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void CloseNow()
    {
        Finished?.Invoke(this);
        Close();
    }
}
