using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Diagnostics;
using WeeklyPlanner.Core.Time;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ApplicationDiagnosticsProviderTests
{
    [Fact]
    public async Task Snapshot_contains_runtime_database_and_log_information_without_card_content()
    {
        var logger = new RecordingAppLogger
        {
            LogDirectoryPath = Path.Combine(Path.GetTempPath(), "wp-logs"),
            LastLogFilePath = Path.Combine(Path.GetTempPath(), "wp-logs", "weeklyplanner.log"),
        };
        var settingsService = new AppSettingsService(
            Path.Combine(Path.GetTempPath(), "wp-settings", "settings.json"));
        var provider = new ApplicationDiagnosticsProvider(
            logger,
            settingsService,
            new ApplicationSession("1234567890abcdef", "PC-TEST"),
            new StubDatabaseDiagnosticsReader(new DatabaseDiagnosticsInfo(true, 2048, 3, null)),
            new FixedClock(new DateTimeOffset(2026, 7, 14, 20, 30, 0, TimeSpan.Zero)));
        var activeDatabasePath = Path.Combine(Path.GetTempPath(), "wp-data", "weeklyplanner.db");
        var settings = new AppSettings
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "wp-data", "database-next-restart.db"),
            UserName = "Emilie",
            PollingIntervalSeconds = 7,
        };
        var runtime = new BoardRuntimeDiagnostics(
            "Database online",
            new DateTimeOffset(2026, 7, 14, 20, 29, 0, TimeSpan.Zero),
            HasActiveEdits: true,
            ColumnCount: 8,
            CardCount: 15,
            ActiveDatabasePath: activeDatabasePath);

        var snapshot = await provider.CollectAsync(settings, runtime);
        var text = snapshot.ToPlainText();

        Assert.Equal("12345678", snapshot.SessionId);
        Assert.Equal("2 KB", snapshot.DatabaseSize);
        Assert.Equal("3", snapshot.SchemaVersion);
        Assert.Equal("8 colonne, 15 card", snapshot.ContentSummary);
        Assert.Contains("Database online", text, StringComparison.Ordinal);
        Assert.Equal(activeDatabasePath, snapshot.DatabasePath);
        Assert.Contains(activeDatabasePath, text, StringComparison.Ordinal);
        Assert.DoesNotContain("titolo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("note", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logger_failure_is_visible_but_does_not_prevent_snapshot()
    {
        var logger = new RecordingAppLogger
        {
            IsAvailable = false,
            LastFailureMessage = "accesso negato",
        };
        var provider = new ApplicationDiagnosticsProvider(
            logger,
            new AppSettingsService(Path.Combine(Path.GetTempPath(), "settings.json")),
            new ApplicationSession("session", "PC"),
            new StubDatabaseDiagnosticsReader(new DatabaseDiagnosticsInfo(false, null, null, "missing")),
            new FixedClock(DateTimeOffset.UtcNow));
        var settings = new AppSettings
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "weeklyplanner.db"),
            UserName = "Emilie",
            PollingIntervalSeconds = 7,
        };

        var snapshot = await provider.CollectAsync(
            settings,
            new BoardRuntimeDiagnostics("Offline", null, false, 0, 0, settings.DatabasePath));

        Assert.Contains("Non disponibile", snapshot.LogStatus, StringComparison.Ordinal);
        Assert.Contains("accesso negato", snapshot.LogStatus, StringComparison.Ordinal);
    }

    private sealed class StubDatabaseDiagnosticsReader(DatabaseDiagnosticsInfo result)
        : IDatabaseDiagnosticsReader
    {
        public Task<DatabaseDiagnosticsInfo> ReadAsync(
            string databasePath,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
