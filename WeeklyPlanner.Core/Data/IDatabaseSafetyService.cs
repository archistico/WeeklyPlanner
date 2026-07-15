namespace WeeklyPlanner.Core.Data;

public interface IDatabaseSafetyService
{
    string BackupDirectory { get; }

    Task<DatabaseBackupInfo> CreateBackupAsync(
        string databasePath,
        DatabaseBackupKind kind = DatabaseBackupKind.Manual,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseBackupInfo>> ListBackupsAsync(
        CancellationToken cancellationToken = default);

    Task<DatabaseBackupInfo> InspectBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default);

    Task<DatabaseRestorePreparation> PrepareRestoreAsync(
        string databasePath,
        string backupPath,
        string currentSessionId,
        CancellationToken cancellationToken = default);

    Task CancelPreparedRestoreAsync(
        DatabaseRestorePreparation preparation,
        CancellationToken cancellationToken = default);

    Task<DatabaseRestoreStartupResult> ProcessPendingRestoreAsync(
        CancellationToken cancellationToken = default);
}
