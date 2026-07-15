using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Time;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseSafetyServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-database-safety-tests-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_tempDirectory, "data", "weeklyplanner.db");

    private string BackupDirectory => Path.Combine(_tempDirectory, "backups");

    private string PendingRestorePath => Path.Combine(_tempDirectory, "state", "pending-restore.json");

    private string SessionDirectory => Path.Combine(_tempDirectory, "sessions");

    [Fact]
    public async Task CreateBackup_creates_a_valid_standalone_copy()
    {
        CreateDatabase("Originale", schemaVersion: 5);
        var service = CreateService();

        var backup = await service.CreateBackupAsync(DatabasePath);

        Assert.True(File.Exists(backup.FilePath));
        Assert.Equal(DatabaseBackupKind.Manual, backup.Kind);
        Assert.Equal(DatabaseBackupIntegrityStatus.Valid, backup.IntegrityStatus);
        Assert.Equal(5, backup.SchemaVersion);
        Assert.True(backup.SizeBytes > 0);

        using var connection = OpenReadOnly(backup.FilePath);
        Assert.Equal("Originale", connection.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
        Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task InspectBackup_rejects_corrupt_and_future_schema_files()
    {
        Directory.CreateDirectory(BackupDirectory);
        var corruptPath = Path.Combine(BackupDirectory, "weeklyplanner-manual-corrupt.db");
        await File.WriteAllTextAsync(corruptPath, "not a sqlite database");

        var service = CreateService();
        var corrupt = await service.InspectBackupAsync(corruptPath);

        Assert.False(corrupt.CanRestore);
        Assert.Contains(
            corrupt.IntegrityStatus,
            new[] { DatabaseBackupIntegrityStatus.Corrupt, DatabaseBackupIntegrityStatus.Error });

        var futurePath = Path.Combine(BackupDirectory, "weeklyplanner-manual-future.db");
        CreateDatabaseAt(futurePath, "Futuro", schemaVersion: 99);
        var future = await service.InspectBackupAsync(futurePath);

        Assert.Equal(DatabaseBackupIntegrityStatus.Incompatible, future.IntegrityStatus);
        Assert.Equal(99, future.SchemaVersion);
        Assert.False(future.CanRestore);
    }

    [Fact]
    public async Task Prepare_and_process_restore_preserve_current_database_and_restore_selected_backup()
    {
        CreateDatabase("Versione backup", schemaVersion: 5);
        var registry = new DatabaseInstanceRegistry(SessionDirectory);
        var service = CreateService(registry);
        var selectedBackup = await service.CreateBackupAsync(DatabasePath);

        UpdateValue("Versione corrente");
        await using var currentLease = registry.Register(DatabasePath, "session-current");

        var preparation = await service.PrepareRestoreAsync(
            DatabasePath,
            selectedBackup.FilePath,
            "session-current");

        Assert.True(File.Exists(preparation.PendingRequestPath));
        UpdateValue("Versione corrente finale");

        await currentLease.DisposeAsync();
        await File.WriteAllBytesAsync(DatabasePath + "-wal", []);
        await File.WriteAllBytesAsync(DatabasePath + "-shm", []);
        await File.WriteAllBytesAsync(DatabasePath + "-journal", []);

        var result = await service.ProcessPendingRestoreAsync();

        Assert.Equal(DatabaseRestoreStartupStatus.Succeeded, result.Status);
        Assert.False(File.Exists(preparation.PendingRequestPath));
        Assert.False(File.Exists(DatabasePath + "-wal"));
        Assert.False(File.Exists(DatabasePath + "-shm"));
        Assert.False(File.Exists(DatabasePath + "-journal"));

        using (var restored = OpenReadOnly(DatabasePath))
        {
            Assert.Equal("Versione backup", restored.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
        }

        Assert.False(string.IsNullOrWhiteSpace(result.PreRestoreBackupPath));
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        using var preRestore = OpenReadOnly(result.PreRestoreBackupPath!);
        Assert.Equal("Versione corrente finale", preRestore.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
    }

    [Fact]
    public async Task Concurrent_restore_processor_is_blocked_without_opening_or_consuming_the_request()
    {
        CreateDatabase("Versione backup", schemaVersion: 5);
        var service = CreateService();
        var selectedBackup = await service.CreateBackupAsync(DatabasePath);
        UpdateValue("Versione corrente");
        var preparation = await service.PrepareRestoreAsync(
            DatabasePath,
            selectedBackup.FilePath,
            "session-current");

        Directory.CreateDirectory(Path.GetDirectoryName(PendingRestorePath)!);
        using (var restoreLock = new FileStream(
                   PendingRestorePath + ".lock",
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            var blocked = await service.ProcessPendingRestoreAsync();

            Assert.Equal(DatabaseRestoreStartupStatus.Blocked, blocked.Status);
            Assert.True(File.Exists(preparation.PendingRequestPath));
            using var current = OpenReadOnly(DatabasePath);
            Assert.Equal("Versione corrente", current.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
        }

        var completed = await service.ProcessPendingRestoreAsync();
        Assert.Equal(DatabaseRestoreStartupStatus.Succeeded, completed.Status);
    }

    [Fact]
    public async Task Restore_rolls_back_to_the_previous_database_if_final_validation_fails()
    {
        CreateDatabase("Versione backup", schemaVersion: 5);
        var checker = new FailingTargetIntegrityChecker();
        var service = CreateService(integrityChecker: checker);
        var selectedBackup = await service.CreateBackupAsync(DatabasePath);
        UpdateValue("Versione corrente");

        var preparation = await service.PrepareRestoreAsync(
            DatabasePath,
            selectedBackup.FilePath,
            "session-current");
        checker.FailTargetPath = DatabasePath;

        var result = await service.ProcessPendingRestoreAsync();

        Assert.Equal(DatabaseRestoreStartupStatus.Failed, result.Status);
        Assert.False(File.Exists(preparation.PendingRequestPath));
        Assert.False(string.IsNullOrWhiteSpace(result.PreRestoreBackupPath));
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        using var current = OpenReadOnly(DatabasePath);
        Assert.Equal("Versione corrente", current.ExecuteScalar<string>("SELECT Value FROM Sample WHERE Id = 1;"));
    }

    [Fact]
    public async Task PrepareRestore_refuses_another_active_instance()
    {
        CreateDatabase("Originale", schemaVersion: 5);
        var registry = new DatabaseInstanceRegistry(SessionDirectory);
        var service = CreateService(registry);
        var backup = await service.CreateBackupAsync(DatabasePath);
        await using var currentLease = registry.Register(DatabasePath, "session-current");
        await using var otherLease = registry.Register(DatabasePath, "session-other");

        var exception = await Assert.ThrowsAsync<DatabaseRestoreBlockedException>(
            () => service.PrepareRestoreAsync(
                DatabasePath,
                backup.FilePath,
                "session-current"));

        Assert.Single(exception.ActiveInstances);
        Assert.Equal("session-other", exception.ActiveInstances[0].SessionId);
        Assert.False(File.Exists(PendingRestorePath));
    }

    [Fact]
    public async Task CancelPreparedRestore_removes_only_the_pending_request()
    {
        CreateDatabase("Originale", schemaVersion: 5);
        var service = CreateService();
        var backup = await service.CreateBackupAsync(DatabasePath);
        var preparation = await service.PrepareRestoreAsync(
            DatabasePath,
            backup.FilePath,
            "session-current");

        await service.CancelPreparedRestoreAsync(preparation);

        Assert.False(File.Exists(PendingRestorePath));
        Assert.True(File.Exists(backup.FilePath));
        Assert.Empty(Directory.EnumerateFiles(BackupDirectory, "weeklyplanner-pre-restore-*.db"));
        Assert.Equal(DatabaseRestoreStartupStatus.None, (await service.ProcessPendingRestoreAsync()).Status);
    }

    private SqliteDatabaseSafetyService CreateService(
        IDatabaseInstanceRegistry? registry = null,
        IDatabaseIntegrityChecker? integrityChecker = null) =>
        new(
            new DatabaseSafetyOptions
            {
                BackupDirectory = BackupDirectory,
                PendingRestoreRequestPath = PendingRestorePath,
                MaximumSupportedSchemaVersion = 5,
                InstanceShutdownWaitTimeout = TimeSpan.FromMilliseconds(250),
                InstanceShutdownPollInterval = TimeSpan.FromMilliseconds(10),
            },
            integrityChecker ?? new SqliteDatabaseIntegrityChecker(),
            registry ?? new DatabaseInstanceRegistry(SessionDirectory),
            new FixedClock(new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero)));

    private void CreateDatabase(string value, int schemaVersion) =>
        CreateDatabaseAt(DatabasePath, value, schemaVersion);

    private static void CreateDatabaseAt(string path, string value, int schemaVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        connection.Open();
        connection.Execute(
            """
            CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
            INSERT INTO SchemaVersion (Version) VALUES (@SchemaVersion);
            CREATE TABLE Sample (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);
            INSERT INTO Sample (Id, Value) VALUES (1, @Value);
            """,
            new { SchemaVersion = schemaVersion, Value = value });
    }

    private void UpdateValue(string value)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
        connection.Open();
        connection.Execute("UPDATE Sample SET Value = @Value WHERE Id = 1;", new { Value = value });
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


    private sealed class FailingTargetIntegrityChecker : IDatabaseIntegrityChecker
    {
        private readonly SqliteDatabaseIntegrityChecker _inner = new();

        public string? FailTargetPath { get; set; }

        public void EnsureIntegrity(SqliteConnection connection, string operationDescription)
        {
            _inner.EnsureIntegrity(connection, operationDescription);
            if (string.Equals(operationDescription, "durante il ripristino", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(FailTargetPath) &&
                string.Equals(
                    Path.GetFullPath(connection.DataSource),
                    Path.GetFullPath(FailTargetPath),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new DatabaseIntegrityException(
                    operationDescription,
                    "Errore simulato dopo la sostituzione.");
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
