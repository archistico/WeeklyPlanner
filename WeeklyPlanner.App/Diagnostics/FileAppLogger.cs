using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.App.Diagnostics;

public sealed class FileAppLogger : IAppLogger
{
    private static readonly HashSet<string> SensitivePropertyNames = new(
        [
            "title",
            "notes",
            "cardtitle",
            "cardnotes",
            "content",
            "body",
            "description",
            "text",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly FileAppLoggerOptions _options;
    private readonly IClock _clock;
    private readonly Channel<LogCommand> _commands;
    private readonly Task _worker;
    private int _disposed;
    private volatile bool _isAvailable = true;
    private string? _lastLogFilePath;
    private string? _lastFailureMessage;
    private DateOnly? _lastRetentionDate;

    public string LogDirectoryPath => _options.LogDirectoryPath;

    public string? LastLogFilePath => Volatile.Read(ref _lastLogFilePath);

    public string? LastFailureMessage => Volatile.Read(ref _lastFailureMessage);

    public bool IsAvailable => _isAvailable;

    public FileAppLogger(
        FileAppLoggerOptions? options = null,
        IClock? clock = null)
    {
        _options = options ?? new FileAppLoggerOptions();
        _options.Validate();
        _clock = clock ?? SystemClock.Instance;

        _commands = Channel.CreateBounded<LogCommand>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _worker = Task.Run(ProcessCommandsAsync);
    }

    public void Log(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        string? errorReference = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var record = new AppLogRecord(
            _clock.Now.ToUniversalTime(),
            level.ToString(),
            eventName,
            message,
            errorReference,
            exception is null ? null : new AppExceptionRecord(
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace),
            SanitizeProperties(properties));

        _commands.Writer.TryWrite(new WriteCommand(record));
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            await _worker.WaitAsync(cancellationToken);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new FlushCommand(completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _commands.Writer.TryComplete();
        await _worker;
    }

    private async Task ProcessCommandsAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            switch (command)
            {
                case WriteCommand write:
                    await TryWriteRecordAsync(write.Record);
                    break;

                case FlushCommand flush:
                    flush.Completion.TrySetResult();
                    break;
            }
        }
    }

    private async Task TryWriteRecordAsync(AppLogRecord record)
    {
        try
        {
            Directory.CreateDirectory(_options.LogDirectoryPath);
            RemoveExpiredFilesIfNeeded(record.TimestampUtc);

            var json = JsonSerializer.Serialize(record, JsonOptions);
            var line = json + Environment.NewLine;
            var targetPath = GetTargetLogFile(record.TimestampUtc, Encoding.UTF8.GetByteCount(line));
            await File.AppendAllTextAsync(targetPath, line, new UTF8Encoding(false));

            Volatile.Write(ref _lastLogFilePath, targetPath);
            Volatile.Write(ref _lastFailureMessage, null);
            _isAvailable = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Volatile.Write(ref _lastFailureMessage, ex.Message);
            _isAvailable = false;
        }
    }

    private string GetTargetLogFile(DateTimeOffset timestampUtc, int incomingByteCount)
    {
        var date = timestampUtc.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var matchingFiles = Directory
            .EnumerateFiles(_options.LogDirectoryPath, $"weeklyplanner-{date}-*.log")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchingFiles.Count > 0)
        {
            var lastPath = matchingFiles[^1];
            var length = new FileInfo(lastPath).Length;
            if (length + incomingByteCount <= _options.MaximumFileSizeBytes)
            {
                return lastPath;
            }
        }

        var sequence = matchingFiles.Count == 0
            ? 1
            : ParseSequence(matchingFiles[^1]) + 1;
        return Path.Combine(
            _options.LogDirectoryPath,
            $"weeklyplanner-{date}-{sequence:000}.log");
    }

    private void RemoveExpiredFilesIfNeeded(DateTimeOffset timestampUtc)
    {
        var currentDate = DateOnly.FromDateTime(timestampUtc.UtcDateTime);
        if (_lastRetentionDate == currentDate)
        {
            return;
        }

        _lastRetentionDate = currentDate;
        var threshold = timestampUtc.UtcDateTime.AddDays(-_options.RetentionDays);
        foreach (var filePath in Directory.EnumerateFiles(_options.LogDirectoryPath, "weeklyplanner-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(filePath) < threshold)
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
                // La retention è best effort e non deve interrompere la scrittura corrente.
            }
            catch (UnauthorizedAccessException)
            {
                // La retention è best effort e non deve interrompere la scrittura corrente.
            }
        }
    }

    private static int ParseSequence(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var separatorIndex = fileName.LastIndexOf('-');
        return separatorIndex >= 0 &&
               int.TryParse(
                   fileName[(separatorIndex + 1)..],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var sequence)
            ? sequence
            : 0;
    }

    private static IReadOnlyDictionary<string, string?> SanitizeProperties(
        IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            result[property.Key] = SensitivePropertyNames.Contains(property.Key)
                ? "[REDACTED]"
                : Convert.ToString(property.Value, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private abstract record LogCommand;

    private sealed record WriteCommand(AppLogRecord Record) : LogCommand;

    private sealed record FlushCommand(TaskCompletionSource Completion) : LogCommand;

    private sealed record AppLogRecord(
        DateTimeOffset TimestampUtc,
        string Level,
        string EventName,
        string Message,
        string? ErrorReference,
        AppExceptionRecord? Exception,
        IReadOnlyDictionary<string, string?> Properties);

    private sealed record AppExceptionRecord(
        string Type,
        string Message,
        string? StackTrace);
}
