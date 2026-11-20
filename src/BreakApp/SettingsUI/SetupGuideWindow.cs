using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Notifications;
using BreakApp.Services;
using BreakApp.Theme;

namespace BreakApp.SettingsUI;

/// <summary>
/// First-run setup guide: starting preset → look → break times → when breaks wait → finish.
/// Edits a copy; saves on Finish. Reachable later from the tray.
/// </summary>
public sealed class SetupGuideWindow : Window
{
    private static readonly string[] StepTitles = ["Welcome", "Make it yours", "Your breaks", "When breaks wait", "All set"];

    private readonly BreakCoordinator _c;
    private readonly SettingsContext _ctx;
    private readonly ContentControl _body = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _back = new() { Content = "Back", Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _next = new() { Height = 34, Padding = new Thickness(22, 0, 22, 0) };
    private int _step;
    private bool _finished;

    public event Action? OpenSettingsRequested;

    public SetupGuideWindow(BreakCoordinator c)
    {
        _c = c;
        var edit = SettingsStore.Clone(c.Settings);
        _ctx = new SettingsContext(edit, c);
        _ctx.AppearanceChanged += () => ThemeManager.Apply(_ctx.Edit.Appearance);
        _ctx.Rebuild = () => ShowStep(_step);

        Title = "Break App · Setup guide";
        Width = 760;
        Height = 700;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico"));
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SourceInitialized += (_, _) => TitleBar();
        ThemeManager.Changed += TitleBar;
        Closed += (_, _) => ThemeManager.Changed -= TitleBar;
        Content = Layout();
        ShowStep(0);
    }

    private void TitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Native.SetDwmInt(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ThemeManager.IsDark ? 1 : 0);
    }

    private UIElement Layout()
    {
        var root = new DockPanel();
        var top = new StackPanel { Margin = new Thickness(36, 22, 36, 4) };
        top.Children.Add(_dots);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var footer = new Grid { Margin = new Thickness(36, 12, 36, 20) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var skip = new Button { Content = "Skip setup", HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Keep the defaults. You can open this guide again from the tray icon." };
        skip.SetResourceReference(StyleProperty, "GhostButton");
        skip.Click += (_, _) => Finish(save: false);
        footer.Children.Add(skip);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        _back.Height = 34;
        _back.Padding = new Thickness(18, 0, 18, 0);
        _back.Click += (_, _) => ShowStep(_step - 1);
        _next.SetResourceReference(StyleProperty, "PrimaryButton");
        _next.Click += (_, _) => { if (_step < StepTitles.Length - 1) ShowStep(_step + 1); else Finish(save: true); };
        nav.Children.Add(_back);
        nav.Children.Add(_next);
        Grid.SetColumn(nav, 1);
        footer.Children.Add(nav);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(36, 6, 36, 6) });
        return root;
    }

    public void GoToStep(int step) => ShowStep(step);
    internal void TestFinish() => Finish(save: true);
    internal void TestCloseWithoutSaving() { _finished = true; Close(); }
    internal SettingsContext TestContext => _ctx;
    internal FrameworkElement? TestBody => _body.Content as FrameworkElement;

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepTitles.Length - 1);
        _dots.Children.Clear();
        for (var i = 0; i < StepTitles.Length; i++)
        {
            var on = i == _step;
            var dot = new Border { Height = 6, Width = on ? 26 : 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0), ToolTip = StepTitles[i] };
            dot.SetResourceReference(Border.BackgroundProperty, i <= _step ? "Accent1Brush" : "TrackBrush");
            _dots.Children.Add(dot);
        }
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _step == StepTitles.Length - 1 ? "Finish" : "Next";

        var page = new StackPanel();
        page.Children.Add(PageBuilder.T(StepTitles[_step] == "Welcome" ? "Welcome to Break App 👋" : StepTitles[_step], "H1"));
        var body = _step switch
        {
            0 => Welcome(),
            1 => Look(),
            2 => Breaks(),
            3 => Waiting(),
            _ => Done(),
        };
        page.Children.Add(body);
        _body.Content = page;
    }

    private static TextBlock Para(string text, double top = 8) =>
        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, top, 0, 0) }.WithRef(TextBlock.ForegroundProperty, "Text2Brush");

    // ------------------------------------------------------------------ steps

    private UIElement Welcome()
    {
        var p = new StackPanel();
        p.Children.Add(Para("Break App reminds you to rest your eyes and move. Pick a starting point — you can change everything later in Settings."));
        p.Children.Add(new TextBlock { Text = "START WITH", Margin = new Thickness(2, 22, 0, 8) }.WithRef(StyleProperty, "H2"));
        foreach (var preset in Presets.All)
        {
            var selected = Presets.Diff(_ctx.Edit, preset.Apply(_ctx.Edit)).Count == 0;
            var card = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = double.NaN, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) };
            if (selected) card.SetResourceReference(BorderBrushProperty, "Accent1Brush");
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = (selected ? "✓  " : "") + preset.Name, FontSize = 15 }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
            text.Children.Add(new TextBlock { Text = preset.Description, FontSize = 12.5, FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }
                .WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
            card.Content = text;
            card.Click += (_, _) =>
            {
                var appearance = _ctx.Edit.Appearance;
                var fresh = AppSettings.CreateDefault();
                fresh.Appearance = appearance;
                _ctx.Edit = preset.Apply(fresh);
                ShowStep(0);
            };
            p.Children.Add(card);
        }
        return p;
    }

    private UIElement Look()
    {
        var b = new PageBuilder(_ctx, "Setup");
        b.Intro("Choose how breaks and notifications look. Changes preview right away.");
        var a = _ctx.Edit.Appearance;
        b.Group("Colours");
        b.Choice("Appearance.Theme", "Theme", "Light, dark, or follow Windows.", () => a.Theme, v => a.Theme = v,
            [(Core.Models.ThemeMode.Auto, "Follow Windows"), (Core.Models.ThemeMode.Light, "Light"), (Core.Models.ThemeMode.Dark, "Dark")], appearance: true);
        b.Choice("Appearance.Accent", "Accent colour", "Buttons, rings and flashes.", () => a.Accent, v => { a.Accent = v; a.CustomAccent = null; },
            [(Accent.Blue, "Blue"), (Accent.Indigo, "Indigo"), (Accent.Teal, "Teal"), (Accent.Violet, "Violet"), (Accent.Rose, "Rose")], appearance: true);
        b.Color("Appearance.CustomAccent", "Or any colour", "Overrides the accent above.", () => a.CustomAccent, v => a.CustomAccent = v, emptyLabel: "Preset");

        b.Group("Notifications");
        b.Choice("Appearance.NotificationCorner", "Corner", "Where notification cards appear.", () => a.NotificationCorner, v => a.NotificationCorner = v,
            [(Corner.BottomRight, "Bottom right"), (Corner.TopRight, "Top right"), (Corner.BottomLeft, "Bottom left"), (Corner.TopLeft, "Top left")]);
        var flash = _ctx.Edit.Rules.FirstOrDefault()?.Flash ?? new FlashSettings();
        b.Choice("Rules[].Flash.Style", "Attention effect", "How notifications grab your attention (all rules).", () => flash.Enabled ? (int)flash.Style : -1, v =>
        {
            foreach (var r in _ctx.Edit.Rules)
            {
                r.Flash.Enabled = v >= 0;
                if (v >= 0) r.Flash.Style = (FlashStyle)v;
            }
        }, [((int)FlashStyle.GlowAndPulse, "Glow + pulse"), ((int)FlashStyle.Pulse, "Pulse (grow/shrink)"), ((int)FlashStyle.Glow, "Glow"), (-1, "None")]);
        b.Add(b.Button("Show a sample notification", () => _c.Notifications.Show(new NotificationRequest("setup-sample", NotificationKind.Warning,
            "Eye rest", "Look at something 6 metres away for 20 seconds.", [new("skip", _ctx.Edit.Texts.SkipButton)], TimeSpan.FromSeconds(6),
            Flash: _ctx.Edit.Rules.FirstOrDefault()?.Flash ?? new FlashSettings()), appearanceOverride: _ctx.Edit.Appearance)));
        return b.Panel;
    }

    private UIElement Breaks()
    {
        var b = new PageBuilder(_ctx, "Setup");
        b.Intro("These are your break rules. Adjust the times and style — warnings, locks, messages and more are in Settings › Rules.");
        if (_ctx.Edit.Rules.Count == 0) _ctx.Edit.Rules.Add(RuleTemplates.EyeRest());
        foreach (var r in _ctx.Edit.Rules)
        {
            b.Group(r.Name);
            b.Duration("Rules[].Interval", "Every", "How often this break comes.", () => r.Interval, v => r.Interval = v, TimeSpan.FromSeconds(10), TimeSpan.FromHours(24));
            b.Duration("Rules[].Duration", "For", "How long the break lasts.", () => r.Duration, v => r.Duration = v, TimeSpan.FromSeconds(5), TimeSpan.FromHours(4));
            b.Choice("Rules[].Style", "Style", "Notification card, window, or fullscreen.", () => r.Style, v => r.Style = v,
                [(BreakStyle.Notification, "Notification"), (BreakStyle.Window, "Window"), (BreakStyle.Fullscreen, "Fullscreen")]);
            b.Toggle("Rules[].Enabled", "On", "", () => r.Enabled, v => r.Enabled = v);
        }
        return b.Panel;
    }

    private UIElement Waiting()
    {
        var b = new PageBuilder(_ctx, "Setup");
        var s = _ctx.Edit.Pause;
        b.Intro("Breaks can wait while you're busy. Games are never paused unless you list them below.");

        b.Group("Calls & meetings");
        b.Toggle("Pause.DetectMicCamera", "Pause while the mic or camera is in use", "Great for Google Meet, Zoom and Teams calls.",
            () => s.DetectMicCamera, v => s.DetectMicCamera = v);
        b.Add(new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), BorderThickness = new Thickness(3, 0, 0, 0),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Text = "⚠ Heads-up: voice chat while gaming (Discord, in-game chat) also uses the mic. If you often play with friends, keep this OFF — otherwise breaks would pause the whole time you're talking.",
            }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"),
        }.WithRef(Border.BackgroundProperty, "WarningBackgroundBrush").WithRef(Border.BorderBrushProperty, "WarningBrush"));

        b.Group("Apps that should never be interrupted");
        b.Apps("Pause.ForegroundExceptions", "Pause while these apps are in front", "Pick from your open apps (e.g. OBS, Photoshop). Optional.",
            () => s.ForegroundExceptions, v => s.ForegroundExceptions = v);

        b.Group("Quiet times");
        b.Row("Pause.QuietTimes", "No breaks during…", "Lessons, meetings, gaming nights. Optional.",
            new TimeWindowsEditor(s.QuietTimes, () => { }, "Add quiet time"), wideControl: true);

        b.Group("Time away");
        b.Toggle("Idle.NaturalBreaks", "Count time away as a break", "Stepped away for 5 minutes? That counts as your stretch break.",
            () => _ctx.Edit.Idle.NaturalBreaks, v => _ctx.Edit.Idle.NaturalBreaks = v);
        return b.Panel;
    }

    private UIElement Done()
    {
        var b = new PageBuilder(_ctx, "Setup");
        var g = _ctx.Edit.General;
        b.Intro("You're ready. Break App lives in the tray (bottom-right, near the clock). Click its icon for settings, right-click for pause, skip and more.");
        b.Group("Startup");
        b.Toggle("General.StartWithWindows", "Start with Windows", "Recommended, so breaks keep working after a restart.", () => g.StartWithWindows, v => g.StartWithWindows = v);
        b.Group("Good to know");
        var tips = new StackPanel();
        foreach (var tip in new[]
        {
            $"Pause / resume anywhere: {(string.IsNullOrEmpty(g.PauseHotkey) ? "set a shortcut in Settings › General" : g.PauseHotkey)}",
            $"Take a break now: {(string.IsNullOrEmpty(g.BreakNowHotkey) ? "set a shortcut in Settings › General" : g.BreakNowHotkey)}",
            $"End a locked break early: {_ctx.Edit.Lock.EscapeHotkey}",
            "Every text, colour, sound and timer can be changed in Settings — use the search box to find anything.",
            "Open this guide again any time: tray icon → Setup guide…",
        })
            tips.Children.Add(new TextBlock { Text = "•  " + tip, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 4, 0, 4) }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
        b.Add(tips);
        var open = b.Button("Finish and open Settings", () => { Finish(save: true); OpenSettingsRequested?.Invoke(); });
        b.AddLoose(PageBuilder.Buttons(open));
        return b.Panel;
    }

    // ------------------------------------------------------------------ finish

    private void Finish(bool save)
    {
        if (_finished) return;
        _finished = true;
        var s = save ? _ctx.Edit : SettingsStore.Clone(_c.Settings);
        s.General.SetupDone = true;
        var message = _c.SaveSettings(s);
        if (message is not null) MessageBox.Show(this, message, "Commitment mode", MessageBoxButton.OK, MessageBoxImage.Information);
        if (save) _c.Info("You're all set", "Break App is running in the tray.", NotificationKind.Success);
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_finished)
        {
            // Closing with the X = skip: keep the current settings but don't ask again.
            _finished = true;
            var s = SettingsStore.Clone(_c.Settings);
            s.General.SetupDone = true;
            _c.SaveSettings(s);
        }
        ThemeManager.Apply(_c.Settings.Appearance);
        base.OnClosing(e);
    }
}
