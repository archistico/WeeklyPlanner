namespace WeeklyPlanner.Core.Time;

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset Now => DateTimeOffset.Now;
}
