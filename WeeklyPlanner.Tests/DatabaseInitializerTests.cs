using Dapper;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-migrations-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public void EnsureInitialized_creates_expected_schema_and_is_idempotent()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = new DatabaseInitializer(connectionFactory);

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

        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, version);
        Assert.Equal(8, columns);
        Assert.Equal(1, boardStateRows);
        Assert.Equal(0, revision);
        Assert.Equal(1, cardVersionColumns);
        Assert.Equal(1, lockTables);
        Assert.Equal(3, cardRevisionTriggers);
        Assert.Equal(2, lockRevisionTriggers);
    }

    [Fact]
    public void EnsureInitialized_upgrades_an_existing_v1_database()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        CreateVersionOneDatabase(connectionFactory);

        new DatabaseInitializer(connectionFactory).EnsureInitialized();

        using var connection = connectionFactory.Create();
        var version = connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;");
        var revision = connection.ExecuteScalar<long>("SELECT Revision FROM BoardState WHERE Id = 1;");
        var cardVersionColumns = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Version';");

        Assert.Equal(3, version);
        Assert.Equal(0, revision);
        Assert.Equal(1, cardVersionColumns);
    }

    [Fact]
    public void EnsureInitialized_upgrades_v2_cards_with_initial_version_one()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        CreateVersionTwoDatabase(connectionFactory);

        new DatabaseInitializer(connectionFactory).EnsureInitialized();

        using var connection = connectionFactory.Create();
        var version = connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;");
        var cardVersion = connection.ExecuteScalar<int>("SELECT Version FROM Cards WHERE Id = 1;");
        var lockTables = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CardEditLocks';");

        Assert.Equal(3, version);
        Assert.Equal(1, cardVersion);
        Assert.Equal(1, lockTables);
    }

    [Fact]
    public void EnsureInitialized_does_not_recreate_a_database_missing_during_recovery()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var initializer = new DatabaseInitializer(connectionFactory);
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
        var initializer = new DatabaseInitializer(connectionFactory);
        initializer.EnsureInitialized();

        using (var connection = connectionFactory.Create())
        {
            connection.Execute("UPDATE SchemaVersion SET Version = 99;");
        }

        var exception = Assert.Throws<SchemaVersionMismatchException>(initializer.EnsureInitialized);

        Assert.Equal(99, exception.FoundVersion);
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, exception.ExpectedVersion);
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
    }
}
