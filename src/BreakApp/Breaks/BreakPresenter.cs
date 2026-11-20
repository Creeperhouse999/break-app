using BreakApp.Core.Models;

namespace BreakApp.Breaks;

/// <summary>Task 8 stub: replaced by the real break windows in Task 10.</summary>
public sealed class BreakPresenter(BreakApp.Services.BreakCoordinator coordinator)
{
    private Action<BreakOutcome>? _done;
    public bool IsRunning => _done is not null;
    public BreakRule? Current { get; private set; }

    public void Present(BreakRule rule, IReadOnlyList<BreakRule> merged, string message, Action<BreakOutcome> done)
    {
        App.Log($"[break] {rule.Name}: {message}");
        Current = rule;
        _done = done;
        End(BreakOutcome.Taken);
    }

    public void End(BreakOutcome outcome)
    {
        var d = _done;
        _done = null;
        Current = null;
        d?.Invoke(outcome);
    }
}
