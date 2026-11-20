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
    private bool _wasPaused;
    private TimeSpan _lastIdleFor;

    /// <summary>True while active-time timers are paused because there was no input for <see cref="IdleSettings.FreezeAfter"/>.</summary>
    public bool TimersFrozen { get; private set; }

    /// <summary>Tick gaps longer than this are treated as sleep / clock jumps.</summary>
    private static readonly TimeSpan GapLimit = TimeSpan.FromSeconds(30);

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
        var kept = s.Rules.Select(r => r.Id).ToHashSet();
        // A removed rule that was mid-break releases the rules merged into it, so nothing stays stuck "in a break".
        foreach (var (id, st) in old.Where(kv => !kept.Contains(kv.Key) && kv.Value.Phase != RulePhase.Counting))
            foreach (var m in st.MergedIds)
                if (old.TryGetValue(m, out var ms) && s.Rules.FirstOrDefault(r => r.Id == m) is { } mr)
                {
                    ms.Phase = RulePhase.Counting;
                    Reset(mr, ms, now);
                }
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
            events.Add(new BreakDueEvent(takeNow, [], Manual: true));
            return events;
        }

        if (pause.IsPaused)
        {
            _wasPaused = true;
            return events;
        }
        if (_wasPaused)
        {
            _wasPaused = false;
            if (_settings.Pause.TimersDuringPause == PauseTimerPolicy.Reset)
                foreach (var r in _settings.Rules) Reset(r, _state[r.Id], now);
        }

        // A long gap between ticks means the PC slept or the clock jumped: treat it as an idle stretch of that length.
        var gap = delta > GapLimit ? delta : TimeSpan.Zero;
        var idleFor = activity.IdleFor > gap ? activity.IdleFor : gap;
        var idle = idleFor >= _settings.Idle.Threshold || gap > TimeSpan.Zero;
        // No input for FreezeAfter (or a sleep gap): active-time timers stop and give back the idle seconds they counted.
        var frozen = idleFor >= _settings.Idle.FreezeAfter || gap > TimeSpan.Zero;
        var newInput = idleFor < _lastIdleFor || idleFor == TimeSpan.Zero;
        _lastIdleFor = idleFor;
        TimersFrozen = frozen;
        var natural = _settings.Idle.NaturalBreaks;
        var active = ActiveRules(now).ToList();

        foreach (var r in active.ToList())
        {
            var st = _state[r.Id];
            if (!idle) st.IdleSatisfied = false;

            if (r.TimerMode == TimerMode.ClockAligned)
            {
                if (now < st.NextAligned) continue;
                if (idle)
                {
                    // Boundary passed while away: a long enough absence is the break. Otherwise wait for the user to return.
                    if (natural && idleFor >= r.Duration) Satisfy(r, st, now, events, active);
                    continue;
                }
                if (now >= st.NextAligned + r.Interval) Satisfy(r, st, now, events, active); // came back too late: drop it
                continue;
            }

            var naturalBreak = natural && !st.IdleSatisfied &&
                               (gap >= r.Duration || idleFor >= Max(_settings.Idle.Threshold, r.Duration));
            if (naturalBreak)
            {
                Satisfy(r, st, now, events, active);
                continue;
            }

            if (newInput) st.IdleCounted = TimeSpan.Zero;
            if (r.TimerMode == TimerMode.ClockTime) st.Elapsed += delta;
            else if (!frozen)
            {
                st.Elapsed += delta;
                if (idleFor > TimeSpan.Zero) st.IdleCounted += delta;
            }
            else if (st.IdleCounted > TimeSpan.Zero)
            {
                st.Elapsed -= st.IdleCounted;
                if (st.Elapsed < TimeSpan.Zero) st.Elapsed = TimeSpan.Zero;
                st.IdleCounted = TimeSpan.Zero;
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

        // Clock-aligned rules wait for the user to come back before firing.
        var due = active.Where(r => TimeUntil(r, _state[r.Id], now) <= TimeSpan.Zero
                                    && !(idle && r.TimerMode == TimerMode.ClockAligned)).ToList();
        foreach (var r in due.Where(r => _state[r.Id].SkipNextFlag).ToList())
        {
            Reset(r, _state[r.Id], now);
            events.Add(new RuleSatisfiedEvent(r, BreakOutcome.Skipped));
            due.Remove(r);
        }

        if (due.Count == 0) return events;

        // Rules due within the collision window join this break instead of interrupting again right after it.
        var group = due.Concat(active.Where(r => !due.Contains(r) && !_state[r.Id].SkipNextFlag
                                                 && TimeUntil(r, _state[r.Id], now) <= _settings.CollisionWindow)).ToList();

        if (_settings.Collision == CollisionPolicy.Sequential)
        {
            foreach (var r in group.OrderBy(r => r.Duration))
            {
                var st = _state[r.Id];
                st.Phase = RulePhase.Pending;
                st.MergedIds = [];
                events.Add(new BreakDueEvent(r, []));
            }
            return events;
        }

        var primary = group.OrderByDescending(r => r.Duration).ThenByDescending(r => (int)r.Lock).ThenByDescending(r => (int)r.Style).First();
        var merged = group.Where(r => r != primary).ToList();
        foreach (var r in group) _state[r.Id].Phase = RulePhase.Pending;
        _state[primary.Id].MergedIds = merged.Select(r => r.Id).ToList();
        events.Add(new BreakDueEvent(primary, merged));
        return events;
    }

    private void Satisfy(BreakRule r, RuleState st, DateTime now, List<SchedulerEvent> events, List<BreakRule> active)
    {
        Reset(r, st, now);
        st.IdleSatisfied = true;
        events.Add(new RuleSatisfiedEvent(r, BreakOutcome.SatisfiedByIdle));
        active.Remove(r);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public bool CanPostpone(Guid ruleId)
    {
        var r = Find(ruleId);
        return r is not null && _state.TryGetValue(ruleId, out var st) && r.AllowPostpone && st.PostponeCount < r.MaxPostpones;
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

        if (_settings.Collision == CollisionPolicy.ResetAll && outcome != BreakOutcome.Postponed)
            foreach (var other in _settings.Rules)
                if (_state.TryGetValue(other.Id, out var os) && os.Phase == RulePhase.Counting) Reset(other, os, now);
    }

    /// <summary>
    /// Safety net: any rule marked as pending or running that the shell is not showing or queueing
    /// (<paramref name="showing"/> = primary rules running or queued) is reset, so breaks can never stop for good.
    /// </summary>
    public void ReleaseOrphans(IEnumerable<Guid> showing)
    {
        var keep = new HashSet<Guid>();
        foreach (var id in showing)
        {
            keep.Add(id);
            if (_state.TryGetValue(id, out var st)) keep.UnionWith(st.MergedIds);
        }
        var now = _clock.Now;
        foreach (var r in _settings.Rules)
        {
            if (!_state.TryGetValue(r.Id, out var st) || st.Phase == RulePhase.Counting || keep.Contains(r.Id)) continue;
            st.Phase = RulePhase.Counting;
            st.MergedIds = [];
            Reset(r, st, now);
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
