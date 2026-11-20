using BreakApp.Core.Time;

namespace BreakApp.Services;

/// <summary>Test clock for BREAKAPP_FAST=1: time runs <see cref="Scale"/> times faster, so a 20 min rule fires after 20 s.</summary>
public sealed class ScaledClock(double scale) : IClock
{
    private readonly DateTime _start = DateTime.Now;
    public double Scale { get; } = scale;
    public DateTime Now => _start + TimeSpan.FromTicks((long)((DateTime.Now - _start).Ticks * Scale));
}
