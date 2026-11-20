using BreakApp.Core.Models;

namespace BreakApp.Notifications;

public enum NotificationKind { Info, Success, Warning, Error }

public sealed record NotificationAction(string Id, string Label, bool Primary = false);

/// <summary>
/// One notification card. Either a plain timed card (<see cref="Duration"/>, Zero = until closed) or a break countdown card
/// (<see cref="Remaining"/> + <see cref="Total"/>, closes with "timeout" when Remaining reaches zero).
/// </summary>
public sealed record NotificationRequest(
    string Id,
    NotificationKind Kind,
    string Title,
    string Body,
    IReadOnlyList<NotificationAction> Actions,
    TimeSpan Duration,
    Func<TimeSpan>? Remaining = null,
    TimeSpan Total = default,
    FlashSettings? Flash = null,
    bool Closable = true);
