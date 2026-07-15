using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SchemaVersionFiveMigrationTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-v5-migration-{Guid.NewGuid():N}.db");
    private readonly string _backupDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-v5-backups-{Guid.NewGuid():N}");

    [Fact]
    public void Upgrade_from_v4_maps_weekly_columns_to_the_fixed_kanban_workflow()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        CreateVersionFourDatabase(factory);

        CreateInitializer(factory).EnsureInitialized();

        using var connection = factory.Create();
        Assert.Equal(5, connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));

        var columns = connection.Query<Column>(
            "SELECT Id, Name, SortOrder, SystemKey, IsSystem FROM Columns ORDER BY SortOrder;")
            .ToList();
        Assert.Equal(
            new[]
            {
                WorkflowColumnKeys.Backlog,
                WorkflowColumnKeys.Todo,
                WorkflowColumnKeys.InProgress,
                WorkflowColumnKeys.Testing,
                WorkflowColumnKeys.Done,
            },
            columns.Select(item => item.SystemKey!));
        Assert.All(columns, item => Assert.True(item.IsSystem));
        Assert.Equal(
            new[] { "BACKLOG", "TODO", "IN PROGRESS", "TESTING", "DONE" },
            columns.Select(item => item.Name));

        var cards = connection.Query<Card>(
            """
            SELECT Id, ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated,
                   PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc,
                   Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version
            FROM Cards ORDER BY ColumnId, SortOrder, Id;
            """).ToList();
        Assert.Equal(0, Assert.Single(cards, item => item.Title == "Backlog").ColumnId);

        var todoCards = cards.Where(item => item.ColumnId == 1).ToList();
        Assert.Equal(
            new[] { "Lunedì A", "Lunedì B", "Martedì" },
            todoCards.Select(item => item.Title));
        Assert.Equal(Enumerable.Range(0, todoCards.Count), todoCards.Select(item => item.SortOrder));

        var generic = connection.QuerySingle<CardTypeDefinition>(
            """
            SELECT Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version, SystemKey, IsSystem
            FROM CardTypes WHERE SystemKey = 'generic';
            """);
        Assert.Equal("Generica", generic.Name);
        Assert.Equal(0, generic.SortOrder);
        Assert.True(generic.IsActive);
        Assert.True(generic.IsDefault);
        Assert.True(generic.IsSystem);
        Assert.All(
            cards.Where(item => item.Title != "Martedì"),
            item => Assert.Equal(generic.Id, item.CardTypeId!.Value));

        var migratedEvents = connection.Query<CardEvent>(
            "SELECT * FROM CardEvents WHERE EventType = 'WorkflowMigrated' ORDER BY CardId;")
            .ToList();
        Assert.Equal(3, migratedEvents.Count);
        Assert.All(migratedEvents, item => Assert.Contains("TODO", item.Summary, StringComparison.Ordinal));
        Assert.Contains(migratedEvents, item => item.DataJson.Contains("Lunedì", StringComparison.Ordinal));
        Assert.Contains(migratedEvents, item => item.DataJson.Contains("Martedì", StringComparison.Ordinal));

        var typeEvents = connection.Query<CardEvent>(
            "SELECT * FROM CardEvents WHERE EventType = 'TypeMigrated' ORDER BY CardId;")
            .ToList();
        Assert.Equal(3, typeEvents.Count);
        Assert.All(typeEvents, item => Assert.Contains("generic", item.DataJson, StringComparison.Ordinal));

        Assert.Single(
            Directory.GetFiles(_backupDirectory, "weeklyplanner-migration-v4-to-v5-*.db"));
        Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check;"));
        Assert.Empty(connection.Query("PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void Upgrade_promotes_an_existing_generic_type_without_duplication()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        CreateVersionFourDatabase(factory);

        using (var connection = factory.Create())
        {
            connection.Execute(
                """
                INSERT INTO CardTypes
                    (Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version)
                VALUES
                    (20, 'generica', '#334155', 5, 0, 0, 1);
                """);
            connection.Execute(
                "UPDATE Cards SET CardTypeId = 20 WHERE Id = 1;");
        }

        CreateInitializer(factory).EnsureInitialized();

        using var verify = factory.Create();
        var genericTypes = verify.Query<CardTypeDefinition>(
            """
            SELECT Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version, SystemKey, IsSystem
            FROM CardTypes
            WHERE SystemKey = 'generic' OR Name = 'Generica' COLLATE NOCASE;
            """).ToList();
        var generic = Assert.Single(genericTypes);
        Assert.Equal(20, generic.Id);
        Assert.Equal("Generica", generic.Name);
        Assert.Equal("#334155", generic.ColorHex);
        Assert.Equal(0, generic.SortOrder);
        Assert.True(generic.IsActive);
        Assert.True(generic.IsDefault);
        Assert.True(generic.IsSystem);
        Assert.Equal(20, verify.ExecuteScalar<long>("SELECT CardTypeId FROM Cards WHERE Id = 1;"));
        Assert.Equal("ok", verify.ExecuteScalar<string>("PRAGMA integrity_check;"));
        Assert.Empty(verify.Query("PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void Schema_v5_requires_a_type_and_protects_system_workflow_columns()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        CreateInitializer(factory).EnsureInitialized();

        using var connection = factory.Create();
        var now = "2026-07-15T12:00:00.0000000Z";
        var missingType = Assert.Throws<SqliteException>(() => connection.Execute(
            """
            INSERT INTO Cards
                (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, CardTypeId,
                 Title, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (0, 'missing-type', @Now, 0, NULL,
                 'Non valida', 0, 'Test', 'Test', @Now, 1);
            """,
            new { Now = now }));
        Assert.Contains("CardTypeId", missingType.Message, StringComparison.OrdinalIgnoreCase);

        var deleteColumn = Assert.Throws<SqliteException>(() =>
            connection.Execute("DELETE FROM Columns WHERE SystemKey = 'done';"));
        Assert.Contains("cannot be deleted", deleteColumn.Message, StringComparison.OrdinalIgnoreCase);

        var renameColumn = Assert.Throws<SqliteException>(() =>
            connection.Execute("UPDATE Columns SET Name = 'COMPLETED' WHERE SystemKey = 'done';"));
        Assert.Contains("cannot be modified", renameColumn.Message, StringComparison.OrdinalIgnoreCase);

        var deleteGeneric = Assert.Throws<SqliteException>(() =>
            connection.Execute("DELETE FROM CardTypes WHERE SystemKey = 'generic';"));
        Assert.Contains("cannot be deleted", deleteGeneric.Message, StringComparison.OrdinalIgnoreCase);


        var deactivateGeneric = Assert.Throws<SqliteException>(() =>
            connection.Execute("UPDATE CardTypes SET IsActive = 0 WHERE SystemKey = 'generic';"));
        Assert.Contains("cannot be modified", deactivateGeneric.Message, StringComparison.OrdinalIgnoreCase);
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

    private static void CreateVersionFourDatabase(SqliteConnectionFactory factory)
    {
        using var connection = factory.Create();
        foreach (var migration in new EmbeddedDatabaseMigrationCatalog()
                     .ReadMigrations()
                     .Where(item => item.Version <= 4)
                     .OrderBy(item => item.Version))
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

        var sqlTypeId = connection.ExecuteScalar<long>(
            "SELECT Id FROM CardTypes WHERE Name = 'SQL';");
        const string insertSql =
            """
            INSERT INTO Cards
                (Id, ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated,
                 PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc,
                 Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
            VALUES
                (@Id, @ColumnId, @StableId, @Now, 0,
                 NULL, @CardTypeId, NULL, NULL,
                 @Title, NULL, @SortOrder, 'Emilie', 'Emilie', @Now, 1);
            """;
        var now = "2026-07-15T10:00:00.0000000Z";
        connection.Execute(insertSql, new
        {
            Id = 1,
            ColumnId = 0,
            StableId = "11111111111111111111111111111111",
            Now = now,
            CardTypeId = (long?)null,
            Title = "Backlog",
            SortOrder = 0,
        });
        connection.Execute(insertSql, new
        {
            Id = 2,
            ColumnId = 1,
            StableId = "22222222222222222222222222222222",
            Now = now,
            CardTypeId = (long?)null,
            Title = "Lunedì A",
            SortOrder = 0,
        });
        connection.Execute(insertSql, new
        {
            Id = 3,
            ColumnId = 1,
            StableId = "33333333333333333333333333333333",
            Now = now,
            CardTypeId = (long?)null,
            Title = "Lunedì B",
            SortOrder = 1,
        });
        connection.Execute(insertSql, new
        {
            Id = 4,
            ColumnId = 2,
            StableId = "44444444444444444444444444444444",
            Now = now,
            CardTypeId = (long?)sqlTypeId,
            Title = "Martedì",
            SortOrder = 0,
        });
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
