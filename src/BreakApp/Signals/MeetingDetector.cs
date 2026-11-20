using System.Diagnostics;
using System.IO;
using BreakApp.Core.Models;
using BreakApp.Services;
using Microsoft.Win32;

namespace BreakApp.Signals;

/// <summary>
/// Detects calls/meetings. Primary: Windows' privacy registry shows which apps are using the microphone or camera right now
/// (works for Google Meet in a browser, Zoom, Teams, Discord). Backup: user-listed process names and window-title patterns.
/// Results are cached for 2 seconds.
/// </summary>
public sealed class MeetingDetector
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private static readonly string OwnExe = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "BreakApp");

    private DateTime _next = DateTime.MinValue;
    private (bool Active, string? Source) _cached;

    public (bool Active, string? Source) Check(PauseSettings s)
    {
        var now = DateTime.UtcNow;
        if (now < _next) return _cached;
        _next = now.AddSeconds(2);
        _cached = Detect(s);
        return _cached;
    }

    public string Describe() => $"Meeting: {_cached.Active}{(_cached.Source is { } src ? $" ({src})" : "")}";

    private static (bool, string?) Detect(PauseSettings s)
    {
        try
        {
            if (s.DetectMicCamera)
            {
                foreach (var cap in new[] { "microphone", "webcam" })
                    if (ActiveUser(cap) is { } who) return (true, who);
            }

            if (s.MeetingProcesses.Count > 0)
            {
                var wanted = s.MeetingProcesses.Select(p => Path.GetFileNameWithoutExtension(p.Trim())).Where(p => p.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                        if (wanted.Contains(p.ProcessName)) return (true, p.ProcessName);
                }
            }

            var patterns = s.MeetingTitlePatterns.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (patterns.Count > 0)
            {
                var ownPid = (uint)Environment.ProcessId;
                foreach (var (title, pid) in Native.VisibleWindowTitles())
                {
                    if (pid == ownPid) continue;
                    var hit = patterns.FirstOrDefault(pt => title.Contains(pt, StringComparison.OrdinalIgnoreCase));
                    if (hit is not null) return (true, hit.Trim(' ', '-'));
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            App.Log("meeting detection failed: " + e.Message);
        }
        return (false, null);
    }

    /// <summary>Name of an app currently using the capability, or null.</summary>
    private static string? ActiveUser(string capability)
    {
        using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{capability}");
        if (root is null) return null;
        foreach (var name in root.GetSubKeyNames())
        {
            if (name == "NonPackaged")
            {
                using var np = root.OpenSubKey(name);
                if (np is null) continue;
                foreach (var exe in np.GetSubKeyNames())
                {
                    using var k = np.OpenSubKey(exe);
                    if (InUse(k))
                    {
                        var app = Path.GetFileNameWithoutExtension(exe[(exe.LastIndexOf('#') + 1)..]);
                        if (!app.Equals(OwnExe, StringComparison.OrdinalIgnoreCase)) return app;
                    }
                }
            }
            else
            {
                using var k = root.OpenSubKey(name);
                if (InUse(k))
                {
                    var app = name.Split('_')[0];
                    return app[(app.LastIndexOf('.') + 1)..];
                }
            }
        }
        return null;
    }

    private static bool InUse(RegistryKey? k) =>
        k?.GetValue("LastUsedTimeStart") is long start && start > 0 && k.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}
