namespace BreakApp.Core.Time;

/// <summary>Local wall-clock time source, injectable for tests.</summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
