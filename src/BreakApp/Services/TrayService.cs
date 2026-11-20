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

    public event Action<string?>? OpenSettingsRequested;
    public event Action? QuitRequested;
    public event Action? DiagnosticsRequested;
    public event Action? SetupGuideRequested;

    public TrayService(BreakCoordinator c)
    {
        _c = c;
        _icon = new NotifyIcon { Text = "Break App", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
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
            tip = $"Next: {next.Rule.Name} in {Format.Countdown(next.In)}";
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void SetIcon(Bitmap bmp)
    {
        var h = bmp.GetHicon();
        _icon.Icon = Icon.FromHandle(h);
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
        _hIcon = h;
        bmp.Dispose();
    }

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

        using var pen = new Pen(System.Drawing.Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        const float m = 6.5f;
        if (paused)
        {
            g.FillRectangle(Brushes.White, 11, 9, 3.5f, 14);
            g.FillRectangle(Brushes.White, 17.5f, 9, 3.5f, 14);
        }
        else if (inBreak)
        {
            g.DrawEllipse(pen, m, m, s - 2 * m, s - 2 * m);
            g.FillEllipse(Brushes.White, 13, 13, 6, 6);
        }
        else
        {
            using var faint = new Pen(System.Drawing.Color.FromArgb(90, 255, 255, 255), 3f);
            g.DrawEllipse(faint, m, m, s - 2 * m, s - 2 * m);
            var sweep = (float)Math.Max(8, 360 * fraction);
            g.DrawArc(pen, m, m, s - 2 * m, s - 2 * m, -90, sweep);
        }
        return bmp;
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        var c = _c;

        var header = c.RunningRule is { } running ? $"On a break: {running.Name}"
            : c.Pause.IsPaused ? c.Pause.Reason ?? "Paused"
            : c.Scheduler.NextBreak() is { } n ? $"Next: {n.Rule.Name} in {Format.Countdown(n.In)}"
            : "No upcoming breaks";
        menu.Items.Add(new ToolStripMenuItem(header) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var rules = c.Settings.Rules.Where(r => r.Enabled).ToList();
        var take = new ToolStripMenuItem("Take a break now");
        foreach (var r in rules) take.DropDownItems.Add(r.Name, null, (_, _) => c.TakeNow(r.Id));
        take.Enabled = rules.Count > 0 && c.RunningRule is null;
        menu.Items.Add(take);

        var skip = new ToolStripMenuItem("Skip next");
        foreach (var r in rules)
        {
            var item = new ToolStripMenuItem($"{r.Name} (in {Format.Countdown(c.Scheduler.TimeUntil(r.Id))})", null, (_, _) =>
            {
                if (c.SkipNext(r.Id) is { } why) c.Info("Can't skip", why);
                else c.Info("Skipped", $"The next '{r.Name}' break will be skipped.", Notifications.NotificationKind.Success);
            }) { Enabled = r.AllowSkip };
            skip.DropDownItems.Add(item);
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
