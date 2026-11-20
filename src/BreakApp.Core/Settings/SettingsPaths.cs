using System.Reflection;
using BreakApp.Core.Models;

namespace BreakApp.Core.Settings;

/// <summary>
/// Lists every user-editable leaf setting as a path like "Idle.Threshold" or "Rules[].Flash.PulseAmount".
/// The settings window registers the paths it edits; <see cref="Missing"/> proves nothing is left out of the UI.
/// </summary>
public static class SettingsPaths
{
    private static readonly HashSet<string> Internal = ["Version", "Rules[].Id", "Rules[].Order"];

    /// <summary>Types edited as one unit by a dedicated editor (not walked into).</summary>
    private static readonly HashSet<Type> Atomic = [typeof(TimeWindow)];

    public static IReadOnlyList<string> AllLeaves()
    {
        var list = new List<string>();
        Walk(typeof(AppSettings), "", list);
        return list.Where(p => !Internal.Contains(p)).ToList();
    }

    public static IReadOnlyList<string> Missing(ISet<string> covered) => AllLeaves().Where(p => !covered.Contains(p)).ToList();

    private static void Walk(Type t, string prefix, List<string> list)
    {
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var path = prefix + p.Name;
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var item = type.GetGenericArguments()[0];
                if (IsModel(item) && !Atomic.Contains(item)) Walk(item, path + "[].", list);
                else list.Add(path);
            }
            else if (IsModel(type) && !Atomic.Contains(type)) Walk(type, path + ".", list);
            else list.Add(path);
        }
    }

    private static bool IsModel(Type t) => t.IsClass && t != typeof(string) && t.Namespace == typeof(AppSettings).Namespace;
}
