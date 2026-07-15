using Dapper;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-migrations-tests-{Guid.NewGuid():N}.db");

    private readonly string _tempBackupDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-migration-backups-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EnsureInitialized_creates_expected_schema_and_is_idempotent()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = CreateInitializer(connectionFactory);

        initializer.EnsureInitialized();
        initializer.EnsureInitialized();

        using var connection = connectionFactory.Create();
        var version = connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;");
        var columns = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Columns;");
        var boardStateRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM BoardState WHERE Id = 1;");
        var revision = connection.ExecuteScalar<long>("SELECT Revision FROM BoardState WHERE Id = 1;");
        var cardVersionColumns = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Version';");
        var lockTables = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CardEditLocks';");
        var cardRevisionTriggers = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'TR_Cards_BoardRevision_%';");
        var lockRevisionTriggers = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'TR_CardEditLocks_BoardRevision_%';");
        var priorities = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Priorities;");
        var cardTypes = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM CardTypes;");
        var deadlineRules = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM PriorityTypeDeadlines;");
        var eventTables = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CardEvents';");
        var stableIdColumns = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'StableId';");
        var workflowKeys = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Columns WHERE IsSystem = 1 AND SystemKey IS NOT NULL;");
        var genericTypes = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM CardTypes WHERE SystemKey = 'generic' AND IsSystem = 1 AND IsDefault = 1;");
        var cardTypeRequiredTriggers = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' " +
            "AND name LIKE 'TR_Cards_CardType_Required_%';");
        var catalogRevisionTriggers = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' " +
            "AND (name LIKE 'TR_Priorities_BoardRevision_%' " +
            "OR name LIKE 'TR_CardTypes_BoardRevision_%' " +
            "OR name LIKE 'TR_PriorityTypeDeadlines_BoardRevision_%' " +
            "OR name LIKE 'TR_Columns_BoardRevision_%');");

        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, version);
        Assert.Equal(5, columns);
        Assert.Equal(1, boardStateRows);
        Assert.Equal(0, revision);
        Assert.Equal(1, cardVersionColumns);
        Assert.Equal(1, lockTables);
        Assert.Equal(3, cardRevisionTriggers);
        Assert.Equal(2, lockRevisionTriggers);
        Assert.Equal(4, priorities);
        Assert.Equal(6, cardTypes);
        Assert.Equal(1, deadlineRules);
        Assert.Equal(1, eventTables);
        Assert.Equal(1, stableIdColumns);
        Assert.Equal(5, workflowKeys);
        Assert.Equal(1, genericTypes);
        Assert.Equal(2, cardTypeRequiredTriggers);
        Assert.Equal(12, catalogRevisionTriggers);
    }

    [Fact]
    public void EnsureInitialized_upgrades_an_existing_v1_database()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        CreateVersionOneDatabase(connectionFactory);

        CreateInitializer(connectionFactory).EnsureInitialized();

        using var connection = connectionFactory.Create();
        var version = connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;");
        var revision = connection.ExecuteScalar<long>("SELECT Revision FROM BoardState WHERE Id = 1;");
        var cardVersionColumns = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Version';");

        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, version);
        Assert.Equal(0, revision);
        Assert.Equal(1, cardVersionColumns);
        Assert.Single(
            Directory.GetFiles(_tempBackupDirectory, "weeklyplanner-migration-v1-to-v5-*.db"));
    }

    [Fact]
    public void EnsureInitialized_upgrades_v2_cards_with_initial_version_one()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        CreateVersionTwoDatabase(connectionFactory);

        CreateInitializer(connectionFactory).EnsureInitialized();

        using var connection = connectionFactory.Create();
        var version = connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;");
        var cardVersion = connection.ExecuteScalar<int>("SELECT Version FROM Cards WHERE Id = 1;");
        var lockTables = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CardEditLocks';");

        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, version);
        Assert.Equal(1, cardVersion);
        Assert.Equal(1, lockTables);
        Assert.Single(
            Directory.GetFiles(_tempBackupDirectory, "weeklyplanner-migration-v2-to-v5-*.db"));
    }

    [Fact]
    public void EnsureInitialized_does_not_create_a_backup_when_schema_is_already_current()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = CreateInitializer(connectionFactory);
        initializer.EnsureInitialized();

        if (Directory.Exists(_tempBackupDirectory))
        {
            Directory.Delete(_tempBackupDirectory, recursive: true);
        }

        initializer.EnsureInitialized();

        Assert.False(Directory.Exists(_tempBackupDirectory));
        using var connection = connectionFactory.Create();
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
    }

    [Fact]
    public void EnsureInitialized_does_not_recreate_a_database_missing_during_recovery()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = CreateInitializer(connectionFactory);
        initializer.EnsureInitialized();
        File.Delete(_tempDbPath);

        var exception = Assert.Throws<FileNotFoundException>(
            () => initializer.EnsureInitialized(allowCreate: false));

        Assert.Equal(_tempDbPath, exception.FileName);
        Assert.False(File.Exists(_tempDbPath));
    }

    [Fact]
    public void EnsureInitialized_rejects_a_newer_schema()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = CreateInitializer(connectionFactory);
        initializer.EnsureInitialized();

        using (var connection = connectionFactory.Create())
        {
            connection.Execute("UPDATE SchemaVersion SET Version = 99;");
        }

        var exception = Assert.Throws<SchemaVersionMismatchException>(initializer.EnsureInitialized);

        Assert.Equal(99, exception.FoundVersion);
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, exception.ExpectedVersion);
    }

    private DatabaseInitializer CreateInitializer(SqliteConnectionFactory connectionFactory)
    {
        var integrityChecker = new SqliteDatabaseIntegrityChecker();
        var backupService = new SqliteDatabaseMigrationBackupService(
            new DatabaseMigrationBackupOptions
            {
                BackupDirectory = _tempBackupDirectory,
                RetentionCount = 5,
            },
            integrityChecker);

        return new DatabaseInitializer(
            connectionFactory,
            new EmbeddedDatabaseMigrationCatalog(),
            integrityChecker,
            backupService);
    }

    private static void CreateVersionOneDatabase(SqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.Create();
        connection.Execute(
            """
            CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
            INSERT INTO SchemaVersion (Version) VALUES (1);

            CREATE TABLE Columns (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY,
                ColumnId INTEGER NOT NULL REFERENCES Columns(Id),
                Title TEXT NOT NULL,
                Notes TEXT,
                SortOrder INTEGER NOT NULL,
                CreatedBy TEXT,
                UpdatedBy TEXT,
                UpdatedAtUtc TEXT NOT NULL
            );

            INSERT INTO Columns (Id, Name, SortOrder) VALUES (0, 'Backlog', 0);
            """);
    }

    private static void CreateVersionTwoDatabase(SqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.Create();
        connection.Execute(
            """
            CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
            INSERT INTO SchemaVersion (Version) VALUES (2);

            CREATE TABLE Columns (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE Cards (
                Id INTEGER PRIMARY KEY,
                ColumnId INTEGER NOT NULL REFERENCES Columns(Id),
                Title TEXT NOT NULL,
                Notes TEXT,
                SortOrder INTEGER NOT NULL,
                CreatedBy TEXT,
                UpdatedBy TEXT,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE BoardState (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                Revision INTEGER NOT NULL CHECK (Revision >= 0)
            );

            INSERT INTO Columns (Id, Name, SortOrder) VALUES (0, 'Backlog', 0);
            INSERT INTO BoardState (Id, Revision) VALUES (1, 0);
            INSERT INTO Cards
                (Id, ColumnId, Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc)
            VALUES
                (1, 0, 'Card esistente', NULL, 0, 'Test', 'Test', '2026-01-01T00:00:00.0000000Z');
            """);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }

        if (Directory.Exists(_tempBackupDirectory))
        {
            Directory.Delete(_tempBackupDirectory, recursive: true);
        }
    }
}
