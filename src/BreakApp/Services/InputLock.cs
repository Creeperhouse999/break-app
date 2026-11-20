using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using BreakApp.Core.Settings;

namespace BreakApp.Services;

/// <summary>
/// Best-effort input lock for locked breaks: low-level keyboard and mouse hooks on a dedicated thread swallow all input
/// (Win, Alt+Tab, Alt+F4, clicks...) except an optional escape combo. Ctrl+Alt+Del can never be blocked (by Windows design).
/// Hooks die with the process; a watchdog also releases them after a maximum time even if the UI stalls.
/// </summary>
public sealed class InputLock : IDisposable
{
    private static readonly List<InputLock> Active = [];
    internal static int ActiveCount { get { lock (Active) return Active.Count; } }

    private readonly Hotkey? _escape;
    private readonly Action _onEscape;
    private readonly HashSet<uint> _down = [];
    private readonly Thread _thread;
    private readonly Timer _watchdog;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private uint _escapeVk;
    private Native.HookProc? _kbProc, _mouseProc;   // keep delegates alive
    private int _released;

    private InputLock(Hotkey? escape, Action onEscape, TimeSpan maxDuration)
    {
        _escape = escape;
        _onEscape = onEscape;
        // Hotkey only accepts real key names (no numbers), so this maps to an actual virtual-key code.
        if (escape is { } h && Enum.TryParse<Key>(h.Key, true, out var key) && Enum.IsDefined(key) && key != Key.None)
            _escapeVk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _thread = new Thread(Run) { IsBackground = true, Name = "input-lock" };
        _watchdog = new Timer(_ => Release(), null, maxDuration, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Lock input. <paramref name="escapeHotkey"/> null = no escape (TOTAL LOCK).</summary>
    public static InputLock Engage(string? escapeHotkey, Action onEscape, TimeSpan maxDuration)
    {
        Hotkey? hk = null;
        if (escapeHotkey is not null)
        {
            // A Locked break must never silently become a TOTAL LOCK: fall back to the default combo if the text is unusable.
            hk = Hotkey.TryParse(escapeHotkey, out var h) ? h
               : Hotkey.TryParse(new Core.Models.LockSettings().EscapeHotkey, out var d) ? d : null;
        }
        var l = new InputLock(hk, onEscape, maxDuration);
        lock (Active) Active.Add(l);
        l._thread.Start();
        l._ready.Wait(TimeSpan.FromSeconds(2));
        return l;
    }

    public static void ReleaseAll()
    {
        List<InputLock> copy;
        lock (Active) copy = [.. Active];
        foreach (var l in copy) l.Release();
    }

    private void Run()
    {
        // Create this thread's message queue first, so a WM_QUIT posted by Release can never be lost.
        Native.PeekMessage(out _, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);
        Volatile.Write(ref _threadId, Native.GetCurrentThreadId());
        var mod = Native.GetModuleHandle(null);
        _kbProc = KeyboardProc;
        _mouseProc = MouseProc;
        var kb = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
        var ms = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
        if (kb == IntPtr.Zero || ms == IntPtr.Zero) App.Log($"input lock: hook install failed ({Marshal.GetLastWin32Error()})");
        _ready.Set();
        try
        {
            // Released before the hooks were in place? Leave immediately (the finally removes them).
            if (Volatile.Read(ref _released) == 1) return;
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        finally
        {
            if (kb != IntPtr.Zero) Native.UnhookWindowsHookEx(kb);
            if (ms != IntPtr.Zero) Native.UnhookWindowsHookEx(ms);
        }
    }

    private static readonly uint[] CtrlKeys = [0x11, 0xA2, 0xA3], ShiftKeys = [0x10, 0xA0, 0xA1], AltKeys = [0x12, 0xA4, 0xA5], WinKeys = [0x5B, 0x5C];

    private bool AnyDown(uint[] keys) => keys.Any(_down.Contains);

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        var msg = (int)wParam;
        if (msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
        {
            _down.Add(info.vkCode);
            if (_escape is { } h && _escapeVk != 0 && info.vkCode == _escapeVk
                && AnyDown(CtrlKeys) == h.Modifiers.HasFlag(HotkeyModifiers.Ctrl)
                && AnyDown(ShiftKeys) == h.Modifiers.HasFlag(HotkeyModifiers.Shift)
                && AnyDown(AltKeys) == h.Modifiers.HasFlag(HotkeyModifiers.Alt)
                && AnyDown(WinKeys) == h.Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(_onEscape);
            }
        }
        else if (msg is Native.WM_KEYUP or Native.WM_SYSKEYUP)
        {
            // Let key-ups through so keys held when the lock started never get stuck "down" in Windows.
            _down.Remove(info.vkCode);
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        return new IntPtr(1); // swallow
    }

    private static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam) =>
        nCode < 0 ? Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam) : new IntPtr(1);

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return;
        _watchdog.Dispose();
        // If the thread hasn't started yet it sees _released after installing its hooks and exits by itself.
        var tid = Volatile.Read(ref _threadId);
        if (tid != 0 && !Native.PostThreadMessage(tid, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero))
            App.Log("input lock: quit message could not be posted");
        lock (Active) Active.Remove(this);
    }

    public void Dispose() => Release();
}
