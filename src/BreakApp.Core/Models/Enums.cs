namespace BreakApp.Core.Models;

public enum TimerMode { ActiveTime, ClockTime, ClockAligned }
public enum BreakStyle { Notification, Window, Fullscreen }
public enum LockLevel { Skippable, LockedWithKeybind, Total }
public enum CollisionPolicy { BiggerWins, Sequential, ResetAll }
public enum PauseTimerPolicy { Freeze, Reset }
public enum ThemeMode { Auto, Light, Dark }
public enum Accent { Blue, Indigo, Teal, Violet, Rose }
public enum Corner { BottomRight, TopRight, BottomLeft, TopLeft }
public enum CountdownFormat { MinSec, Ring, Bar }
public enum MonitorMode { All, Primary, Specific }
public enum MessageOrder { Random, Sequential }

public enum BreakOutcome
{
    Taken,
    Skipped,
    Postponed,
    SatisfiedByIdle,
    MergedIntoBigger,
    InterruptedByMeeting,
    EndedByKeybind,
    SuppressedByPause,
}
