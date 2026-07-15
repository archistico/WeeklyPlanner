using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Crea backup SQLite coerenti prima degli upgrade e ripristina il file originale in caso di errore.
/// </summary>
public sealed class SqliteDatabaseMigrationBackupService : IDatabaseMigrationBackupService
{
    private const string BackupFilePrefix = "weeklyplanner-migration";

    private readonly DatabaseMigrationBackupOptions _options;
    private readonly IDatabaseIntegrityChecker _integrityChecker;
    private readonly IClock _clock;

    public SqliteDatabaseMigrationBackupService(
        DatabaseMigrationBackupOptions? options = null,
        IDatabaseIntegrityChecker? integrityChecker = null,
        IClock? clock = null)
    {
        _options = options ?? new DatabaseMigrationBackupOptions();
        _options.Validate();
        _integrityChecker = integrityChecker ?? new SqliteDatabaseIntegrityChecker();
        _clock = clock ?? SystemClock.Instance;
    }

    public DatabaseMigrationBackup CreateBackup(
        SqliteConnection sourceConnection,
        int sourceSchemaVersion,
        int targetSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);

        Directory.CreateDirectory(_options.BackupDirectory);

        var createdAtUtc = _clock.Now.ToUniversalTime();
        var timestamp = createdAtUtc.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var fileName =
            $"{BackupFilePrefix}-v{sourceSchemaVersion}-to-v{targetSchemaVersion}-{timestamp}-{Guid.NewGuid():N}.db";
        var backupPath = Path.Combine(_options.BackupDirectory, fileName);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        }.ToString();

        try
        {
            using var destinationConnection = new SqliteConnection(connectionString);
            destinationConnection.Open();
            sourceConnection.BackupDatabase(destinationConnection);
            _integrityChecker.EnsureIntegrity(
                destinationConnection,
                "sul backup preventivo della migrazione");
        }
        catch
        {
            TryDeleteFile(backupPath);
            throw;
        }

        return new DatabaseMigrationBackup(
            backupPath,
            sourceSchemaVersion,
            targetSchemaVersion,
            createdAtUtc);
    }

    public void RestoreBackup(DatabaseMigrationBackup backup, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(backup.FilePath))
        {
            throw new FileNotFoundException(
                $"Il backup di migrazione '{backup.FilePath}' non è più disponibile.",
                backup.FilePath);
        }

        var databaseDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException(
                $"Impossibile determinare la cartella del database '{databasePath}'.");
        Directory.CreateDirectory(databaseDirectory);

        var restoreTempPath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(databasePath)}.restore-{Guid.NewGuid():N}.tmp");
        var failedDatabasePath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(databasePath)}.failed-migration-{_clock.Now.ToUniversalTime():yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(backup.FilePath, restoreTempPath, overwrite: false);
            EnsureFileIntegrity(restoreTempPath, "sulla copia temporanea di ripristino");
            DeleteSqliteSidecarFiles(databasePath);

            var previousDatabaseSaved = false;
            if (File.Exists(databasePath))
            {
                ReplaceFile(restoreTempPath, databasePath, failedDatabasePath);
                previousDatabaseSaved = true;
            }
            else
            {
                File.Move(restoreTempPath, databasePath);
            }

            try
            {
                EnsureFileIntegrity(databasePath, "dopo il ripristino automatico");
                if (previousDatabaseSaved)
                {
                    TryDeleteFile(failedDatabasePath);
                }
            }
            catch
            {
                if (previousDatabaseSaved && File.Exists(failedDatabasePath))
                {
                    DeleteSqliteSidecarFiles(databasePath);
                    RestorePreviousDatabase(failedDatabasePath, databasePath);
                }

                throw;
            }
        }
        finally
        {
            TryDeleteFile(restoreTempPath);
        }
    }

    public void ApplyRetention(DatabaseMigrationBackup backupToKeep)
    {
        ArgumentNullException.ThrowIfNull(backupToKeep);

        try
        {
            if (!Directory.Exists(_options.BackupDirectory))
            {
                return;
            }

            var backups = Directory
                .EnumerateFiles(_options.BackupDirectory, $"{BackupFilePrefix}-v*-to-v*-*.db")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file =>
                    string.Equals(file.FullName, backupToKeep.FilePath, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();

            var pathsToKeep = backups
                .Take(_options.RetentionCount)
                .Select(file => file.FullName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in backups)
            {
                if (!pathsToKeep.Contains(file.FullName))
                {
                    TryDeleteFile(file.FullName);
                }
            }
        }
        catch (IOException)
        {
            // La retention è best-effort: un backup valido non deve rendere inutilizzabile l'app.
        }
        catch (UnauthorizedAccessException)
        {
            // La pulizia verrà ritentata alla prossima migrazione.
        }
    }

    private void EnsureFileIntegrity(string path, string operationDescription)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        _integrityChecker.EnsureIntegrity(connection, operationDescription);
    }


    private static void RestorePreviousDatabase(string previousDatabasePath, string databasePath)
    {
        var invalidRestoredPath = databasePath + $".invalid-restore-{Guid.NewGuid():N}";
        try
        {
            File.Replace(
                previousDatabasePath,
                databasePath,
                invalidRestoredPath,
                ignoreMetadataErrors: true);
            TryDeleteFile(invalidRestoredPath);
        }
        catch (PlatformNotSupportedException)
        {
            TryDeleteFile(databasePath);
            File.Move(previousDatabasePath, databasePath);
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath, string failedDatabasePath)
    {
        try
        {
            File.Replace(sourcePath, destinationPath, failedDatabasePath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceFileWithFallback(sourcePath, destinationPath, failedDatabasePath);
        }
    }

    private static void ReplaceFileWithFallback(
        string sourcePath,
        string destinationPath,
        string failedDatabasePath)
    {
        File.Move(destinationPath, failedDatabasePath);
        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch
        {
            if (!File.Exists(destinationPath) && File.Exists(failedDatabasePath))
            {
                File.Move(failedDatabasePath, destinationPath);
            }

            throw;
        }
    }

    private static void DeleteSqliteSidecarFiles(string databasePath)
    {
        TryDeleteFile(databasePath + "-journal");
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // La retention e la pulizia dei temporanei non devono nascondere l'esito principale.
        }
        catch (UnauthorizedAccessException)
        {
            // Come sopra: il file verrà riprovato in un'esecuzione successiva.
        }
    }
}
