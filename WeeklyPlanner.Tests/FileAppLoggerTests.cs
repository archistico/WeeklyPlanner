using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.Core.Time;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class FileAppLoggerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-logger-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Logger_writes_json_line_and_redacts_sensitive_properties()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        await using var logger = CreateLogger(clock);

        logger.Information(
            "card.saved",
            "Card salvata.",
            new Dictionary<string, object?>
            {
                ["cardId"] = 42,
                ["Title"] = "Contenuto riservato",
                ["Notes"] = "Note riservate",
            });
        await logger.FlushAsync();

        var logPath = Assert.Single(Directory.GetFiles(_tempDirectory, "*.log"));
        var content = await File.ReadAllTextAsync(logPath);

        Assert.Contains("card.saved", content, StringComparison.Ordinal);
        Assert.Contains("\"cardId\":\"42\"", content, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Contenuto riservato", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Note riservate", content, StringComparison.Ordinal);
        Assert.True(logger.IsAvailable);
        Assert.Equal(logPath, logger.LastLogFilePath);
    }

    [Fact]
    public async Task Logger_rolls_when_maximum_file_size_is_reached()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        await using var logger = CreateLogger(clock, maximumFileSizeBytes: 1024);
        var longMessage = new string('x', 900);

        logger.Information("test.first", longMessage);
        logger.Information("test.second", longMessage);
        await logger.FlushAsync();

        var files = Directory.GetFiles(_tempDirectory, "weeklyplanner-20260714-*.log");
        Assert.Equal(2, files.Length);
    }

    [Fact]
    public async Task Logger_continues_sequence_from_existing_rotated_file()
    {
        Directory.CreateDirectory(_tempDirectory);
        var existingPath = Path.Combine(_tempDirectory, "weeklyplanner-20260714-007.log");
        await File.WriteAllTextAsync(existingPath, new string('x', 1024));

        var clock = new MutableClock(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        await using var logger = CreateLogger(clock, maximumFileSizeBytes: 1024);

        logger.Information("test.sequence", "Verifica prosecuzione sequenza.");
        await logger.FlushAsync();

        Assert.True(File.Exists(Path.Combine(_tempDirectory, "weeklyplanner-20260714-008.log")));
    }

    [Fact]
    public async Task Logger_removes_files_older_than_retention()
    {
        Directory.CreateDirectory(_tempDirectory);
        var oldPath = Path.Combine(_tempDirectory, "weeklyplanner-20260601-001.log");
        await File.WriteAllTextAsync(oldPath, "old");
        File.SetLastWriteTimeUtc(oldPath, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var clock = new MutableClock(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        await using var logger = CreateLogger(clock, retentionDays: 14);
        logger.Information("test.retention", "Verifica retention.");
        await logger.FlushAsync();

        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public async Task Logger_failure_does_not_throw_and_is_exposed_in_diagnostics()
    {
        Directory.CreateDirectory(_tempDirectory);
        var blockingFile = Path.Combine(_tempDirectory, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "x");
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        var options = new FileAppLoggerOptions
        {
            LogDirectoryPath = Path.Combine(blockingFile, "logs"),
        };
        await using var logger = new FileAppLogger(options, clock);

        logger.Information("test.failure", "Questa scrittura deve fallire senza propagare eccezioni.");
        await logger.FlushAsync();

        Assert.False(logger.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(logger.LastFailureMessage));
    }

    private FileAppLogger CreateLogger(
        IClock clock,
        long maximumFileSizeBytes = 5 * 1024 * 1024,
        int retentionDays = 14) =>
        new(
            new FileAppLoggerOptions
            {
                LogDirectoryPath = _tempDirectory,
                MaximumFileSizeBytes = maximumFileSizeBytes,
                RetentionDays = retentionDays,
                QueueCapacity = 64,
            },
            clock);

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
