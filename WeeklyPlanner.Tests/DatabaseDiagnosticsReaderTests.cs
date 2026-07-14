using Dapper;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Diagnostics;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseDiagnosticsReaderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-diagnostics-db-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Missing_database_is_reported_without_creating_it()
    {
        var databasePath = Path.Combine(_tempDirectory, "missing", "weeklyplanner.db");
        var reader = new DatabaseDiagnosticsReader();

        var result = await reader.ReadAsync(databasePath);

        Assert.False(result.FileExists);
        Assert.Null(result.SchemaVersion);
        Assert.False(File.Exists(databasePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(databasePath)));
    }

    [Fact]
    public async Task Existing_database_reports_size_and_schema_version()
    {
        var databasePath = Path.Combine(_tempDirectory, "weeklyplanner.db");
        var factory = new SqliteConnectionFactory(databasePath);
        new DatabaseInitializer(factory).EnsureInitialized();
        using (var connection = factory.Create())
        {
            connection.Execute("INSERT INTO Cards (ColumnId, Title, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc) VALUES (0, 'Test', 0, 'Emilie', 'Emilie', '2026-07-14T20:00:00.0000000Z');");
        }

        var reader = new DatabaseDiagnosticsReader();
        var result = await reader.ReadAsync(databasePath);

        Assert.True(result.FileExists);
        Assert.True(result.FileSizeBytes > 0);
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, result.SchemaVersion);
        Assert.Null(result.ErrorMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
