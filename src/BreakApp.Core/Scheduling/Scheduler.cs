using BreakApp.Core.Models;
using BreakApp.Core.Time;

namespace BreakApp.Core.Scheduling;

/// <summary>
/// The break timer state machine. Call <see cref="Tick"/> about once per second; it returns what the shell should show.
/// Pure logic: no timers, no UI, time comes from <see cref="IClock"/>.
/// </summary>
public sealed class Scheduler
{
    private readonly IClock _clock;
    private AppSettings _settings;
    private readonly Dictionary<Guid, RuleState> _state = [];
    private DateTime _lastTick;

    public Scheduler(AppSettings settings, IClock clock)
    {
        _clock = clock;
        _settings = settings;
        _lastTick = clock.Now;
        foreach (var r in settings.Rules) _state[r.Id] = NewState(r, _lastTick);
    }

    public bool InBreak => _state.Values.Any(s => s.Phase != RulePhase.Counting);

    public void UpdateSettings(AppSettings s)
    {
        var now = _clock.Now;
        var old = new Dictionary<Guid, RuleState>(_state);
        _state.Clear();
        foreach (var r in s.Rules)
        {
            if (old.TryGetValue(r.Id, out var st))
            {
                var prev = _settings.Rules.FirstOrDefault(x => x.Id == r.Id);
                if (r.TimerMode == TimerMode.ClockAligned && (prev is null || prev.TimerMode != TimerMode.ClockAligned || prev.Interval != r.Interval))
                    st.NextAligned = NextBoundary(now, r.Interval);
                _state[r.Id] = st;
            }
            else _state[r.Id] = NewState(r, now);
        }
        _settings = s;
    }

    public IReadOnlyList<SchedulerEvent> Tick(ActivitySnapshot activity, PauseState pause)
    {
        var now = _clock.Now;
        var delta = now - _lastTick;
        _lastTick = now;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;

        var events = new List<SchedulerEvent>();
        if (InBreak) return events;

        // Manual "take break now" wins over everything, even a pause.
        var takeNow = _settings.Rules.FirstOrDefault(r => _state[r.Id].TakeNowFlag);
        if (takeNow is not null)
        {
            var st = _state[takeNow.Id];
            st.TakeNowFlag = false;
            st.Phase = RulePhase.Pending;
            st.MergedIds = [];
            events.Add(new BreakDueEvent(takeNow, []));
            return events;
        }

        if (pause.IsPaused) return events;

        var idle = activity.IdleFor >= _settings.Idle.Threshold;
        var active = ActiveRules(now).ToList();

        foreach (var r in active)
        {
            var st = _state[r.Id];
            switch (r.TimerMode)
            {
                case TimerMode.ActiveTime when !idle:
                case TimerMode.ClockTime:
                    st.Elapsed += delta;
                    break;
            }
        }

        foreach (var r in active)
        {
            var st = _state[r.Id];
            var left = TimeUntil(r, st, now);
            if (!st.Warned && !st.SkipNextFlag && left > TimeSpan.Zero && r.Warning.LeadTime > TimeSpan.Zero
                && left <= r.Warning.LeadTime && (r.Warning.ShowNotification || r.Warning.PlaySound))
            {
                st.Warned = true;
                events.Add(new WarningEvent(r, left));
            }
        }

        var due = active.Where(r => TimeUntil(r, _state[r.Id], now) <= TimeSpan.Zero).ToList();
        foreach (var r in due.Where(r => _state[r.Id].SkipNextFlag).ToList())
        {
            Reset(r, _state[r.Id], now);
            events.Add(new RuleSatisfiedEvent(r, BreakOutcome.Skipped));
            due.Remove(r);
        }

        foreach (var r in due)
        {
            var st = _state[r.Id];
            st.Phase = RulePhase.Pending;
            st.MergedIds = [];
            events.Add(new BreakDueEvent(r, []));
        }
        return events;
    }

    public bool Postpone(Guid ruleId)
    {
        var r = Find(ruleId);
        if (r is null || !_state.TryGetValue(ruleId, out var st)) return false;
        if (!r.AllowPostpone || st.PostponeCount >= r.MaxPostpones) return false;
        var now = _clock.Now;
        if (r.TimerMode == TimerMode.ClockAligned) st.NextAligned = now + r.PostponeLength;
        else st.Elapsed = r.Interval - r.PostponeLength;
        st.PostponeCount++;
        st.Warned = false;
        return true;
    }

    public void SkipNext(Guid ruleId)
    {
        if (_state.TryGetValue(ruleId, out var st)) st.SkipNextFlag = true;
    }

    public void TakeNow(Guid ruleId)
    {
        if (_state.TryGetValue(ruleId, out var st)) st.TakeNowFlag = true;
    }

    public void BreakStarted(Guid ruleId)
    {
        if (_state.TryGetValue(ruleId, out var st)) st.Phase = RulePhase.Running;
    }

    public void BreakFinished(Guid ruleId, BreakOutcome outcome)
    {
        var r = Find(ruleId);
        if (r is null || !_state.TryGetValue(ruleId, out var st)) return;
        var now = _clock.Now;
        var merged = st.MergedIds;
        st.Phase = RulePhase.Counting;
        st.MergedIds = [];

        if (outcome == BreakOutcome.Postponed)
        {
            if (!Postpone(ruleId)) Reset(r, st, now);
        }
        else Reset(r, st, now);

        foreach (var id in merged)
        {
            var mr = Find(id);
            if (mr is null || !_state.TryGetValue(id, out var ms)) continue;
            ms.Phase = RulePhase.Counting;
            if (outcome == BreakOutcome.Postponed)
            {
                // Keep it due together with the postponed primary.
                if (mr.TimerMode == TimerMode.ClockAligned) ms.NextAligned = now + r.PostponeLength;
                else ms.Elapsed = mr.Interval - r.PostponeLength;
                ms.Warned = false;
            }
            else Reset(mr, ms, now);
        }
    }

    public (BreakRule Rule, TimeSpan In)? NextBreak()
    {
        var now = _clock.Now;
        (BreakRule Rule, TimeSpan In)? best = null;
        foreach (var r in ActiveRules(now))
        {
            var left = TimeUntil(r, _state[r.Id], now);
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            if (best is null || left < best.Value.In) best = (r, left);
        }
        return best;
    }

    public TimeSpan TimeUntil(Guid ruleId)
    {
        var r = Find(ruleId);
        return r is null || !_state.TryGetValue(ruleId, out var st) ? TimeSpan.MaxValue : TimeUntil(r, st, _clock.Now);
    }

    // ---------------------------------------------------------------- helpers

    private BreakRule? Find(Guid id) => _settings.Rules.FirstOrDefault(r => r.Id == id);

    private IEnumerable<BreakRule> ActiveRules(DateTime now) =>
        _settings.Rules.Where(r => r.Enabled && _state.ContainsKey(r.Id)
                                   && (r.ActiveWindows.Count == 0 || r.ActiveWindows.Any(w => w.Contains(now))));

    private static TimeSpan TimeUntil(BreakRule r, RuleState st, DateTime now) =>
        r.TimerMode == TimerMode.ClockAligned ? st.NextAligned - now : r.Interval - st.Elapsed;

    private static RuleState NewState(BreakRule r, DateTime now) => new() { NextAligned = NextBoundary(now, r.Interval) };

    private static void Reset(BreakRule r, RuleState st, DateTime now)
    {
        st.Elapsed = TimeSpan.Zero;
        st.Warned = false;
        st.PostponeCount = 0;
        st.SkipNextFlag = false;
        st.NextAligned = NextBoundary(now, r.Interval);
    }

    /// <summary>First multiple of <paramref name="interval"/> after local midnight that is strictly later than <paramref name="now"/>.</summary>
    internal static DateTime NextBoundary(DateTime now, TimeSpan interval)
    {
        var midnight = now.Date;
        var k = (now - midnight).Ticks / interval.Ticks + 1;
        return midnight + TimeSpan.FromTicks(k * interval.Ticks);
    }
}
