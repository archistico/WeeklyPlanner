using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseMigrationSafetyTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-migration-safety-tests-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_tempDirectory, "weeklyplanner.db");

    private string BackupDirectory => Path.Combine(_tempDirectory, "backups");

    [Fact]
    public void Successful_upgrade_creates_a_backup_of_the_previous_schema()
    {
        var factory = CreateVersionOneDatabase();
        var initializer = CreateInitializer(factory);

        initializer.EnsureInitialized();

        var backupPath = Assert.Single(
            Directory.GetFiles(BackupDirectory, "weeklyplanner-migration-v1-to-v5-*.db"));
        using var backup = OpenReadOnly(backupPath);
        Assert.Equal(1, backup.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal(
            0,
            backup.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'BoardState';"));

        using var upgraded = factory.Create();
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, upgraded.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
    }


    [Fact]
    public void Existing_unversioned_database_is_backed_up_before_the_version_table_is_created()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        using (var connection = factory.Create())
        {
            connection.Execute(
                """
                CREATE TABLE LegacyData (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);
                INSERT INTO LegacyData (Id, Value) VALUES (1, 'Preservato');
                """);
        }

        CreateInitializer(factory).EnsureInitialized();

        var backupPath = Assert.Single(
            Directory.GetFiles(BackupDirectory, "weeklyplanner-migration-v0-to-v5-*.db"));
        using (var backup = OpenReadOnly(backupPath))
        {
            Assert.Equal(
                0,
                backup.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';"));
            Assert.Equal(
                "Preservato",
                backup.ExecuteScalar<string>("SELECT Value FROM LegacyData WHERE Id = 1;"));
        }

        using var upgraded = factory.Create();
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, upgraded.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal(
            "Preservato",
            upgraded.ExecuteScalar<string>("SELECT Value FROM LegacyData WHERE Id = 1;"));
    }

    [Fact]
    public void Failed_migration_restores_the_original_database_and_preserves_the_backup()
    {
        var factory = CreateVersionOneDatabase();
        var migrations = new EmbeddedDatabaseMigrationCatalog()
            .ReadMigrations()
            .Select(migration => migration.Version == 3
                ? migration with
                {
                    Sql =
                        """
                        CREATE TABLE ShouldBeRolledBack (Id INTEGER PRIMARY KEY);
                        THIS IS NOT VALID SQL;
                        """,
                }
                : migration)
            .ToList();
        var initializer = CreateInitializer(factory, new StaticMigrationCatalog(migrations));

        var exception = Assert.Throws<DatabaseMigrationFailedException>(initializer.EnsureInitialized);

        Assert.True(exception.DatabaseRestored);
        Assert.True(File.Exists(exception.BackupPath));
        Assert.Equal(1, exception.SourceSchemaVersion);
        Assert.Equal(DatabaseInitializer.ExpectedSchemaVersion, exception.TargetSchemaVersion);

        using var restored = factory.Create();
        Assert.Equal(1, restored.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal(
            0,
            restored.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'BoardState';"));
        Assert.Equal(
            0,
            restored.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ShouldBeRolledBack';"));
        Assert.Equal("Card originale", restored.ExecuteScalar<string>("SELECT Title FROM Cards WHERE Id = 1;"));
        Assert.Equal("ok", restored.ExecuteScalar<string>("PRAGMA integrity_check;"));
    }



    [Fact]
    public void Failed_post_migration_integrity_check_restores_the_original_database()
    {
        var factory = CreateVersionOneDatabase();
        var initializerIntegrityChecker = new FailOnSecondIntegrityChecker();
        var backupService = new SqliteDatabaseMigrationBackupService(
            new DatabaseMigrationBackupOptions
            {
                BackupDirectory = BackupDirectory,
                RetentionCount = 5,
            },
            new SqliteDatabaseIntegrityChecker());
        var initializer = new DatabaseInitializer(
            factory,
            new EmbeddedDatabaseMigrationCatalog(),
            initializerIntegrityChecker,
            backupService);

        var exception = Assert.Throws<DatabaseMigrationFailedException>(initializer.EnsureInitialized);

        Assert.IsType<DatabaseIntegrityException>(exception.InnerException);
        Assert.Equal(3, initializerIntegrityChecker.CallCount);
        using var restored = factory.Create();
        Assert.Equal(1, restored.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal("Card originale", restored.ExecuteScalar<string>("SELECT Title FROM Cards WHERE Id = 1;"));
    }

    [Fact]
    public void Failed_creation_removes_the_new_partial_database()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        var migrations = new EmbeddedDatabaseMigrationCatalog()
            .ReadMigrations()
            .Select(migration => migration.Version == 1
                ? migration with { Sql = "CREATE TABLE Partial (Id INTEGER); INVALID SQL;" }
                : migration)
            .ToList();
        var initializer = CreateInitializer(factory, new StaticMigrationCatalog(migrations));

        Assert.Throws<SqliteException>(initializer.EnsureInitialized);

        Assert.False(File.Exists(DatabasePath));
        Assert.False(File.Exists(DatabasePath + "-journal"));
    }


    [Fact]
    public void Missing_migration_is_detected_before_creating_a_backup()
    {
        var factory = CreateVersionOneDatabase();
        var onlyVersionThree = new EmbeddedDatabaseMigrationCatalog()
            .ReadMigrations()
            .Where(migration => migration.Version == 3)
            .ToList();
        var backupService = new RecordingBackupService();
        var initializer = new DatabaseInitializer(
            factory,
            new StaticMigrationCatalog(onlyVersionThree),
            new SqliteDatabaseIntegrityChecker(),
            backupService);

        var exception = Assert.Throws<MissingMigrationException>(initializer.EnsureInitialized);

        Assert.Equal(2, exception.MissingVersion);
        Assert.False(backupService.CreateCalled);
        using var connection = factory.Create();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
    }

    [Fact]
    public void Failed_pre_migration_integrity_check_does_not_create_a_backup_or_change_the_schema()
    {
        var factory = CreateVersionOneDatabase();
        var backupService = new RecordingBackupService();
        var initializer = new DatabaseInitializer(
            factory,
            new EmbeddedDatabaseMigrationCatalog(),
            new ThrowingIntegrityChecker(),
            backupService);

        Assert.Throws<DatabaseIntegrityException>(initializer.EnsureInitialized);

        Assert.False(backupService.CreateCalled);
        using var connection = factory.Create();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
    }

    [Fact]
    public void Restore_failure_is_reported_without_hiding_the_migration_error()
    {
        var factory = CreateVersionOneDatabase();
        var migrations = new EmbeddedDatabaseMigrationCatalog()
            .ReadMigrations()
            .Select(migration => migration.Version == 3
                ? migration with { Sql = "INVALID MIGRATION;" }
                : migration)
            .ToList();
        var backupService = new FailingRestoreBackupService(BackupDirectory);
        var initializer = new DatabaseInitializer(
            factory,
            new StaticMigrationCatalog(migrations),
            new SqliteDatabaseIntegrityChecker(),
            backupService);

        var exception = Assert.Throws<DatabaseMigrationRecoveryException>(initializer.EnsureInitialized);

        Assert.IsType<SqliteException>(exception.MigrationException);
        Assert.IsType<IOException>(exception.RestoreException);
        Assert.True(File.Exists(exception.BackupPath));
    }

    private SqliteConnectionFactory CreateVersionOneDatabase()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        using var connection = factory.Create();
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
            INSERT INTO Cards
                (Id, ColumnId, Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc)
            VALUES
                (1, 0, 'Card originale', NULL, 0, 'Test', 'Test', '2026-01-01T00:00:00.0000000Z');
            """);
        return factory;
    }

    private DatabaseInitializer CreateInitializer(
        SqliteConnectionFactory factory,
        IDatabaseMigrationCatalog? catalog = null)
    {
        var integrityChecker = new SqliteDatabaseIntegrityChecker();
        var backupService = new SqliteDatabaseMigrationBackupService(
            new DatabaseMigrationBackupOptions
            {
                BackupDirectory = BackupDirectory,
                RetentionCount = 5,
            },
            integrityChecker);
        return new DatabaseInitializer(
            factory,
            catalog ?? new EmbeddedDatabaseMigrationCatalog(),
            integrityChecker,
            backupService);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class StaticMigrationCatalog(IReadOnlyList<DatabaseMigration> migrations)
        : IDatabaseMigrationCatalog
    {
        public IReadOnlyList<DatabaseMigration> ReadMigrations() => migrations;
    }


    private sealed class FailOnSecondIntegrityChecker : IDatabaseIntegrityChecker
    {
        private readonly SqliteDatabaseIntegrityChecker _inner = new();

        public int CallCount { get; private set; }

        public void EnsureIntegrity(SqliteConnection connection, string operationDescription)
        {
            CallCount++;
            if (CallCount == 2)
            {
                throw new DatabaseIntegrityException(
                    operationDescription,
                    "Controllo finale simulato non riuscito.");
            }

            _inner.EnsureIntegrity(connection, operationDescription);
        }
    }

    private sealed class ThrowingIntegrityChecker : IDatabaseIntegrityChecker
    {
        public void EnsureIntegrity(SqliteConnection connection, string operationDescription) =>
            throw new DatabaseIntegrityException(operationDescription, "Errore simulato.");
    }

    private sealed class RecordingBackupService : IDatabaseMigrationBackupService
    {
        public bool CreateCalled { get; private set; }

        public DatabaseMigrationBackup CreateBackup(
            SqliteConnection sourceConnection,
            int sourceSchemaVersion,
            int targetSchemaVersion)
        {
            CreateCalled = true;
            throw new InvalidOperationException("Non dovrebbe essere chiamato.");
        }

        public void RestoreBackup(DatabaseMigrationBackup backup, string databasePath) =>
            throw new NotSupportedException();

        public void ApplyRetention(DatabaseMigrationBackup backupToKeep) =>
            throw new NotSupportedException();
    }

    private sealed class FailingRestoreBackupService : IDatabaseMigrationBackupService
    {
        private readonly SqliteDatabaseMigrationBackupService _inner;

        public FailingRestoreBackupService(string backupDirectory)
        {
            _inner = new SqliteDatabaseMigrationBackupService(
                new DatabaseMigrationBackupOptions
                {
                    BackupDirectory = backupDirectory,
                    RetentionCount = 5,
                });
        }

        public DatabaseMigrationBackup CreateBackup(
            SqliteConnection sourceConnection,
            int sourceSchemaVersion,
            int targetSchemaVersion) =>
            _inner.CreateBackup(sourceConnection, sourceSchemaVersion, targetSchemaVersion);

        public void RestoreBackup(DatabaseMigrationBackup backup, string databasePath) =>
            throw new IOException("Ripristino simulato non disponibile.");

        public void ApplyRetention(DatabaseMigrationBackup backupToKeep) =>
            _inner.ApplyRetention(backupToKeep);
    }
}
