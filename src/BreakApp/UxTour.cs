using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BreakApp.Breaks;
using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;
using BreakApp.Notifications;
using BreakApp.Services;
using BreakApp.SettingsUI;
using BreakApp.Signals;

namespace BreakApp;

/// <summary>
/// `--ux-tour &lt;dir&gt;`: opens every window, menu, card and break ON SCREEN and captures what is really shown
/// (popups, menus and the tray menu included). Uses temp settings; never locks input. For visual review.
/// </summary>
internal static class UxTour
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);

    private static string _dir = "";
    private static int _n;

    private static void Shot(string name, System.Drawing.Rectangle r)
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        r.Intersect(screen);
        if (r.Width <= 0 || r.Height <= 0) return;
        using var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size);
        bmp.Save(Path.Combine(_dir, $"{++_n:00}-{name}.png"));
    }

    private static System.Drawing.Rectangle Rect(Window w, int pad = 0)
    {
        GetWindowRect(new WindowInteropHelper(w).Handle, out var r);
        return System.Drawing.Rectangle.FromLTRB(r.Left - pad, r.Top - pad, r.Right + pad, r.Bottom + pad);
    }

    private static Task Wait(int ms) => Task.Delay(ms);

    public static async Task Run(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        var tmp = Path.Combine(Path.GetTempPath(), "BreakAppTour_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var clock = new ManualClock(DateTime.Now);
        var s = AppSettings.CreateDefault();
        s.General.SetupDone = true;
        s.General.StartWithWindows = false;
        Theme.ThemeManager.Apply(s.Appearance);
        var c = new BreakCoordinator(s, Path.Combine(tmp, "s.json"), clock, 1, Path.Combine(tmp, "h.jsonl"));
        c.ActivityOverride = _ => new ActivitySnapshot(TimeSpan.Zero);
        c.PauseOverride = (_, _) => new PauseSignals(false, null, false, false, null, false);
        for (var i = 0; i < 300; i++) { clock.Now = clock.Now.AddSeconds(1); c.TickNow(); } // some time on the clock

        // ---- settings window, every page
        var w = new SettingsWindow(c, null) { Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        await Wait(600);
        foreach (var (name, _) in SettingsPages.Categories)
        {
            w.TestNavigate(name);
            await Wait(350);
            Shot("settings-" + name.Replace(" & ", "-").Replace(' ', '-').ToLowerInvariant(), Rect(w));
        }
        w.TestNavigate("Breaks", w.TestContext.Edit.Rules[0].Id.ToString());
        await Wait(350);
        Shot("settings-rule-editor", Rect(w));
        w.TestNavigate("Breaks");
        await Wait(200);
        w.TestSearch("quiet");
        await Wait(350);
        Shot("settings-search", Rect(w));
        w.TestSearch("");

        // presets menu (popups close at once unless our window is the active one)
        w.Activate();
        await Wait(300);
        w.TestOpenPresets();
        await Wait(500);
        Shot("menu-presets", Rect(w, 40));
        w.TestCloseMenus();
        await Wait(200);

        // app picker
        var picker = new AppPickerWindow(RunningApps.Scan(), ["obs64"]) { Owner = w };
        picker.Show();
        await Wait(500);
        Shot("app-picker", Rect(picker));
        picker.Close();

        w.TestNavigate("When to pause");
        w.TestContext.Edit.Pause.ForegroundExceptions = ["obs64", "Photoshop"];
        w.TestNavigate("When to pause");
        await Wait(400);
        Shot("settings-pausing-with-apps", Rect(w));
        w.Close();
        await Wait(300);

        // ---- setup guide
        var g = new SetupGuideWindow(c) { Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
        g.Show();
        for (var i = 0; i < 5; i++)
        {
            g.GoToStep(i);
            await Wait(400);
            Shot($"setup-step{i + 1}", Rect(g));
        }
        g.TestCloseWithoutSaving();
        await Wait(300);

        // ---- notifications
        var area = SystemParameters.WorkArea;
        var corner = new System.Drawing.Rectangle(
            (int)((area.Right - 520) * DpiScale()), (int)((area.Bottom - 320) * DpiScale()), (int)(520 * DpiScale()), (int)(320 * DpiScale()));
        c.Notifications.Show(new NotificationRequest("tour-warn", NotificationKind.Info, "Stretch in 30 s", "5 min break · fullscreen",
            [new("now", "Start now", true), new("postpone", "Postpone 5 min"), new("skip", "Skip")], TimeSpan.FromSeconds(6)));
        await Wait(900);
        Shot("notification-warning", corner);
        c.Notifications.Close("tour-warn");
        await Wait(500);
        var end = DateTime.Now.AddSeconds(15);
        c.Notifications.Show(new NotificationRequest("tour-break", NotificationKind.Warning, "Short break", "Look away from the screen for a bit.",
            [new("skip", "Skip"), new("postpone", "Postpone 5 min")], TimeSpan.Zero, Remaining: () => end - DateTime.Now, Total: TimeSpan.FromSeconds(20),
            Flash: new FlashSettings()));
        await Wait(900);
        Shot("notification-break", corner);
        c.Notifications.Close("tour-break");
        await Wait(500);

        // ---- breaks (preview path: never locks)
        foreach (var style in new[] { BreakStyle.Window, BreakStyle.Fullscreen })
        {
            var r = RuleTemplates.LongBreak();
            r.Style = style;
            r.Duration = TimeSpan.FromMinutes(5);
            c.Presenter.Preview(r, c.Settings);
            await Wait(2500);
            var p = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            Shot("break-" + style.ToString().ToLowerInvariant(), style == BreakStyle.Window
                ? new System.Drawing.Rectangle(p.X + p.Width / 4, p.Y + p.Height / 6, p.Width / 2, p.Height * 2 / 3) : p);
            c.Presenter.End(BreakOutcome.Skipped);
            await Wait(400);
        }

        // ---- tray menu
        var holder = new Window { Width = 200, Height = 100, Left = 20, Top = 20, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        holder.Show();
        holder.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(holder).Handle);
        await Wait(300);
        var tray = new TrayService(c);
        var at = new System.Drawing.Point((int)(400 * DpiScale()), (int)(300 * DpiScale()));
        var menu = tray.TestShowMenu(at);
        await Wait(700);
        Shot("tray-menu", new System.Drawing.Rectangle(at.X - 20, at.Y - 20, menu.Width + 60, menu.Height + 60));
        menu.Close();
        tray.Dispose();
        holder.Close();

        // ---- diagnostics
        var d = new DiagnosticsWindow(c) { Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
        d.Show();
        await Wait(800);
        Shot("diagnostics", Rect(d));
        d.Close();

        c.Dispose();
        try { Directory.Delete(tmp, true); } catch { }
    }

    private static double DpiScale()
    {
        var src = PresentationSource.FromVisual(Application.Current.MainWindow ?? new Window());
        return src?.CompositionTarget?.TransformToDevice.M11 ?? System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width / SystemParameters.PrimaryScreenWidth;
    }
}
