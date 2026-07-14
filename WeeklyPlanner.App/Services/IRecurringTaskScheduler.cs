namespace WeeklyPlanner.App.Services;

public interface IRecurringTaskScheduler : IAsyncDisposable
{
    TimeSpan Interval { get; set; }

    bool IsRunning { get; }

    bool IsExecuting { get; }

    void Start(Func<CancellationToken, Task> callback, CancellationToken cancellationToken);

    Task StopAsync();
}
