using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;
using BreakApp.Services;
using Microsoft.Win32;

namespace BreakApp.Signals;

/// <summary>
/// "How long has the user been away?" combined over keyboard/mouse, gamepads (XInput) and video playback
/// (an app keeping the display on). A locked session always counts as away.
/// </summary>
public sealed class ActivityMonitor : IDisposable
{
    private readonly uint[] _packets = new uint[4];
    private readonly bool[] _connected = new bool[4];
    private DateTime _lastPad = DateTime.MinValue, _lastMedia = DateTime.MinValue, _nextPadScan = DateTime.MinValue;
    private DateTime? _lockedAt;
    private bool _xinputMissing, _mediaNow;

    public ActivityMonitor() => SystemEvents.SessionSwitch += OnSessionSwitch;

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _lockedAt ??= DateTime.UtcNow;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            _lockedAt = null;
    }

    public ActivitySnapshot Snapshot(IdleSettings s)
    {
        var now = DateTime.UtcNow;
        if (!s.DetectKeyboardMouse && !s.DetectGamepad && !s.DetectMedia) return new(TimeSpan.Zero); // nothing to measure: never idle

        var idle = TimeSpan.MaxValue;
        if (s.DetectKeyboardMouse) idle = Native.IdleTime();
        if (s.DetectGamepad)
        {
            PollGamepads(now);
            idle = Min(idle, now - _lastPad);
        }
        if (s.DetectMedia)
        {
            _mediaNow = Native.DisplayRequired();
            if (_mediaNow) _lastMedia = now;
            idle = Min(idle, now - _lastMedia);
        }
        if (_lockedAt is DateTime locked) idle = Max(idle, now - locked);
        return new(idle == TimeSpan.MaxValue ? TimeSpan.FromDays(1) : idle);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private void PollGamepads(DateTime now)
    {
        if (_xinputMissing) return;
        var anyConnected = _connected.Any(c => c);
        if (!anyConnected && now < _nextPadScan) return; // nothing plugged in: re-scan every 10 s only
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (!anyConnected || _connected[i])
                {
                    var ok = Native.XInputGetState(i, out var st) == 0;
                    if (ok && _connected[i] && st.dwPacketNumber != _packets[i]) _lastPad = now;
                    if (ok) _packets[i] = st.dwPacketNumber;
                    _connected[i] = ok;
                }
            }
        }
        catch (DllNotFoundException) { _xinputMissing = true; }
        catch (EntryPointNotFoundException) { _xinputMissing = true; }
        if (!_connected.Any(c => c)) _nextPadScan = now.AddSeconds(10);
    }

    public string Describe() =>
        $"Keyboard/mouse idle: {Native.IdleTime():hh\\:mm\\:ss}\n" +
        $"Gamepad connected: {_connected.Any(c => c)}{(_xinputMissing ? " (XInput missing)" : "")}, last input: {Ago(_lastPad)}\n" +
        $"Video/display request: {_mediaNow}, last: {Ago(_lastMedia)}\n" +
        $"Session locked: {_lockedAt is not null}";

    private static string Ago(DateTime t) => t == DateTime.MinValue ? "never" : $"{(DateTime.UtcNow - t):hh\\:mm\\:ss} ago";

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
