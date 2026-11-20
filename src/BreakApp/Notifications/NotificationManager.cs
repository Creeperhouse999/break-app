namespace BreakApp.Notifications;

/// <summary>Task 8 stub: replaced by the real card in Task 9.</summary>
public sealed class NotificationManager(BreakApp.Services.SoundService sound, Func<BreakApp.Core.Models.AppearanceSettings> appearance, double timeScale)
{
    public void Show(NotificationRequest r, Action<string>? callback = null) => App.Log($"[notification] {r.Kind} {r.Title} - {r.Body}");
    public void Close(string id) { }
}
