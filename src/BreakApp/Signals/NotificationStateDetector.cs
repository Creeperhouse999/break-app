using System.Runtime.InteropServices;
using BreakApp.Services;

namespace BreakApp.Signals;

/// <summary>
/// Do Not Disturb / Focus and fullscreen detection.
/// DND: the shell's quiet-hours WNF state (undocumented but stable since Windows 10 1803; 0 = off), falling back to
/// SHQueryUserNotificationState's QUNS_QUIET_TIME. Fullscreen: QUNS busy / D3D fullscreen / presentation mode.
/// </summary>
public sealed partial class NotificationStateDetector
{
    // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED
    private const ulong QuietHoursStateName = 0x0D83063EA3BF1C75;

    private bool _wnfBroken;
    private (bool Dnd, bool Fullscreen) _last;

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
        out uint changeStamp, void* buffer, ref uint bufferSize);

    public (bool Dnd, bool Fullscreen) Check()
    {
        bool dnd = false, fullscreen = false;
        try
        {
            if (Native.SHQueryUserNotificationState(out var state) == 0)
            {
                fullscreen = state is Native.QUERY_USER_NOTIFICATION_STATE.Busy
                    or Native.QUERY_USER_NOTIFICATION_STATE.RunningD3DFullScreen
                    or Native.QUERY_USER_NOTIFICATION_STATE.PresentationMode;
                dnd = state == Native.QUERY_USER_NOTIFICATION_STATE.QuietTime;
            }
        }
        catch (EntryPointNotFoundException) { }
        dnd |= QuietHoursProfile() > 0;
        _last = (dnd, fullscreen);
        return _last;
    }

    private unsafe int QuietHoursProfile()
    {
        if (_wnfBroken) return 0;
        try
        {
            var name = QuietHoursStateName;
            int profile = 0;
            uint size = sizeof(int);
            var status = NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, &profile, ref size);
            if (status != 0) { _wnfBroken = true; return 0; }
            return profile;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or AccessViolationException)
        {
            _wnfBroken = true;
            return 0;
        }
    }

    public string Describe() => $"Do Not Disturb: {_last.Dnd}{(_wnfBroken ? " (quiet-hours state unavailable)" : "")}, fullscreen app: {_last.Fullscreen}";
}
