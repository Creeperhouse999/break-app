using System.Windows.Input;
using System.Windows.Interop;
using BreakApp.Core.Settings;
using Microsoft.Win32;

namespace BreakApp.Services;

/// <summary>System-wide shortcuts via RegisterHotKey on a message-only window.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("BreakAppHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _source.AddHook(WndProc);
    }

    /// <summary>Returns false when the text is invalid or another app already owns the combo.</summary>
    public bool Register(string text, Action action)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!Hotkey.TryParse(text, out var hk) || !Enum.TryParse<Key>(hk.Key, true, out var key)) return false;
        uint mods = Native.MOD_NOREPEAT;
        if (hk.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= Native.MOD_CONTROL;
        if (hk.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= Native.MOD_SHIFT;
        if (hk.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= Native.MOD_ALT;
        if (hk.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= Native.MOD_WIN;
        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var a))
        {
            handled = true;
            try { a(); } catch (Exception e) { App.Log("hotkey action failed: " + e); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.Dispose();
    }
}

/// <summary>Start with Windows through the per-user Run key (no admin needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "BreakApp";

    public static void Set(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath;
            if (enabled && exe is not null) k.SetValue(Name, $"\"{exe}\"");
            else if (k.GetValue(Name) is not null) k.DeleteValue(Name);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            App.Log("autostart failed: " + e.Message);
        }
    }
}
