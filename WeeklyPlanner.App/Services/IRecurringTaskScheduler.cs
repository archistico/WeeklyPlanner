namespace WeeklyPlanner.App.Services;

public interface IRecurringTaskScheduler : IDisposable
{
    TimeSpan Interval { get; set; }

    bool IsRunning { get; }

    void Start(Func<CancellationToken, Task> callback, CancellationToken cancellationToken);

    void Stop();
}
