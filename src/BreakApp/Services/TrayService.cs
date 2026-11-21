using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BreakApp.Core.Commitment;
using BreakApp.Core.Settings;
using BreakApp.Theme;

namespace BreakApp.Services;

/// <summary>Tray icon with a live "time to next break" ring, tooltip, and the main menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly BreakCoordinator _c;
    private readonly NotifyIcon _icon;
    private IntPtr _hIcon;
    private string _iconKey = "";
    private readonly System.Windows.Forms.Timer _menuTimer = new() { Interval = 1000 };
    private ToolStripMenuItem? _header;
    private readonly List<(ToolStripMenuItem Item, BreakApp.Core.Models.BreakRule Rule)> _skipItems = [];

    public event Action<string?>? OpenSettingsRequested;
    public event Action? QuitRequested;
    public event Action? DiagnosticsRequested;
    public event Action? SetupGuideRequested;

    public TrayService(BreakCoordinator c)
    {
        _c = c;
        _icon = new NotifyIcon { Text = "Break App", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        // Keep the times in the open menu ticking.
        _menuTimer.Tick += (_, _) => UpdateMenuTexts();
        _icon.ContextMenuStrip.Opened += (_, _) => _menuTimer.Start();
        _icon.ContextMenuStrip.Closed += (_, _) => _menuTimer.Stop();
        _icon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(null);
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettingsRequested?.Invoke(null); };
        c.StateChanged += Refresh;
        ThemeManager.Changed += () => { _iconKey = ""; Refresh(); };
        Refresh();
    }

    private void Refresh()
    {
        string tip;
        double fraction = 1;
        var paused = _c.Pause.IsPaused;
        if (_c.RunningRule is { } running) tip = $"On a break: {running.Name}";
        else if (paused) tip = _c.Pause.Reason ?? "Paused";
        else if (_c.Scheduler.NextBreak() is { } next)
        {
            tip = HeaderText();
            fraction = next.Rule.Interval.Ticks > 0 ? Math.Clamp(next.In.TotalSeconds / next.Rule.Interval.TotalSeconds, 0, 1) : 1;
        }
        else tip = "No upcoming breaks";

        var text = Truncate("Break App · " + tip, 127);
        if (_icon.Text != text) _icon.Text = text;
        var key = $"{paused}|{_c.RunningRule is not null}|{Math.Round(fraction * 24)}|{ThemeManager.Palette.A1}";
        if (key != _iconKey)
        {
            _iconKey = key;
            SetIcon(DrawIcon(paused, _c.RunningRule is not null, fraction));
        }
    }

    /// <summary>"Next: Short break in 12:30", with a note when timers are paused because there's no input.</summary>
    private string HeaderText()
    {
        if (_c.RunningRule is { } running) return $"On a break: {running.Name}";
        if (_c.Pause.IsPaused) return _c.Pause.Reason ?? "Paused";
        if (_c.Scheduler.NextBreak() is not { } n) return "No upcoming breaks";
        var text = $"Next: {n.Rule.Name} in {Format.Countdown(n.In)}";
        return _c.Scheduler.TimersFrozen && n.Rule.TimerMode == BreakApp.Core.Models.TimerMode.ActiveTime ? text + " (paused: no input)" : text;
    }

    private string SkipText(BreakApp.Core.Models.BreakRule r) => $"{r.Name} (in {Format.Countdown(_c.Scheduler.TimeUntil(r.Id))})";

    private void UpdateMenuTexts()
    {
        if (_header is not null) _header.Text = HeaderText();
        foreach (var (item, rule) in _skipItems) item.Text = SkipText(rule);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void SetIcon(Bitmap bmp)
    {
        var h = bmp.GetHicon();
        _icon.Icon = Icon.FromHandle(h);
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
        _hIcon = h;
        bmp.Dispose();
    }

    private static readonly string IconFontFamily =
        new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private static System.Drawing.Color C(System.Windows.Media.Color c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Accent tile; white ring shrinks as the next break approaches; grey with pause bars when paused.</summary>
    internal static Bitmap DrawIcon(bool paused, bool inBreak, double fraction)
    {
        const int s = 32;
        var bmp = new Bitmap(s, s);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        var p = ThemeManager.Palette;
        using var path = new GraphicsPath();
        const float r = 7, w = s - 1;
        path.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
        path.AddArc(w - 2 * r, 0, 2 * r, 2 * r, 270, 90);
        path.AddArc(w - 2 * r, w - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddArc(0, w - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        var c1 = paused ? System.Drawing.Color.FromArgb(0x6B, 0x72, 0x80) : C(p.A1);
        var c2 = paused ? System.Drawing.Color.FromArgb(0x9C, 0xA3, 0xAF) : C(p.A3);
        using (var brush = new LinearGradientBrush(new RectangleF(0, 0, s, s), c1, c2, 45f)) g.FillPath(brush, path);

        // Same as the app icon: white stopwatch (Segoe Fluent Icons U+E916). Paused = grey tile.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(IconFontFamily, s * 0.6f, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("", font, Brushes.White, new RectangleF(0, s * 0.02f, s, s), fmt);
        return bmp;
    }

    /// <summary>UX tour: open the menu at a point so it can be captured.</summary>
    internal ContextMenuStrip TestShowMenu(System.Drawing.Point at)
    {
        var menu = _icon.ContextMenuStrip!;
        menu.AutoClose = false; // capture only: stay open without focus
        BuildMenu();           // an empty strip refuses to open
        menu.Show(at);
        return menu;
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Renderer = new ThemedMenuRenderer();
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(4);
        menu.Font = new Font("Segoe UI", 10f);
        _skipItems.Clear();
        var c = _c;

        _header = new ToolStripMenuItem(HeaderText()) { Enabled = false };
        menu.Items.Add(_header);
        menu.Items.Add(new ToolStripSeparator());

        var rules = c.Settings.Rules.Where(r => r.Enabled).ToList();
        var take = new ToolStripMenuItem("Take a break now");
        foreach (var r in rules) take.DropDownItems.Add(r.Name, null, (_, _) => c.TakeNow(r.Id));
        take.Enabled = rules.Count > 0 && c.RunningRule is null;
        menu.Items.Add(take);

        var skip = new ToolStripMenuItem("Skip next");
        foreach (var r in rules)
        {
            var item = new ToolStripMenuItem(SkipText(r), null, (_, _) =>
            {
                if (c.SkipNext(r.Id) is { } why) c.Info("Can't skip", why);
                else c.Info("Skipped", $"The next '{r.Name}' break will be skipped.", Notifications.NotificationKind.Success);
            }) { Enabled = r.AllowSkip };
            skip.DropDownItems.Add(item);
            _skipItems.Add((item, r));
        }
        skip.Enabled = rules.Count > 0;
        menu.Items.Add(skip);

        var pauseBlock = c.BlockReason(GuardedAction.Pause);
        if (c.Signals.Manual.IsPaused(c.Clock.Now))
            menu.Items.Add("Resume breaks", null, (_, _) => c.Resume());
        else
        {
            var pause = new ToolStripMenuItem(pauseBlock is null ? "Pause" : $"Pause — {pauseBlock}") { Enabled = pauseBlock is null };
            void Add(string label, Func<string?> act) => pause.DropDownItems.Add(label, null, (_, _) => { if (act() is { } why) c.Info("Can't pause", why); });
            Add("15 minutes", () => c.PauseFor(TimeSpan.FromMinutes(15)));
            Add("30 minutes", () => c.PauseFor(TimeSpan.FromMinutes(30)));
            Add("1 hour", () => c.PauseFor(TimeSpan.FromHours(1)));
            Add("2 hours", () => c.PauseFor(TimeSpan.FromHours(2)));
            Add("Until tomorrow", c.PauseUntilTomorrow);
            Add("Until I resume", () => c.PauseFor(null));
            menu.Items.Add(pause);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettingsRequested?.Invoke(null));
        menu.Items.Add("Stats…", null, (_, _) => OpenSettingsRequested?.Invoke("Stats"));
        menu.Items.Add("Setup guide…", null, (_, _) => SetupGuideRequested?.Invoke());
        if (c.Settings.General.AdvancedMode) menu.Items.Add("Diagnostics…", null, (_, _) => DiagnosticsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        var quitBlock = c.BlockReason(GuardedAction.Quit);
        menu.Items.Add(new ToolStripMenuItem(quitBlock is null ? "Quit" : $"Quit — {quitBlock}", null, (_, _) => QuitRequested?.Invoke()) { Enabled = quitBlock is null });
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
    }
}

/// <summary>Draws the WinForms tray menu in the app's theme (surface colour, light/dark text, accent hover, rounded highlight).</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private static System.Drawing.Color Res(string key, System.Drawing.Color fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush b
            ? System.Drawing.Color.FromArgb(255, b.Color.R, b.Color.G, b.Color.B) : fallback;

    private static System.Drawing.Color Blend(System.Drawing.Color over, System.Drawing.Color under, double a) =>
        System.Drawing.Color.FromArgb((int)(over.R * a + under.R * (1 - a)), (int)(over.G * a + under.G * (1 - a)), (int)(over.B * a + under.B * (1 - a)));

    private readonly System.Drawing.Color _surface = Res("SurfaceBrush", System.Drawing.Color.FromArgb(21, 38, 79));
    private readonly System.Drawing.Color _text = Res("Text1Brush", System.Drawing.Color.White);
    private readonly System.Drawing.Color _muted = Res("Text3Brush", System.Drawing.Color.Gray);
    private readonly System.Drawing.Color _accent = Res("Accent2Brush", System.Drawing.Color.CornflowerBlue);
    private readonly System.Drawing.Color _border = Res("ButtonBorderBrush", System.Drawing.Color.DimGray);

    public ThemedMenuRenderer() : base(new Table()) => RoundedEdges = false;

    private sealed class Table : ProfessionalColorTable
    {
        public Table() => UseSystemColors = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(_surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(_border);
        e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = new GraphicsPath();
        const int d = 10;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(Blend(_accent, _surface, 0.22));
        e.Graphics.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _text : _muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Blend(_border, _surface, 0.6));
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }
}
