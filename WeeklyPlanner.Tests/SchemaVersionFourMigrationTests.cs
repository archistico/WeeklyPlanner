using Dapper;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SchemaVersionFourMigrationTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-v4-migration-tests-{Guid.NewGuid():N}.db");
    private readonly string _backupDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-v4-migration-backups-{Guid.NewGuid():N}");

    [Fact]
    public void Upgrade_from_v3_backfills_stable_metadata_and_import_event()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        CreateVersionThreeDatabase(factory);
        var initializer = CreateInitializer(factory);

        initializer.EnsureInitialized();

        using var connection = factory.Create();
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion,
            connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        var card = connection.QuerySingle<Card>(
            """
            SELECT Id, ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated,
                   PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc,
                   Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version
            FROM Cards WHERE Id = 1;
            """);
        Assert.False(string.IsNullOrWhiteSpace(card.StableId));
        Assert.Equal("2026-07-14T08:30:00.0000000Z", card.CreatedAtUtc);
        Assert.True(card.CreatedAtIsEstimated);
        Assert.Null(card.PriorityId);
                Assert.NotNull(card.CardTypeId);
        Assert.Equal(
            "generic",
            connection.ExecuteScalar<string>(
                "SELECT SystemKey FROM CardTypes WHERE Id = @CardTypeId;",
                new { card.CardTypeId }));

        var imported = connection.QuerySingle<CardEvent>(
            "SELECT * FROM CardEvents WHERE CardId = 1 AND EventType = 'Imported';");
        Assert.Equal(card.StableId, imported.CardStableId);
        Assert.Equal("Emilie", imported.UserName);
        Assert.Contains("estimated", imported.DataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Single(
            Directory.GetFiles(_backupDirectory, "weeklyplanner-migration-v3-to-v5-*.db"));
    }

    [Fact]
    public void Stable_identifier_is_required_unique_and_immutable()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        CreateInitializer(factory).EnsureInitialized();

        using var connection = factory.Create();
        var genericTypeId = connection.ExecuteScalar<long>(
            "SELECT Id FROM CardTypes WHERE SystemKey = 'generic';");
        var missingStableId = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => connection.Execute(
            """
            INSERT INTO Cards
                (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, CardTypeId, Title, SortOrder,
                 CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (0, NULL, @Now, 0, @GenericTypeId, 'Invalid', 0, 'Test', 'Test', @Now, 1);
            """,
            new { Now = "2026-07-15T10:00:00.0000000Z", GenericTypeId = genericTypeId }));
        Assert.Contains("StableId", missingStableId.Message, StringComparison.OrdinalIgnoreCase);

        connection.Execute(
            """
            INSERT INTO Cards
                (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, CardTypeId, Title, SortOrder,
                 CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (0, 'stable-1', @Now, 0, @GenericTypeId, 'Valid', 0, 'Test', 'Test', @Now, 1);
            """,
            new { Now = "2026-07-15T10:00:00.0000000Z", GenericTypeId = genericTypeId });

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => connection.Execute(
            "UPDATE Cards SET StableId = 'stable-2' WHERE StableId = 'stable-1';"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => connection.Execute(
            """
            INSERT INTO Cards
                (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, CardTypeId, Title, SortOrder,
                 CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (0, 'stable-1', @Now, 0, @GenericTypeId, 'Duplicate', 1, 'Test', 'Test', @Now, 1);
            """,
            new { Now = "2026-07-15T10:00:00.0000000Z", GenericTypeId = genericTypeId }));
    }

    private DatabaseInitializer CreateInitializer(SqliteConnectionFactory factory)
    {
        var integrity = new SqliteDatabaseIntegrityChecker();
        return new DatabaseInitializer(
            factory,
            new EmbeddedDatabaseMigrationCatalog(),
            integrity,
            new SqliteDatabaseMigrationBackupService(
                new DatabaseMigrationBackupOptions
                {
                    BackupDirectory = _backupDirectory,
                    RetentionCount = 5,
                },
                integrity));
    }

    private static void CreateVersionThreeDatabase(SqliteConnectionFactory factory)
    {
        using var connection = factory.Create();
        var migrations = new EmbeddedDatabaseMigrationCatalog()
            .ReadMigrations()
            .Where(item => item.Version <= 3)
            .OrderBy(item => item.Version);

        foreach (var migration in migrations)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: transaction);
            connection.Execute("DELETE FROM SchemaVersion;", transaction: transaction);
            connection.Execute(
                "INSERT INTO SchemaVersion (Version) VALUES (@Version);",
                new { migration.Version },
                transaction);
            transaction.Commit();
        }

        connection.Execute(
            """
            INSERT INTO Cards
                (Id, ColumnId, Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (1, 0, 'Card esistente', 'Note', 0, 'Emilie', 'Emilie',
                 '2026-07-14T08:30:00.0000000Z', 1);
            """);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_backupDirectory))
        {
            Directory.Delete(_backupDirectory, recursive: true);
        }
    }
}
