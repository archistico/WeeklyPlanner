using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Time;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseMigrationBackupServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-backup-service-tests-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_tempDirectory, "weeklyplanner.db");

    private string BackupDirectory => Path.Combine(_tempDirectory, "backups");

    [Fact]
    public void CreateBackup_creates_an_integral_copy_with_the_original_data()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        using var source = factory.Create();
        source.Execute(
            """
            CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
            INSERT INTO SchemaVersion (Version) VALUES (1);
            CREATE TABLE Sample (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);
            INSERT INTO Sample (Id, Value) VALUES (1, 'Originale');
            """);

        var service = CreateService(retentionCount: 5);
        var backup = service.CreateBackup(source, sourceSchemaVersion: 1, targetSchemaVersion: 3);

        Assert.True(File.Exists(backup.FilePath));
        Assert.Equal(1, backup.SourceSchemaVersion);
        Assert.Equal(3, backup.TargetSchemaVersion);

        using var backupConnection = OpenReadOnly(backup.FilePath);
        Assert.Equal(1, backupConnection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal("Originale", backupConnection.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
        Assert.Equal("ok", backupConnection.ExecuteScalar<string>("PRAGMA integrity_check;"));
    }

    [Fact]
    public void ApplyRetention_keeps_the_current_backup_and_only_the_configured_number_of_files()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        using var source = factory.Create();
        source.Execute(
            """
            CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
            INSERT INTO SchemaVersion (Version) VALUES (1);
            """);

        var service = CreateService(retentionCount: 2);
        var backups = new List<DatabaseMigrationBackup>();
        for (var index = 0; index < 4; index++)
        {
            var backup = service.CreateBackup(source, 1, 3);
            File.SetLastWriteTimeUtc(backup.FilePath, new DateTime(2026, 7, 15, 10, index, 0, DateTimeKind.Utc));
            backups.Add(backup);
        }

        service.ApplyRetention(backups[3]);

        var remaining = Directory.GetFiles(BackupDirectory, "weeklyplanner-migration-v*-to-v*-*.db");
        Assert.Equal(2, remaining.Length);
        Assert.Contains(
            remaining,
            path => string.Equals(path, backups[3].FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            remaining,
            path => string.Equals(path, backups[2].FilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RestoreBackup_replaces_a_partially_migrated_database_with_the_original_copy()
    {
        Directory.CreateDirectory(_tempDirectory);
        var factory = new SqliteConnectionFactory(DatabasePath);
        var service = CreateService(retentionCount: 5);
        DatabaseMigrationBackup backup;

        using (var source = factory.Create())
        {
            source.Execute(
                """
                CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
                INSERT INTO SchemaVersion (Version) VALUES (1);
                CREATE TABLE Sample (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);
                INSERT INTO Sample (Id, Value) VALUES (1, 'Originale');
                """);
            backup = service.CreateBackup(source, 1, 3);
            source.Execute("UPDATE SchemaVersion SET Version = 2; CREATE TABLE PartialMigration (Id INTEGER);");
        }

        service.RestoreBackup(backup, DatabasePath);

        using var restored = factory.Create();
        Assert.Equal(1, restored.ExecuteScalar<int>("SELECT Version FROM SchemaVersion;"));
        Assert.Equal("Originale", restored.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
        Assert.Equal(
            0,
            restored.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'PartialMigration';"));
        Assert.Equal("ok", restored.ExecuteScalar<string>("PRAGMA integrity_check;"));
    }


    [Fact]
    public void Default_backup_directory_is_an_absolute_local_path()
    {
        var path = DatabaseMigrationBackupOptions.GetDefaultBackupDirectory();

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.EndsWith(
            Path.Combine("WeeklyPlanner", "Backups", "Migrations"),
            path);
    }

    [Fact]
    public void Options_reject_a_relative_backup_directory()
    {
        var options = new DatabaseMigrationBackupOptions
        {
            BackupDirectory = "relative-backups",
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    private SqliteDatabaseMigrationBackupService CreateService(int retentionCount) =>
        new(
            new DatabaseMigrationBackupOptions
            {
                BackupDirectory = BackupDirectory,
                RetentionCount = retentionCount,
            },
            new SqliteDatabaseIntegrityChecker(),
            new FixedClock(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)));

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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
