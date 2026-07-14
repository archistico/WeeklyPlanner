namespace WeeklyPlanner.App.Diagnostics;

public interface IAppLogger : IAsyncDisposable
{
    string LogDirectoryPath { get; }

    string? LastLogFilePath { get; }

    string? LastFailureMessage { get; }

    bool IsAvailable { get; }

    void Log(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        string? errorReference = null,
        IReadOnlyDictionary<string, object?>? properties = null);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
