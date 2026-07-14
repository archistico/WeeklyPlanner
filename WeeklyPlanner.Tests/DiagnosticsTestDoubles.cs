using WeeklyPlanner.App.Diagnostics;

namespace WeeklyPlanner.Tests;

internal sealed class RecordingAppLogger : IAppLogger
{
    public List<RecordedLog> Entries { get; } = [];

    public string LogDirectoryPath { get; set; } = Path.Combine(Path.GetTempPath(), "weeklyplanner-test-logs");

    public string? LastLogFilePath { get; set; }

    public string? LastFailureMessage { get; set; }

    public bool IsAvailable { get; set; } = true;

    public void Log(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        string? errorReference = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        Entries.Add(new RecordedLog(
            level,
            eventName,
            message,
            exception,
            errorReference,
            properties ?? new Dictionary<string, object?>()));
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal sealed record RecordedLog(
        AppLogLevel Level,
        string EventName,
        string Message,
        Exception? Exception,
        string? ErrorReference,
        IReadOnlyDictionary<string, object?> Properties);
}

internal sealed class FixedErrorReferenceGenerator(string value) : IErrorReferenceGenerator
{
    public string Create() => value;
}
