namespace BreakApp.Core.Models;

/// <summary>One "every X, break for Y" rule. Mutable POCO so the settings UI can bind to it directly.</summary>
public sealed class BreakRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Break";
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);
    public TimerMode TimerMode { get; set; } = TimerMode.ActiveTime;
    public BreakStyle Style { get; set; } = BreakStyle.Notification;
    public LockLevel Lock { get; set; } = LockLevel.Skippable;
    public WarningSettings Warning { get; set; } = new();
    public bool AllowPostpone { get; set; } = true;
    public TimeSpan PostponeLength { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxPostpones { get; set; } = 2;
    public bool AllowSkip { get; set; } = true;
    public List<string> Messages { get; set; } = [];
    public MessageOrder MessageOrder { get; set; } = MessageOrder.Random;
    public FlashSettings Flash { get; set; } = new();
    /// <summary>null = built-in chime, "" = no sound, otherwise a file path.</summary>
    public string? StartSound { get; set; }
    /// <summary>null = built-in chime, "" = no sound, otherwise a file path.</summary>
    public string? EndSound { get; set; }
    public AppearanceOverride? Appearance { get; set; }
    /// <summary>Empty = rule is active at all times.</summary>
    public List<TimeWindow> ActiveWindows { get; set; } = [];
    public bool? CommitmentOverride { get; set; }
    public bool EndOnMeeting { get; set; } = true;
    public int Order { get; set; }
}

public sealed class WarningSettings
{
    public bool ShowNotification { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public TimeSpan LeadTime { get; set; } = TimeSpan.FromSeconds(30);
    public bool ShowPostponeButton { get; set; } = true;
    public bool ShowSkipNextButton { get; set; } = true;
    public bool ShowStartNowButton { get; set; } = true;
}

public sealed class FlashSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>null = use the accent colour. Otherwise "#RRGGBB".</summary>
    public string? Color { get; set; }
    public double SpeedHz { get; set; } = 2;
    /// <summary>0 = flash until the notification closes.</summary>
    public int Count { get; set; } = 3;
}

public sealed class AppearanceOverride
{
    public Accent? Accent { get; set; }
    public ThemeMode? Theme { get; set; }
    public double? Opacity { get; set; }
    public string? BackgroundImage { get; set; }
}

/// <summary>A weekly time window. When End &lt;= Start the window wraps past midnight and belongs to its start day.</summary>
public sealed class TimeWindow
{
    public List<DayOfWeek> Days { get; set; } = [];
    public TimeOnly Start { get; set; } = new(9, 0);
    public TimeOnly End { get; set; } = new(17, 0);
    public string Label { get; set; } = "";

    public bool Contains(DateTime local)
    {
        var t = TimeOnly.FromDateTime(local);
        if (Start < End)
            return Days.Contains(local.DayOfWeek) && t >= Start && t < End;
        // Wraps midnight: evening part on the start day, early part on the following day.
        if (t >= Start) return Days.Contains(local.DayOfWeek);
        if (t < End) return Days.Contains(local.AddDays(-1).DayOfWeek);
        return false;
    }
}
