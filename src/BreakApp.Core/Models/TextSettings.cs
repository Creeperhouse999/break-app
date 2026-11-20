namespace BreakApp.Core.Models;

/// <summary>
/// Every piece of text the app shows, editable. Placeholders: {rule}, {time}, {duration}, {style}, {rules}, {hotkey}.
/// Hints may be left blank to hide them; other blank texts fall back to the default.
/// </summary>
public sealed class TextSettings
{
    public string BrandName { get; set; } = "Break App";
    /// <summary>Warning notification title. {rule}, {time} (time left).</summary>
    public string WarningTitle { get; set; } = "{rule} in {time}";
    /// <summary>Warning notification body. {duration}, {style}.</summary>
    public string WarningBody { get; set; } = "{duration} break · {style}";
    public string StartNowButton { get; set; } = "Start now";
    /// <summary>{time} = postpone length.</summary>
    public string PostponeButton { get; set; } = "Postpone {time}";
    /// <summary>Skip button on warnings and notification breaks.</summary>
    public string SkipButton { get; set; } = "Skip";
    /// <summary>Skip button on window / fullscreen breaks.</summary>
    public string SkipBreakButton { get; set; } = "Skip break";
    /// <summary>{rules} = names of smaller breaks merged into this one.</summary>
    public string AlsoCovers { get; set; } = "Also covers: {rules}";
    public string HintSkip { get; set; } = "Press Esc to skip.";
    /// <summary>{hotkey} = escape key combo.</summary>
    public string HintLocked { get; set; } = "Input is locked. Press {hotkey} to end early.";
    public string HintTotal { get; set; } = "Locked until the timer ends.";
    /// <summary>Shown when a rule has no messages of its own.</summary>
    public string DefaultMessage { get; set; } = "Time for a break.";
    public string StyleNotification { get; set; } = "notification";
    public string StyleWindow { get; set; } = "window";
    public string StyleFullscreen { get; set; } = "fullscreen";

    /// <summary>Names of the texts that may be blank on purpose.</summary>
    public static readonly string[] MayBeBlank = [nameof(HintSkip), nameof(HintLocked), nameof(HintTotal)];
}
