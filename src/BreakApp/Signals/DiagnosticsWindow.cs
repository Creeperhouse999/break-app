using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BreakApp.Core.Settings;
using BreakApp.Services;

namespace BreakApp.Signals;

/// <summary>Live view of every signal and timer (tray → Diagnostics…, advanced mode only).</summary>
public sealed class DiagnosticsWindow : Window
{
    private readonly BreakCoordinator _c;
    private readonly TextBlock _text = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public DiagnosticsWindow(BreakCoordinator c)
    {
        _c = c;
        Title = "Break App · Diagnostics";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Text1Brush");
        Content = new ScrollViewer { Content = _text, Padding = new Thickness(18) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Clock: {_c.Clock.Now:yyyy-MM-dd HH:mm:ss}  (time scale ×{_c.TimeScale})");
        sb.AppendLine($"Idle for (combined): {_c.LastActivity.IdleFor:hh\\:mm\\:ss}  threshold {_c.Settings.Idle.Threshold:mm\\:ss}");
        sb.AppendLine(_c.Signals.Describe());
        sb.AppendLine($"Pause: {(_c.Pause.IsPaused ? _c.Pause.Reason : "no")}");
        sb.AppendLine($"Running break: {_c.RunningRule?.Name ?? "none"}");
        sb.AppendLine($"Pending settings (commitment): {_c.Guard.PendingAppliesAt?.ToString("HH:mm:ss") ?? "none"}");
        sb.AppendLine();
        foreach (var r in _c.Settings.Rules)
            sb.AppendLine($"{(r.Enabled ? "●" : "○")} {r.Name,-18} every {Format.Duration(r.Interval),-10} for {Format.Duration(r.Duration),-8} next in {Format.Countdown(_c.Scheduler.TimeUntil(r.Id))}");
        _text.Text = sb.ToString();
    }
}
