using BreakApp.Core.Models;

namespace BreakApp.Notifications;

/// <summary>Shows notification cards one at a time; a request with the current card's id updates it in place, others queue.</summary>
public sealed class NotificationManager(Func<AppearanceSettings> appearance)
{
    private readonly LinkedList<(NotificationRequest Request, Action<string>? Callback)> _queue = new();
    private NotificationWindow? _current;

    public bool IsShowing(string id) => _current?.Id == id || _queue.Any(q => q.Request.Id == id);

    public void Show(NotificationRequest r, Action<string>? callback = null)
    {
        if (_current is not null && _current.Id == r.Id)
        {
            _current.Update(r, callback);
            return;
        }
        var queued = _queue.FirstOrDefault(q => q.Request.Id == r.Id);
        if (queued.Request is not null) _queue.Remove(queued);
        _queue.AddLast((r, callback));
        Pump();
    }

    /// <summary>Close without reporting a result (e.g. the warning when its break starts).</summary>
    public void Close(string id)
    {
        foreach (var q in _queue.Where(q => q.Request.Id == id).ToList()) _queue.Remove(q);
        if (_current?.Id == id) _current.Dismiss("hidden");
    }

    private void Pump()
    {
        if (_current is not null || _queue.Count == 0) return;
        var (req, cb) = _queue.First!.Value;
        _queue.RemoveFirst();
        var w = new NotificationWindow(req, cb, appearance());
        w.Finished += done =>
        {
            if (_current == done) _current = null;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Pump);
        };
        _current = w;
        w.Show();
    }
}
