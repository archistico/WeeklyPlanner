namespace WeeklyPlanner.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
