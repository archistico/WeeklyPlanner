namespace WeeklyPlanner.App.Diagnostics;

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    public string LogDirectoryPath => string.Empty;

    public string? LastLogFilePath => null;

    public string? LastFailureMessage => null;

    public bool IsAvailable => false;

    private NullAppLogger()
    {
    }

    public void Log(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        string? errorReference = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
