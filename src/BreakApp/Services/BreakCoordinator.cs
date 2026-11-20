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
    /// <summary>Last app (other than this one) that was the active window; used by "add exception".</summary>
    public string? LastForegroundApp { get; private set; }
    private static readonly string OwnProcess = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

    /// <summary>Raised after every tick and every state change (tray tooltip, diagnostics).</summary>
    public event Action? StateChanged;
    /// <summary>Raised after settings were applied (hotkeys, autostart, theme).</summary>
    public event Action<AppSettings>? SettingsApplied;

    /// <summary>Self-test hooks: replace OS signals with scripted ones.</summary>
    internal Func<IdleSettings, ActivitySnapshot>? ActivityOverride { get; set; }
    internal Func<PauseSettings, DateTime, Core.Pausing.PauseSignals>? PauseOverride { get; set; }
    internal void TickNow() => Tick();

    /// <summary>When set, breaks are held (e.g. while the setup guide is open). Shown as the pause reason.</summary>
    public string? HoldReason { get; set; }

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
        Notifications = new NotificationManager(() => Settings.Appearance);
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
        var activity = ActivityOverride?.Invoke(Settings.Idle) ?? Signals.Activity(Settings.Idle);
        if (SignalHub.ForegroundProcessName() is { } fg && fg != OwnProcess) LastForegroundApp = fg;
        LastActivity = new ActivitySnapshot(TimeSpan.FromTicks((long)(activity.IdleFor.Ticks * TimeScale)));
        var signals = PauseOverride?.Invoke(Settings.Pause, now) ?? Signals.Pause(Settings.Pause, now);
        // Our own fullscreen break looks like a "fullscreen app" to Windows (and can switch on Do Not Disturb):
        // never let it pause/end itself.
        if (Presenter.IsRunning) signals = signals with { DoNotDisturb = false, FullscreenApp = false };
        Pause = PauseEvaluator.Evaluate(Settings.Pause, signals, now, Settings.General.Use24HourClock);
        if (HoldReason is { } hold && !Pause.IsPaused) Pause = new PauseState(true, hold);

        if (_running is not null && Presenter.IsRunning && PauseEvaluator.ShouldEndRunningBreak(_running.Rule, _pausedAtStart, Pause))
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

        StartNext();
        // Safety net: anything the scheduler thinks is "in a break" but nobody shows or queues gets released.
        var showing = _queue.Select(q => q.Rule.Id).ToList();
        if (_running is not null) showing.Add(_running.Rule.Id);
        Scheduler.ReleaseOrphans(showing);
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ breaks

    private bool _pausedAtStart;

    private void StartNext()
    {
        if (Presenter.IsRunning || _queue.Count == 0) return;
        // Scheduled breaks queued during a pause (e.g. sequential breaks) wait until the pause ends; manual ones go now.
        if (Pause.IsPaused && !_queue.Peek().Manual) return;
        var d = _queue.Dequeue();
        _running = d;
        _breakStart = Clock.Now;
        _pausedAtStart = Pause.IsPaused;
        Scheduler.BreakStarted(d.Rule.Id);
        Notifications.Close(WarningId(d.Rule));
        try
        {
            Presenter.Present(d.Rule, d.Merged, NextMessage(d.Rule), outcome => Finished(d, outcome));
        }
        catch (Exception e)
        {
            App.Log("presenting a break failed: " + e);
            Presenter.Abort();
            Finished(d, BreakOutcome.Skipped);
        }
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
        if (_queue.Count > 0) System.Windows.Application.Current.Dispatcher.BeginInvoke(StartNext, DispatcherPriority.Background); // guarded: no double start
    }

    private void Log(BreakRule r, BreakOutcome outcome, TimeSpan actual)
    {
        try { History.Append(new HistoryEntry(Clock.Now, r.Id, r.Name, outcome, actual)); }
        catch (Exception e) { App.Log("history append failed: " + e.Message); }
    }

    public string NextMessage(BreakRule r)
    {
        if (r.Messages.Count == 0) return Settings.Texts.DefaultMessage;
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

        var t = Settings.Texts;
        var actions = new List<NotificationAction>();
        if (r.Warning.ShowStartNowButton) actions.Add(new("now", t.StartNowButton, Primary: true));
        if (r.Warning.ShowPostponeButton && r.AllowPostpone)
            actions.Add(new("postpone", Templates.Render(t.PostponeButton, new Dictionary<string, string> { ["time"] = Format.Duration(r.PostponeLength) })));
        if (r.Warning.ShowSkipNextButton && r.AllowSkip) actions.Add(new("skip", t.SkipButton));

        var values = new Dictionary<string, string>
        {
            ["rule"] = r.Name,
            ["time"] = Format.Duration(w.TimeLeft),
            ["duration"] = Format.Duration(r.Duration),
            ["style"] = r.Style switch { BreakStyle.Fullscreen => t.StyleFullscreen, BreakStyle.Window => t.StyleWindow, _ => t.StyleNotification },
        };
        var real = TimeSpan.FromTicks((long)(w.TimeLeft.Ticks / TimeScale));
        var shown = real < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : real > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : real;
        Notifications.Show(new NotificationRequest(
            WarningId(r), NotificationKind.Info, Templates.Render(t.WarningTitle, values),
            Templates.Render(t.WarningBody, values), actions, shown,
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
        var hadPending = Guard.PendingAppliesAt is not null;
        var weakening = Guard.WeakeningChanges(Settings, edited);
        if (weakening.Count > 0)
        {
            // Everything harmless applies now; committed rules and commitment settings wait for the cooldown in force NOW.
            Guard.Schedule(edited, Settings.Commitment.Cooldown);
            ApplySettings(CommitmentGuard.ApplyNowPart(Settings, edited));
            return $"Commitment mode: your other changes are saved. These apply at {Guard.PendingAppliesAt:HH:mm}:\n• " + string.Join("\n• ", weakening)
                   + (hadPending ? "\n\n(This replaces the earlier pending change.)" : "");
        }
        Guard.CancelPending();
        ApplySettings(edited);
        return hadPending ? "Your earlier pending commitment change was discarded because you saved new settings that keep the commitment." : null;
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
