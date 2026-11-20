using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;

namespace BreakApp.Signals;

/// <summary>Collects every OS signal the scheduler needs. Cheap checks run each tick; expensive ones are cached inside the detectors.</summary>
public sealed class SignalHub : IDisposable
{
    private readonly ActivityMonitor _activity = new();
    private readonly MeetingDetector _meetings = new();
    private readonly NotificationStateDetector _notifState = new();

    public ManualPause Manual { get; } = new();

    /// <summary>Latest raw values, for the diagnostics window.</summary>
    public string Describe() =>
        $"{_activity.Describe()}\n{_meetings.Describe()}\n{_notifState.Describe()}";

    public ActivitySnapshot Activity(IdleSettings s) => _activity.Snapshot(s);

    public PauseSignals Pause(PauseSettings s, DateTime now)
    {
        var (meeting, source) = _meetings.Check(s);
        bool dnd = false, fullscreen = false;
        if (s.RespectDoNotDisturb || s.PauseDuringFullscreen) (dnd, fullscreen) = _notifState.Check();
        var foreground = s.ForegroundExceptions.Count > 0 ? ForegroundProcessName() : null;
        return new PauseSignals(meeting, source, dnd, fullscreen, Manual.Until, Manual.Indefinite, foreground);
    }

    /// <summary>Process name of the active window (e.g. "obs64"), or null.</summary>
    public static string? ForegroundProcessName()
    {
        try
        {
            var hwnd = BreakApp.Services.Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            BreakApp.Services.Native.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Dispose() => _activity.Dispose();
}

public sealed class ManualPause
{
    public DateTime? Until { get; private set; }
    public bool Indefinite { get; private set; }
    public void PauseUntil(DateTime until) { Until = until; Indefinite = false; }
    public void PauseIndefinitely() { Indefinite = true; Until = null; }
    public void Resume() { Until = null; Indefinite = false; }
    public bool IsPaused(DateTime now) => Indefinite || (Until is DateTime u && u > now);
}
