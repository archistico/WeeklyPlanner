using Dapper;
using Microsoft.Data.Sqlite;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Inizializza il database e applica in ordine tutte le migrazioni embedded mancanti.
/// Gli upgrade di database esistenti sono protetti da integrity check e backup preventivo.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    public const int ExpectedSchemaVersion = 5;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseMigrationCatalog _migrationCatalog;
    private readonly IDatabaseIntegrityChecker _integrityChecker;
    private readonly IDatabaseMigrationBackupService _backupService;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
        : this(
            connectionFactory,
            new EmbeddedDatabaseMigrationCatalog(),
            new SqliteDatabaseIntegrityChecker(),
            new SqliteDatabaseMigrationBackupService())
    {
    }

    public DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        IDatabaseMigrationCatalog migrationCatalog,
        IDatabaseIntegrityChecker integrityChecker,
        IDatabaseMigrationBackupService backupService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _migrationCatalog = migrationCatalog ?? throw new ArgumentNullException(nameof(migrationCatalog));
        _integrityChecker = integrityChecker ?? throw new ArgumentNullException(nameof(integrityChecker));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
    }

    public void EnsureInitialized() => EnsureInitialized(allowCreate: true);

    public void EnsureInitialized(bool allowCreate)
    {
        var databaseAlreadyExisted = File.Exists(_connectionFactory.DatabasePath);
        if (!allowCreate && !databaseAlreadyExisted)
        {
            throw new FileNotFoundException(
                $"Il database locale non è più disponibile nel percorso '{_connectionFactory.DatabasePath}'.",
                _connectionFactory.DatabasePath);
        }

        SqliteConnection? connection = null;
        try
        {
            connection = _connectionFactory.Create();

            ConfigurePersistentJournalMode(connection);

            var currentVersion = GetCurrentVersion(connection);
            if (currentVersion > ExpectedSchemaVersion)
            {
                throw new SchemaVersionMismatchException(currentVersion, ExpectedSchemaVersion);
            }

            if (currentVersion == ExpectedSchemaVersion)
            {
                return;
            }

            var migrations = SelectRequiredMigrations(currentVersion);
            DatabaseMigrationBackup? backup = null;

            if (databaseAlreadyExisted)
            {
                _integrityChecker.EnsureIntegrity(
                    connection,
                    $"prima dell'aggiornamento dallo schema v{currentVersion} alla v{ExpectedSchemaVersion}");
                backup = _backupService.CreateBackup(
                    connection,
                    currentVersion,
                    ExpectedSchemaVersion);
            }

            try
            {
                ApplyMigrations(connection, migrations);
                _integrityChecker.EnsureIntegrity(
                    connection,
                    $"dopo l'aggiornamento allo schema v{ExpectedSchemaVersion}");
            }
            catch (Exception migrationException) when (backup is not null)
            {
                connection.Dispose();
                connection = null;
                RestoreAfterMigrationFailure(backup, currentVersion, migrationException);
            }
            catch when (!databaseAlreadyExisted)
            {
                connection.Dispose();
                connection = null;
                DeleteNewlyCreatedDatabase();
                throw;
            }

            if (backup is not null)
            {
                _backupService.ApplyRetention(backup);
            }
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private IReadOnlyList<DatabaseMigration> SelectRequiredMigrations(int currentVersion)
    {
        var migrations = _migrationCatalog
            .ReadMigrations()
            .Where(migration => migration.Version > currentVersion &&
                                migration.Version <= ExpectedSchemaVersion)
            .OrderBy(migration => migration.Version)
            .ToList();

        for (var version = currentVersion + 1; version <= ExpectedSchemaVersion; version++)
        {
            if (migrations.All(migration => migration.Version != version))
            {
                throw new MissingMigrationException(version);
            }
        }

        return migrations;
    }

    private static void ApplyMigrations(
        SqliteConnection connection,
        IReadOnlyList<DatabaseMigration> migrations)
    {
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
    }

    private void DeleteNewlyCreatedDatabase()
    {
        TryDeleteFile(_connectionFactory.DatabasePath);
        TryDeleteFile(_connectionFactory.DatabasePath + "-journal");
        TryDeleteFile(_connectionFactory.DatabasePath + "-wal");
        TryDeleteFile(_connectionFactory.DatabasePath + "-shm");
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
            // L'eccezione originale della migrazione resta il segnale principale.
        }
        catch (UnauthorizedAccessException)
        {
            // Il file parziale non viene mai considerato uno schema valido.
        }
    }

    private void RestoreAfterMigrationFailure(
        DatabaseMigrationBackup backup,
        int originalSchemaVersion,
        Exception migrationException)
    {
        try
        {
            _backupService.RestoreBackup(backup, _connectionFactory.DatabasePath);

            using var restoredConnection = _connectionFactory.Create();
            _integrityChecker.EnsureIntegrity(
                restoredConnection,
                "dopo il rollback della migrazione fallita");
            var restoredVersion = GetCurrentVersion(restoredConnection);
            if (restoredVersion != originalSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Il rollback ha ripristinato lo schema v{restoredVersion} invece della v{originalSchemaVersion}.");
            }
        }
        catch (Exception restoreException)
        {
            throw new DatabaseMigrationRecoveryException(
                originalSchemaVersion,
                ExpectedSchemaVersion,
                backup.FilePath,
                migrationException,
                restoreException);
        }

        throw new DatabaseMigrationFailedException(
            originalSchemaVersion,
            ExpectedSchemaVersion,
            backup.FilePath,
            migrationException);
    }

    private static void ConfigurePersistentJournalMode(System.Data.IDbConnection connection)
    {
        var journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode = DELETE;");
        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite non ha applicato journal_mode=DELETE (modalità restituita: '{journalMode}').");
        }
    }

    private static int GetCurrentVersion(System.Data.IDbConnection connection)
    {
        var versionTableExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';") > 0;

        return versionTableExists
            ? connection.ExecuteScalar<int>("SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;")
            : 0;
    }
}

public sealed class SchemaVersionMismatchException : Exception
{
    public int FoundVersion { get; }
    public int ExpectedVersion { get; }

    public SchemaVersionMismatchException(int foundVersion, int expectedVersion)
        : base($"Lo schema del database (v{foundVersion}) è più recente di quello atteso da " +
               $"questa versione dell'app (v{expectedVersion}). Aggiorna l'applicazione prima di continuare.")
    {
        FoundVersion = foundVersion;
        ExpectedVersion = expectedVersion;
    }
}

public sealed class MissingMigrationException : Exception
{
    public int MissingVersion { get; }

    public MissingMigrationException(int missingVersion)
        : base($"Manca lo script embedded per la migrazione dello schema v{missingVersion}.")
    {
        MissingVersion = missingVersion;
    }
}

public sealed class DatabaseMigrationFailedException : Exception
{
    public int SourceSchemaVersion { get; }

    public int TargetSchemaVersion { get; }

    public string BackupPath { get; }

    public bool DatabaseRestored => true;

    public DatabaseMigrationFailedException(
        int sourceSchemaVersion,
        int targetSchemaVersion,
        string backupPath,
        Exception migrationException)
        : base(
            $"La migrazione dello schema dalla v{sourceSchemaVersion} alla v{targetSchemaVersion} non è riuscita. " +
            $"Il database originale è stato ripristinato dal backup '{backupPath}'.",
            migrationException)
    {
        SourceSchemaVersion = sourceSchemaVersion;
        TargetSchemaVersion = targetSchemaVersion;
        BackupPath = backupPath;
    }
}

public sealed class DatabaseMigrationRecoveryException : Exception
{
    public int SourceSchemaVersion { get; }

    public int TargetSchemaVersion { get; }

    public string BackupPath { get; }

    public Exception MigrationException { get; }

    public Exception RestoreException { get; }

    public DatabaseMigrationRecoveryException(
        int sourceSchemaVersion,
        int targetSchemaVersion,
        string backupPath,
        Exception migrationException,
        Exception restoreException)
        : base(
            $"La migrazione dello schema dalla v{sourceSchemaVersion} alla v{targetSchemaVersion} è fallita " +
            $"e non è stato possibile ripristinare automaticamente il backup '{backupPath}'.",
            restoreException)
    {
        SourceSchemaVersion = sourceSchemaVersion;
        TargetSchemaVersion = targetSchemaVersion;
        BackupPath = backupPath;
        MigrationException = migrationException;
        RestoreException = restoreException;
    }
}
