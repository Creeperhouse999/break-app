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
        OnSettingsApplied(settings);
        Coordinator.Start();
        Log($"started (fast={fast})");
    }

    /// <summary>Command-line demos used for manual verification.</summary>
    private bool HandleDemoArgs(string[] args)
    {
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
        return false;
    }

    private void OnSettingsApplied(Core.Models.AppSettings s)
    {
    }

    public void ShowSettings(string? page)
    {
        Log("settings requested: " + page);
    }

    private void ShowDiagnostics()
    {
    }

    public void Quit()
    {
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
