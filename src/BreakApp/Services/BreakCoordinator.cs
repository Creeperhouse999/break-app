using System.Windows.Threading;
using BreakApp.Breaks;
using BreakApp.Core.Commitment;
using BreakApp.Core.History;
using BreakApp.Core.Models;
using BreakApp.Core.Pausing;
using BreakApp.Core.Scheduling;
using BreakApp.Core.Settings;
using BreakApp.Core.Time;
using BreakApp.Notifications;
using BreakApp.Signals;
using BreakApp.Theme;

namespace BreakApp.Services;

/// <summary>
/// Glue between the OS and the Core scheduler: once per second it gathers signals, ticks the scheduler,
/// and shows warnings and breaks. Everything runs on the UI thread.
/// </summary>
public sealed class BreakCoordinator : IDisposable
{
    private readonly string _settingsPath;
    private readonly DispatcherTimer _timer;
    private readonly Queue<BreakDueEvent> _queue = new();
    private readonly Dictionary<Guid, int> _messageIndex = [];
    private readonly Random _random = new();
    private BreakDueEvent? _running;
    private DateTime _breakStart;

    public AppSettings Settings { get; private set; }
    public IClock Clock { get; }
    /// <summary>1 normally, 60 with BREAKAPP_FAST=1.</summary>
    public double TimeScale { get; }
    public Scheduler Scheduler { get; }
    public HistoryLog History { get; }
    public CommitmentGuard Guard { get; }
    public SignalHub Signals { get; }
    public NotificationManager Notifications { get; }
    public BreakPresenter Presenter { get; }
    public SoundService Sound { get; }
    public PauseState Pause { get; private set; } = PauseState.None;
    public ActivitySnapshot LastActivity { get; private set; } = new(TimeSpan.Zero);
    public BreakRule? RunningRule => _running?.Rule;

    /// <summary>Raised after every tick and every state change (tray tooltip, diagnostics).</summary>
    public event Action? StateChanged;
    /// <summary>Raised after settings were applied (hotkeys, autostart, theme).</summary>
    public event Action<AppSettings>? SettingsApplied;

    public BreakCoordinator(AppSettings settings, string settingsPath, IClock clock, double timeScale, string historyPath)
    {
        Settings = settings;
        _settingsPath = settingsPath;
        Clock = clock;
        TimeScale = timeScale;
        Scheduler = new Scheduler(settings, clock);
        History = new HistoryLog(historyPath);
        Guard = new CommitmentGuard(clock);
        Signals = new SignalHub();
        Sound = new SoundService { Settings = () => Settings.Sound };
        Notifications = new NotificationManager(Sound, () => Settings.Appearance, timeScale);
        Presenter = new BreakPresenter(this);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => SafeTick();
    }

    public void Start() => _timer.Start();

    private void SafeTick()
    {
        try { Tick(); }
        catch (Exception e) { App.Log("tick failed: " + e); }
    }

    private void Tick()
    {
        var now = Clock.Now;
        var activity = Signals.Activity(Settings.Idle);
        LastActivity = new ActivitySnapshot(TimeSpan.FromTicks((long)(activity.IdleFor.Ticks * TimeScale)));
        Pause = PauseEvaluator.Evaluate(Settings.Pause, Signals.Pause(Settings.Pause, now), now);

        if (_running is not null && Presenter.IsRunning && PauseEvaluator.ShouldEndRunningBreak(_running.Rule, Pause))
            Presenter.End(BreakOutcome.InterruptedByMeeting);

        if (Guard.DuePending() is { } pending) ApplySettings(pending);

        foreach (var ev in Scheduler.Tick(LastActivity, Pause))
        {
            switch (ev)
            {
                case WarningEvent w: ShowWarning(w); break;
                case BreakDueEvent d: _queue.Enqueue(d); break;
                case RuleSatisfiedEvent s: Log(s.Rule, s.Reason, TimeSpan.Zero); break;
            }
        }

        if (!Presenter.IsRunning && _queue.Count > 0) StartNext();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ breaks

    private void StartNext()
    {
        var d = _queue.Dequeue();
        _running = d;
        _breakStart = Clock.Now;
        Scheduler.BreakStarted(d.Rule.Id);
        Notifications.Close(WarningId(d.Rule));
        Presenter.Present(d.Rule, d.Merged, NextMessage(d.Rule), outcome => Finished(d, outcome));
        StateChanged?.Invoke();
    }

    private void Finished(BreakDueEvent d, BreakOutcome outcome)
    {
        if (_running != d) return;
        _running = null;
        Scheduler.BreakFinished(d.Rule.Id, outcome);
        Log(d.Rule, outcome, Clock.Now - _breakStart);
        foreach (var m in d.Merged) Log(m, BreakOutcome.MergedIntoBigger, TimeSpan.Zero);
        StateChanged?.Invoke();
        if (_queue.Count > 0) System.Windows.Application.Current.Dispatcher.BeginInvoke(StartNext, DispatcherPriority.Background);
    }

    private void Log(BreakRule r, BreakOutcome outcome, TimeSpan actual)
    {
        try { History.Append(new HistoryEntry(Clock.Now, r.Id, r.Name, outcome, actual)); }
        catch (Exception e) { App.Log("history append failed: " + e.Message); }
    }

    public string NextMessage(BreakRule r)
    {
        if (r.Messages.Count == 0) return "Time for a break.";
        int i;
        if (r.MessageOrder == MessageOrder.Sequential)
        {
            i = _messageIndex.GetValueOrDefault(r.Id) % r.Messages.Count;
            _messageIndex[r.Id] = i + 1;
        }
        else i = _random.Next(r.Messages.Count);
        return r.Messages[i];
    }

    private static string WarningId(BreakRule r) => "warn-" + r.Id;

    private void ShowWarning(WarningEvent w)
    {
        var r = w.Rule;
        if (r.Warning.PlaySound) Sound.Play(Settings.Sound.WarningSound);
        if (!r.Warning.ShowNotification) return;

        var actions = new List<NotificationAction>();
        if (r.Warning.ShowStartNowButton) actions.Add(new("now", "Start now", Primary: true));
        if (r.Warning.ShowPostponeButton && r.AllowPostpone) actions.Add(new("postpone", $"Postpone {Format.Duration(r.PostponeLength)}"));
        if (r.Warning.ShowSkipNextButton && r.AllowSkip) actions.Add(new("skip", "Skip"));

        var real = TimeSpan.FromTicks((long)(w.TimeLeft.Ticks / TimeScale));
        var shown = real < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : real > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : real;
        Notifications.Show(new NotificationRequest(
            WarningId(r), NotificationKind.Info, $"{r.Name} in {Format.Duration(w.TimeLeft)}",
            $"{Format.Duration(r.Duration)} break · {Describe(r.Style)}", actions, shown,
            Flash: r.Flash.Enabled ? r.Flash : null),
            id =>
            {
                switch (id)
                {
                    case "now": TakeNow(r.Id); break;
                    case "postpone":
                        if (!Scheduler.Postpone(r.Id)) Info("Can't postpone", $"'{r.Name}' was already postponed {r.MaxPostpones}×.");
                        break;
                    case "skip":
                        if (SkipNext(r.Id) is { } reason) Info("Can't skip", reason);
                        break;
                }
                StateChanged?.Invoke();
            });
    }

    private static string Describe(BreakStyle s) => s switch
    {
        BreakStyle.Fullscreen => "fullscreen",
        BreakStyle.Window => "window",
        _ => "notification",
    };

    public void Info(string title, string body, NotificationKind kind = NotificationKind.Info) =>
        Notifications.Show(new NotificationRequest("info-" + Guid.NewGuid(), kind, title, body, [], TimeSpan.FromSeconds(6)));

    // ------------------------------------------------------------------ user actions (tray, hotkeys)

    public void TakeNow(Guid ruleId)
    {
        Scheduler.TakeNow(ruleId);
        Tick();
    }

    /// <summary>Returns a reason when blocked (commitment mode or rule forbids skipping), otherwise skips and returns null.</summary>
    public string? SkipNext(Guid ruleId)
    {
        var r = Settings.Rules.FirstOrDefault(x => x.Id == ruleId);
        if (r is null) return null;
        if (!r.AllowSkip) return $"'{r.Name}' can't be skipped.";
        if (CommitmentGuard.IsActive(Settings, r) && Guard.BlockReason(Settings, Scheduler, GuardedAction.SkipNext) is { } why) return why;
        Scheduler.SkipNext(ruleId);
        StateChanged?.Invoke();
        return null;
    }

    public string? BlockReason(GuardedAction action) => Guard.BlockReason(Settings, Scheduler, action);

    public string? PauseFor(TimeSpan? duration)
    {
        if (BlockReason(GuardedAction.Pause) is { } why) return why;
        if (duration is null) Signals.Manual.PauseIndefinitely();
        else Signals.Manual.PauseUntil(Clock.Now + duration.Value);
        Tick();
        return null;
    }

    public string? PauseUntilTomorrow()
    {
        if (BlockReason(GuardedAction.Pause) is { } why) return why;
        Signals.Manual.PauseUntil(Clock.Now.Date.AddDays(1));
        Tick();
        return null;
    }

    public void Resume()
    {
        Signals.Manual.Resume();
        Tick();
    }

    /// <summary>Apply and persist settings. Weakening changes under commitment mode are delayed by the cooldown.</summary>
    public string? SaveSettings(AppSettings edited)
    {
        SettingsValidator.Normalize(edited);
        var weakening = Guard.WeakeningChanges(Settings, edited);
        if (weakening.Count > 0)
        {
            Guard.Schedule(edited);
            return $"Commitment mode: these changes apply at {Guard.PendingAppliesAt:HH:mm}:\n• " + string.Join("\n• ", weakening);
        }
        Guard.CancelPending();
        ApplySettings(edited);
        return null;
    }

    private void ApplySettings(AppSettings s)
    {
        Settings = s;
        Scheduler.UpdateSettings(s);
        try { SettingsStore.Save(s, _settingsPath); }
        catch (Exception e) { App.Log("settings save failed: " + e.Message); Info("Couldn't save settings", e.Message, NotificationKind.Error); }
        ThemeManager.Apply(s.Appearance);
        SettingsApplied?.Invoke(s);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        if (Presenter.IsRunning) Presenter.End(BreakOutcome.Skipped);
        Signals.Dispose();
    }
}
