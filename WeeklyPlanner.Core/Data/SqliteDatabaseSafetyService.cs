using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Gestisce backup manuali SQLite e ripristini differiti applicati al riavvio,
/// prima che la board apra connessioni al database.
/// </summary>
public sealed class SqliteDatabaseSafetyService : IDatabaseSafetyService
{
    private const string ManualBackupPrefix = "weeklyplanner-manual";
    private const string PreRestoreBackupPrefix = "weeklyplanner-pre-restore";

    private readonly DatabaseSafetyOptions _options;
    private readonly IDatabaseIntegrityChecker _integrityChecker;
    private readonly IDatabaseInstanceRegistry _instanceRegistry;
    private readonly IClock _clock;

    public SqliteDatabaseSafetyService(
        DatabaseSafetyOptions? options = null,
        IDatabaseIntegrityChecker? integrityChecker = null,
        IDatabaseInstanceRegistry? instanceRegistry = null,
        IClock? clock = null)
    {
        _options = options ?? new DatabaseSafetyOptions();
        _options.Validate();
        _integrityChecker = integrityChecker ?? new SqliteDatabaseIntegrityChecker();
        _instanceRegistry = instanceRegistry ?? new DatabaseInstanceRegistry();
        _clock = clock ?? SystemClock.Instance;
    }

    public string BackupDirectory => _options.BackupDirectory;

    public Task<DatabaseBackupInfo> CreateBackupAsync(
        string databasePath,
        DatabaseBackupKind kind = DatabaseBackupKind.Manual,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => CreateBackup(databasePath, kind, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<DatabaseBackupInfo>> ListBackupsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DatabaseBackupInfo>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(BackupDirectory))
                {
                    return [];
                }

                return Directory
                    .EnumerateFiles(BackupDirectory, "weeklyplanner-*.db", SearchOption.TopDirectoryOnly)
                    .Select(path => InspectBackup(path, cancellationToken))
                    .OrderByDescending(backup => backup.CreatedAtUtc)
                    .ThenBy(backup => backup.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            },
            cancellationToken);

    public Task<DatabaseBackupInfo> InspectBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => InspectBackup(backupPath, cancellationToken),
            cancellationToken);

    public async Task<DatabaseRestorePreparation> PrepareRestoreAsync(
        string databasePath,
        string backupPath,
        string currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var normalizedDatabasePath = NormalizeDatabasePath(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        var activeOtherInstances = _instanceRegistry.GetActiveInstances(
            normalizedDatabasePath,
            currentSessionId);
        if (activeOtherInstances.Count > 0)
        {
            throw new DatabaseRestoreBlockedException(
                "Chiudi le altre istanze di WeeklyPlanner che usano questo database prima di ripristinare un backup.",
                activeOtherInstances);
        }

        var selectedBackup = await InspectBackupAsync(backupPath, cancellationToken).ConfigureAwait(false);
        EnsureRestorable(selectedBackup);
        if (string.Equals(
                normalizedDatabasePath,
                selectedBackup.FilePath,
                PathComparison()))
        {
            throw new InvalidDataException("Il database operativo non può essere usato come file di backup per il restore.");
        }

        var requestedAtUtc = _clock.Now.ToUniversalTime();
        var request = new PendingDatabaseRestoreRequest(
            normalizedDatabasePath,
            selectedBackup.FilePath,
            currentSessionId,
            requestedAtUtc);

        await WritePendingRequestAsync(request, cancellationToken).ConfigureAwait(false);

        return new DatabaseRestorePreparation(
            normalizedDatabasePath,
            selectedBackup.FilePath,
            _options.PendingRestoreRequestPath,
            requestedAtUtc);
    }

    public Task CancelPreparedRestoreAsync(
        DatabaseRestorePreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                Path.GetFullPath(preparation.PendingRequestPath),
                Path.GetFullPath(_options.PendingRestoreRequestPath),
                PathComparison()))
        {
            throw new InvalidOperationException("La richiesta di ripristino non appartiene a questo servizio.");
        }

        DeletePendingRequest();
        return Task.CompletedTask;
    }

    public async Task<DatabaseRestoreStartupResult> ProcessPendingRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.PendingRestoreRequestPath))
        {
            return DatabaseRestoreStartupResult.None;
        }

        var restoreLock = TryAcquireRestoreLock();
        if (restoreLock is null)
        {
            return new DatabaseRestoreStartupResult(
                DatabaseRestoreStartupStatus.Blocked,
                "Un'altra istanza di WeeklyPlanner sta già elaborando il ripristino. Chiudi questa finestra e attendi il completamento dell'operazione.");
        }

        try
        {
            PendingDatabaseRestoreRequest? request;
            try
            {
                request = await ReadPendingRequestAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                DeletePendingRequest();
                return new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Failed,
                    $"La richiesta di ripristino non è leggibile ed è stata annullata. Dettagli: {ex.Message}");
            }

            if (request is null)
            {
                return DatabaseRestoreStartupResult.None;
            }

            var activeInstancesGone = await WaitForInstancesToCloseAsync(
                request.DatabasePath,
                cancellationToken).ConfigureAwait(false);
            if (!activeInstancesGone)
            {
                return new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Blocked,
                    "Il ripristino è stato rinviato perché un'altra istanza di WeeklyPlanner sta ancora usando il database. Chiudi tutte le istanze e riavvia l'app.",
                    request.DatabasePath,
                    request.BackupPath);
            }

            string? preRestoreBackupPath = null;
            try
            {
                var backup = await InspectBackupAsync(request.BackupPath, cancellationToken).ConfigureAwait(false);
                EnsureRestorable(backup);

                var preRestoreBackup = await CreateBackupAsync(
                    request.DatabasePath,
                    DatabaseBackupKind.PreRestore,
                    cancellationToken).ConfigureAwait(false);
                preRestoreBackupPath = preRestoreBackup.FilePath;

                await Task.Run(
                    () => RestoreDatabaseFile(request, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                DeletePendingRequest();
                return new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Succeeded,
                    $"Ripristino completato dal backup '{backup.FileName}'. Il database precedente è disponibile nel backup preventivo '{preRestoreBackup.FileName}'.",
                    request.DatabasePath,
                    request.BackupPath,
                    preRestoreBackup.FilePath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DeletePendingRequest();
                return new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Failed,
                    $"Il ripristino non è stato completato. WeeklyPlanner ha conservato o ripristinato il database precedente. Dettagli: {ex.Message}",
                    request.DatabasePath,
                    request.BackupPath,
                    preRestoreBackupPath);
            }
        }
        finally
        {
            restoreLock.Dispose();
        }
    }

    private DatabaseBackupInfo CreateBackup(
        string databasePath,
        DatabaseBackupKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedDatabasePath = NormalizeDatabasePath(databasePath);
        if (!File.Exists(normalizedDatabasePath))
        {
            throw new FileNotFoundException(
                "Il database da salvare non esiste.",
                normalizedDatabasePath);
        }

        Directory.CreateDirectory(BackupDirectory);

        using var source = OpenDatabase(normalizedDatabasePath, SqliteOpenMode.ReadOnly);
        _integrityChecker.EnsureIntegrity(source, "prima della creazione del backup manuale");
        var schemaVersion = ReadSchemaVersion(source);
        EnsureSupportedSchemaVersion(schemaVersion);

        var createdAtUtc = _clock.Now.ToUniversalTime();
        var prefix = kind == DatabaseBackupKind.PreRestore
            ? PreRestoreBackupPrefix
            : ManualBackupPrefix;
        var timestamp = createdAtUtc.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        var fileName = $"{prefix}-v{schemaVersion}-{timestamp}-{Guid.NewGuid():N}.db";
        var backupPath = Path.Combine(BackupDirectory, fileName);
        var temporaryBackupPath = backupPath + ".tmp";

        try
        {
            using (var destination = OpenDatabase(temporaryBackupPath, SqliteOpenMode.ReadWriteCreate))
            {
                source.BackupDatabase(destination);
                _integrityChecker.EnsureIntegrity(destination, "sul backup appena creato");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryBackupPath, backupPath, overwrite: false);
        }
        catch
        {
            TryDeleteFile(temporaryBackupPath);
            TryDeleteFile(backupPath);
            throw;
        }

        return InspectBackup(backupPath, cancellationToken);
    }

    private DatabaseBackupInfo InspectBackup(string backupPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        var normalizedPath = Path.GetFullPath(backupPath.Trim());
        var fileName = Path.GetFileName(normalizedPath);
        var kind = fileName.StartsWith(PreRestoreBackupPrefix, StringComparison.OrdinalIgnoreCase)
            ? DatabaseBackupKind.PreRestore
            : DatabaseBackupKind.Manual;

        if (!File.Exists(normalizedPath))
        {
            return new DatabaseBackupInfo(
                normalizedPath,
                fileName,
                kind,
                DateTimeOffset.MinValue,
                0,
                null,
                DatabaseBackupIntegrityStatus.Missing,
                "File non disponibile.");
        }

        var file = new FileInfo(normalizedPath);
        try
        {
            using var connection = OpenDatabase(normalizedPath, SqliteOpenMode.ReadOnly);
            _integrityChecker.EnsureIntegrity(connection, "sul backup selezionato");
            var schemaVersion = ReadSchemaVersion(connection);
            if (schemaVersion is null or < 1 || schemaVersion > _options.MaximumSupportedSchemaVersion)
            {
                return new DatabaseBackupInfo(
                    normalizedPath,
                    fileName,
                    kind,
                    file.LastWriteTimeUtc,
                    file.Length,
                    schemaVersion,
                    DatabaseBackupIntegrityStatus.Incompatible,
                    schemaVersion is null
                        ? "Il file non contiene una versione schema WeeklyPlanner riconoscibile."
                        : $"Schema v{schemaVersion} non supportato da questa versione dell'app.");
            }

            return new DatabaseBackupInfo(
                normalizedPath,
                fileName,
                kind,
                file.LastWriteTimeUtc,
                file.Length,
                schemaVersion,
                DatabaseBackupIntegrityStatus.Valid,
                "Integrità verificata.");
        }
        catch (DatabaseIntegrityException ex)
        {
            return new DatabaseBackupInfo(
                normalizedPath,
                fileName,
                kind,
                file.LastWriteTimeUtc,
                file.Length,
                null,
                DatabaseBackupIntegrityStatus.Corrupt,
                ex.IntegrityDetails);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new DatabaseBackupInfo(
                normalizedPath,
                fileName,
                kind,
                file.LastWriteTimeUtc,
                file.Length,
                null,
                DatabaseBackupIntegrityStatus.Error,
                ex.Message);
        }
    }

    private void RestoreDatabaseFile(
        PendingDatabaseRestoreRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var databaseDirectory = Path.GetDirectoryName(request.DatabasePath)
            ?? throw new InvalidOperationException("Impossibile determinare la cartella del database.");
        Directory.CreateDirectory(databaseDirectory);

        var restoreTempPath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(request.DatabasePath)}.restore-{Guid.NewGuid():N}.tmp");
        var previousDatabasePath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(request.DatabasePath)}.before-restore-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(request.BackupPath, restoreTempPath, overwrite: false);
            EnsureFileIntegrityAndCompatibility(restoreTempPath);
            cancellationToken.ThrowIfCancellationRequested();

            DeleteSqliteSidecarFiles(request.DatabasePath);
            var previousDatabaseSaved = false;
            if (File.Exists(request.DatabasePath))
            {
                ReplaceFile(restoreTempPath, request.DatabasePath, previousDatabasePath);
                previousDatabaseSaved = true;
            }
            else
            {
                File.Move(restoreTempPath, request.DatabasePath);
            }

            try
            {
                EnsureFileIntegrityAndCompatibility(request.DatabasePath);
                if (previousDatabaseSaved)
                {
                    TryDeleteFile(previousDatabasePath);
                }
            }
            catch
            {
                if (previousDatabaseSaved && File.Exists(previousDatabasePath))
                {
                    DeleteSqliteSidecarFiles(request.DatabasePath);
                    RestorePreviousDatabase(previousDatabasePath, request.DatabasePath);
                }

                throw;
            }
        }
        finally
        {
            TryDeleteFile(restoreTempPath);
        }
    }

    private async Task<bool> WaitForInstancesToCloseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.InstanceShutdownWaitTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_instanceRegistry.GetActiveInstances(databasePath).Count == 0)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(_options.InstanceShutdownPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private FileStream? TryAcquireRestoreLock()
    {
        var lockPath = GetRestoreLockPath();
        var directory = Path.GetDirectoryName(lockPath)
            ?? throw new InvalidOperationException("Impossibile determinare la cartella del lock restore.");
        Directory.CreateDirectory(directory);

        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetRestoreLockPath() => _options.PendingRestoreRequestPath + ".lock";

    private async Task WritePendingRequestAsync(
        PendingDatabaseRestoreRequest request,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_options.PendingRestoreRequestPath)
            ?? throw new InvalidOperationException("Impossibile determinare la cartella della richiesta restore.");
        Directory.CreateDirectory(directory);

        var tempPath = _options.PendingRestoreRequestPath + $".{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _options.PendingRestoreRequestPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task<PendingDatabaseRestoreRequest?> ReadPendingRequestAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.PendingRestoreRequestPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(
                _options.PendingRestoreRequestPath,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PendingDatabaseRestoreRequest>(json)
                ?? throw new InvalidDataException("La richiesta di ripristino è vuota.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "La richiesta di ripristino non è leggibile.",
                ex);
        }
    }

    private void DeletePendingRequest() => TryDeleteFile(_options.PendingRestoreRequestPath);

    private void EnsureFileIntegrityAndCompatibility(string path)
    {
        using var connection = OpenDatabase(path, SqliteOpenMode.ReadOnly);
        _integrityChecker.EnsureIntegrity(connection, "durante il ripristino");
        EnsureSupportedSchemaVersion(ReadSchemaVersion(connection));
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Cache = SqliteCacheMode.Default,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    private static int? ReadSchemaVersion(SqliteConnection connection)
    {
        var tableExists = connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';") > 0;
        if (!tableExists)
        {
            return null;
        }

        return connection.ExecuteScalar<int?>("SELECT MAX(Version) FROM SchemaVersion;");
    }

    private void EnsureSupportedSchemaVersion(int? schemaVersion)
    {
        if (schemaVersion is null or < 1)
        {
            throw new InvalidDataException(
                "Il file non contiene uno schema WeeklyPlanner riconoscibile.");
        }

        if (schemaVersion > _options.MaximumSupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Il database usa lo schema v{schemaVersion}, superiore alla versione supportata v{_options.MaximumSupportedSchemaVersion}.");
        }
    }

    private static void EnsureRestorable(DatabaseBackupInfo backup)
    {
        if (!backup.CanRestore)
        {
            throw new InvalidDataException(
                $"Il backup '{backup.FileName}' non può essere ripristinato: {backup.IntegrityMessage}");
        }
    }

    private static string NormalizeDatabasePath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return Path.GetFullPath(databasePath.Trim());
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void DeleteSqliteSidecarFiles(string databasePath)
    {
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
        TryDeleteFile(databasePath + "-journal");
    }

    private static void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
    {
        try
        {
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceFileWithFallback(sourcePath, destinationPath, backupPath);
        }
    }

    private static void ReplaceFileWithFallback(
        string sourcePath,
        string destinationPath,
        string backupPath)
    {
        File.Move(destinationPath, backupPath);
        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch
        {
            if (!File.Exists(destinationPath) && File.Exists(backupPath))
            {
                File.Move(backupPath, destinationPath);
            }

            throw;
        }
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
            File.Move(databasePath, invalidRestoredPath);
            try
            {
                File.Move(previousDatabasePath, databasePath);
                TryDeleteFile(invalidRestoredPath);
            }
            catch
            {
                if (!File.Exists(databasePath) && File.Exists(invalidRestoredPath))
                {
                    File.Move(invalidRestoredPath, databasePath);
                }

                throw;
            }
        }
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class DatabaseRestoreBlockedException : Exception
{
    public IReadOnlyList<DatabaseInstanceInfo> ActiveInstances { get; }

    public DatabaseRestoreBlockedException(
        string message,
        IReadOnlyList<DatabaseInstanceInfo> activeInstances)
        : base(message)
    {
        ActiveInstances = activeInstances;
    }
}
