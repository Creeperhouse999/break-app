namespace BreakApp.Core.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<BreakRule> Rules { get; set; } = [];
    public CollisionPolicy Collision { get; set; } = CollisionPolicy.BiggerWins;
    public TimeSpan CollisionWindow { get; set; } = TimeSpan.FromMinutes(1);
    public IdleSettings Idle { get; set; } = new();
    public PauseSettings Pause { get; set; } = new();
    public LockSettings Lock { get; set; } = new();
    public CommitmentSettings Commitment { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public SoundSettings Sound { get; set; } = new();
    public GeneralSettings General { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Rules =
        [
            new BreakRule
            {
                Name = "Eye rest",
                Interval = TimeSpan.FromMinutes(20),
                Duration = TimeSpan.FromSeconds(20),
                Style = BreakStyle.Notification,
                TimerMode = TimerMode.ActiveTime,
                Lock = LockLevel.Skippable,
                Warning = new WarningSettings { ShowNotification = false, PlaySound = false },
                Messages = ["Look at something 6 metres away for 20 seconds.", "Blink slowly a few times and relax your eyes.", "Look out of the window."],
                Order = 0,
            },
            new BreakRule
            {
                Name = "Stretch",
                Interval = TimeSpan.FromMinutes(60),
                Duration = TimeSpan.FromMinutes(5),
                Style = BreakStyle.Fullscreen,
                TimerMode = TimerMode.ActiveTime,
                Lock = LockLevel.Skippable,
                Warning = new WarningSettings { LeadTime = TimeSpan.FromSeconds(30) },
                Messages = ["Stand up and stretch your arms over your head.", "Roll your shoulders and neck gently.", "Walk around and get some water."],
                Order = 1,
            },
        ],
    };
}

public sealed class IdleSettings
{
    public bool NaturalBreaks { get; set; } = true;
    public TimeSpan Threshold { get; set; } = TimeSpan.FromMinutes(3);
    public bool DetectKeyboardMouse { get; set; } = true;
    public bool DetectGamepad { get; set; } = true;
    public bool DetectMedia { get; set; } = true;
}

public sealed class PauseSettings
{
    public bool DetectMicCamera { get; set; } = true;
    public List<string> MeetingProcesses { get; set; } = ["zoom", "teams", "ms-teams"];
    public List<string> MeetingTitlePatterns { get; set; } = ["Meet -", "Zoom Meeting"];
    public List<TimeWindow> QuietTimes { get; set; } = [];
    public bool PauseDuringFullscreen { get; set; }
    public bool RespectDoNotDisturb { get; set; }
    public PauseTimerPolicy TimersDuringPause { get; set; } = PauseTimerPolicy.Freeze;
}

public sealed class LockSettings
{
    public string EscapeHotkey { get; set; } = "Ctrl+Shift+Alt+B";
}

public sealed class CommitmentSettings
{
    public bool Enabled { get; set; }
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed class AppearanceSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    public Accent Accent { get; set; } = Accent.Blue;
    public double Opacity { get; set; } = 0.95;
    public double FontScale { get; set; } = 1.0;
    public Corner NotificationCorner { get; set; } = Corner.BottomRight;
    public CountdownFormat Countdown { get; set; } = CountdownFormat.Ring;
    public string? BackgroundImage { get; set; }
    public bool ReduceMotion { get; set; }
    public MonitorMode Monitors { get; set; } = MonitorMode.All;
    public List<string> SpecificMonitors { get; set; } = [];
}

public sealed class SoundSettings
{
    public bool Mute { get; set; }
    public double Volume { get; set; } = 0.6;
    /// <summary>null = built-in chime, "" = none, otherwise a file path.</summary>
    public string? WarningSound { get; set; }
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;
    public string PauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string BreakNowHotkey { get; set; } = "Ctrl+Alt+B";
    public bool AdvancedMode { get; set; }
}
