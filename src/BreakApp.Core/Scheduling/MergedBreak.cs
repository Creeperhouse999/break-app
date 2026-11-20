using BreakApp.Core.Models;
using BreakApp.Core.Settings;

namespace BreakApp.Core.Scheduling;

public static class MergedBreak
{
    /// <summary>
    /// The rule to actually show when smaller rules were merged into <paramref name="primary"/> (bigger-wins collision):
    /// longest duration and the STRICTEST lock / skip / postpone of the group, so merging can never weaken a break
    /// (e.g. a TOTAL LOCK merged into a Locked break stays a TOTAL LOCK). Returns a copy; rules are not changed.
    /// </summary>
    public static BreakRule Effective(BreakRule primary, IReadOnlyList<BreakRule> merged)
    {
        if (merged.Count == 0) return primary;
        var e = SettingsStore.CloneRule(primary);
        foreach (var m in merged)
        {
            if (m.Lock > e.Lock) e.Lock = m.Lock;
            if (m.Duration > e.Duration) e.Duration = m.Duration;
            e.AllowSkip &= m.AllowSkip;
            e.AllowPostpone &= m.AllowPostpone;
        }
        return e;
    }
}
