using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;

namespace BreakApp.Signals;

/// <summary>Task 8 stub: replaced by real detectors in Task 11.</summary>
public sealed class SignalHub : IDisposable
{
    public ManualPause Manual { get; } = new();
    public ActivitySnapshot Activity(IdleSettings s) => new(BreakApp.Services.Native.IdleTime());
    public PauseSignals Pause(PauseSettings s, DateTime now) =>
        new(false, null, false, false, Manual.Until, Manual.Indefinite);
    public void Dispose() { }
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
