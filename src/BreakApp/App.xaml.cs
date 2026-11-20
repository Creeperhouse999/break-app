using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BreakApp.Core.History;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;
using BreakApp.Services;
using BreakApp.Theme;

namespace BreakApp;

public partial class App : Application
{
    private const string MutexName = "BreakApp.SingleInstance";
    private const string ShowEventName = "BreakApp.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayService? _tray;

    public static BreakCoordinator Coordinator { get; private set; } = null!;
    public static string LogPath => Path.Combine(SettingsStore.AppDataDir, "app.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Crash(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Crash(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log("unobserved: " + ex.Exception); ex.SetObserved(); };

        _mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* first instance is starting */ }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(() => ShowSettings(null));
        }) { IsBackground = true, Name = "show-listener" }.Start();

        var settings = SettingsStore.Load(SettingsStore.DefaultPath);
        if (!File.Exists(SettingsStore.DefaultPath))
        {
            try { SettingsStore.Save(settings, SettingsStore.DefaultPath); } catch (Exception ex) { Log("first save failed: " + ex.Message); }
        }
        ThemeManager.Apply(settings.Appearance);

        var fast = Environment.GetEnvironmentVariable("BREAKAPP_FAST") == "1";
        IClock clock = fast ? new ScaledClock(60) : new SystemClock();
        Coordinator = new BreakCoordinator(settings, SettingsStore.DefaultPath, clock, fast ? 60 : 1, HistoryLog.DefaultPath);
        Coordinator.SettingsApplied += OnSettingsApplied;

        if (HandleDemoArgs(e.Args)) return;

        _tray = new TrayService(Coordinator);
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.QuitRequested += Quit;
        _tray.DiagnosticsRequested += ShowDiagnostics;
        _tray.SetupGuideRequested += ShowSetupGuide;
        OnSettingsApplied(settings);
        Coordinator.Start();
        Log($"started (fast={fast})");
        if (!settings.General.SetupDone) ShowSetupGuide();

        // Startup allocates a lot once (XAML, settings, theme); hand it back so the idle tray app stays small.
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        trim.Tick += (_, _) =>
        {
            trim.Stop();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        };
        trim.Start();
    }

    /// <summary>Command-line demos used for manual verification.</summary>
    private bool HandleDemoArgs(string[] args)
    {
        var shots = Array.IndexOf(args, "--render-screenshots");
        if (shots >= 0)
        {
            Screenshots.Render(args.ElementAtOrDefault(shots + 1) ?? Path.Combine(Environment.CurrentDirectory, "docs", "screenshots"));
            Shutdown();
            return true;
        }
        if (args.Contains("--check-settings"))
        {
            // Headless: builds every settings page (no window) and lists settings that have no UI control.
            var missing = SettingsUI.SettingsWindow.CoverageCheck(Coordinator);
            var report = missing.Count == 0 ? "settings coverage: OK (every setting has a control)" : "settings coverage: MISSING " + string.Join(", ", missing);
            Log(report);
            File.WriteAllText(Path.Combine(SettingsStore.AppDataDir, "coverage.txt"), report);
            Shutdown();
            return true;
        }
        if (args.Contains("--demo-notification"))
        {
            var r = Coordinator.Settings.Rules.FirstOrDefault() ?? Core.Settings.RuleTemplates.Stretch();
            Coordinator.Notifications.Show(new Notifications.NotificationRequest(
                "demo", Notifications.NotificationKind.Info, "Stretch in 30 s", "5 min break · fullscreen",
                [new("now", "Start now", true), new("postpone", "Postpone 5 min"), new("skip", "Skip")],
                TimeSpan.FromSeconds(8), Flash: new Core.Models.FlashSettings { Enabled = true, Count = 0, SpeedHz = 1.5 }),
                id => { Log("demo notification result: " + id); Dispatcher.BeginInvoke(Shutdown, DispatcherPriority.Background); });
            return true;
        }
        if (args.Contains("--demo-break-notification"))
        {
            var end = DateTime.Now.AddSeconds(15);
            Coordinator.Notifications.Show(new Notifications.NotificationRequest(
                "demo-break", Notifications.NotificationKind.Warning, "Eye rest", "Look at something 6 metres away for 20 seconds.",
                [new("skip", "Skip"), new("postpone", "Postpone 5 min")], TimeSpan.Zero,
                Remaining: () => end - DateTime.Now, Total: TimeSpan.FromSeconds(15),
                Flash: new Core.Models.FlashSettings { Enabled = true, Style = Core.Models.FlashStyle.Pulse, PulseAmount = 6, SpeedHz = 1.5 }),
                id => { Log("demo break notification result: " + id); Dispatcher.BeginInvoke(Shutdown, DispatcherPriority.Background); });
            return true;
        }
        var demoBreak = Array.IndexOf(args, "--demo-break");
        if (demoBreak >= 0)
        {
            // --demo-break window|fullscreen|locked|total [seconds]
            var kind = args.ElementAtOrDefault(demoBreak + 1) ?? "fullscreen";
            var secs = int.TryParse(args.ElementAtOrDefault(demoBreak + 2), out var n) ? n : 8;
            var rule = Core.Settings.RuleTemplates.Stretch();
            rule.Duration = TimeSpan.FromSeconds(secs);
            rule.Style = kind == "window" ? Core.Models.BreakStyle.Window : Core.Models.BreakStyle.Fullscreen;
            rule.Lock = kind switch { "locked" => Core.Models.LockLevel.LockedWithKeybind, "total" => Core.Models.LockLevel.Total, _ => Core.Models.LockLevel.Skippable };
            Coordinator.Presenter.Present(rule, [Core.Settings.RuleTemplates.EyeRest()], rule.Messages[0], outcome =>
            {
                Log("demo break result: " + outcome);
                Dispatcher.BeginInvoke(Shutdown, DispatcherPriority.Background);
            });
            return true;
        }
        return false;
    }

    private GlobalHotkeys? _hotkeys;
    private SettingsUI.SettingsWindow? _settingsWindow;
    private string _hotkeyState = "";

    private void OnSettingsApplied(Core.Models.AppSettings s)
    {
        // BREAKAPP_NO_AUTOSTART=1 lets test runs leave the Windows startup list alone.
        if (Environment.GetEnvironmentVariable("BREAKAPP_NO_AUTOSTART") != "1") Autostart.Set(s.General.StartWithWindows);

        var state = s.General.PauseHotkey + "|" + s.General.BreakNowHotkey;
        if (state == _hotkeyState) return;
        _hotkeyState = state;
        _hotkeys ??= new GlobalHotkeys();
        _hotkeys.Clear();
        var failed = new List<string>();
        if (!_hotkeys.Register(s.General.PauseHotkey, TogglePause)) failed.Add(s.General.PauseHotkey);
        if (!_hotkeys.Register(s.General.BreakNowHotkey, BreakNow)) failed.Add(s.General.BreakNowHotkey);
        if (failed.Count > 0)
            Coordinator.Info("Shortcut not available", $"{string.Join(", ", failed)} is used by another app. Pick another one in Settings › General.",
                Notifications.NotificationKind.Warning);
    }

    private void TogglePause()
    {
        var c = Coordinator;
        if (c.Signals.Manual.IsPaused(c.Clock.Now))
        {
            c.Resume();
            c.Info("Breaks resumed", "", Notifications.NotificationKind.Success);
        }
        else if (c.PauseFor(null) is { } why) c.Info("Can't pause", why);
        else c.Info("Breaks paused", "Press the shortcut again to resume.");
    }

    private void BreakNow()
    {
        if (Coordinator.Settings.Rules.FirstOrDefault(r => r.Enabled) is { } r) Coordinator.TakeNow(r.Id);
    }

    private SettingsUI.SetupGuideWindow? _setupGuide;

    public void ShowSetupGuide()
    {
        if (_setupGuide is null)
        {
            _setupGuide = new SettingsUI.SetupGuideWindow(Coordinator);
            _setupGuide.OpenSettingsRequested += () => ShowSettings(null);
            _setupGuide.Closed += (_, _) => _setupGuide = null;
            _setupGuide.Show();
        }
        _setupGuide.Activate();
    }

    public void ShowSettings(string? page)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsUI.SettingsWindow(Coordinator, page);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private Signals.DiagnosticsWindow? _diagnostics;

    private void ShowDiagnostics()
    {
        if (_diagnostics is null)
        {
            _diagnostics = new Signals.DiagnosticsWindow(Coordinator);
            _diagnostics.Closed += (_, _) => _diagnostics = null;
            _diagnostics.Show();
        }
        _diagnostics.Activate();
    }

    public void Quit()
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Coordinator?.Dispose();
        InputLock.ReleaseAll();
        Log("quit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InputLock.ReleaseAll();
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    private static void Crash(Exception? ex)
    {
        InputLock.ReleaseAll();
        Log("CRASH: " + ex);
        try
        {
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            File.AppendAllText(Path.Combine(SettingsStore.AppDataDir, "error.log"), $"{DateTime.Now:O} {ex}\n");
        }
        catch { }
    }

    private static readonly object LogLock = new();

    public static void Log(string message)
    {
        Debug.WriteLine(message);
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(SettingsStore.AppDataDir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
            }
        }
        catch { /* logging must never throw */ }
    }
}
