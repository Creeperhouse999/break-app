using System.Globalization;
using BreakApp.Core.Models;
using BreakApp.Core.Scheduling;

namespace BreakApp.Core.Pausing;

/// <summary>Raw pause-related signals gathered by the shell each tick.</summary>
public sealed record PauseSignals(
    bool MeetingActive,
    string? MeetingSource,
    bool DoNotDisturb,
    bool FullscreenApp,
    DateTime? ManualPauseUntil,
    bool ManualPauseIndefinite,
    string? ForegroundApp = null);

public static class PauseEvaluator
{
    /// <summary>
    /// Combines signals and settings into one pause state. The first matching reason wins:
    /// manual, meeting, quiet time, app exception, Do Not Disturb, fullscreen.
    /// </summary>
    public static PauseState Evaluate(PauseSettings s, PauseSignals sig, DateTime now, bool use24Hour = true)
    {
        if (sig.ManualPauseIndefinite) return new(true, "Paused manually");
        if (sig.ManualPauseUntil is DateTime until && until > now)
            return new(true, "Paused manually until " + until.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture));

        if (sig.MeetingActive)
            return new(true, string.IsNullOrEmpty(sig.MeetingSource) ? "In a meeting" : $"In a meeting ({sig.MeetingSource})");

        var quiet = s.QuietTimes.FirstOrDefault(q => q.Contains(now));
        if (quiet is not null)
            return new(true, string.IsNullOrWhiteSpace(quiet.Label) ? "Quiet time" : $"Quiet time: {quiet.Label}");

        if (sig.ForegroundApp is { Length: > 0 } fg)
        {
            var hit = s.ForegroundExceptions.FirstOrDefault(e => SameApp(e, fg));
            if (hit is not null) return new(true, $"Exception: {Path.GetFileNameWithoutExtension(hit.Trim())} is in front");
        }

        if (s.RespectDoNotDisturb && sig.DoNotDisturb) return new(true, "Do Not Disturb");
        if (s.PauseDuringFullscreen && sig.FullscreenApp) return new(true, "Fullscreen app");
        return PauseState.None;
    }

    private static bool SameApp(string configured, string processName) =>
        string.Equals(Path.GetFileNameWithoutExtension(configured.Trim()), processName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Spec §7: a pause (meeting, quiet time, manual) ends a running break unless it is a TOTAL LOCK or the rule opts out.</summary>
    public static bool ShouldEndRunningBreak(BreakRule rule, PauseState state) =>
        state.IsPaused && rule.Lock != LockLevel.Total && rule.EndOnMeeting;
}
