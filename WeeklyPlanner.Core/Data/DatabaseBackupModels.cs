namespace WeeklyPlanner.Core.Data;

public enum DatabaseBackupKind
{
    Manual,
    PreRestore,
}

public enum DatabaseBackupIntegrityStatus
{
    Valid,
    Corrupt,
    Incompatible,
    Missing,
    Error,
}

public sealed record DatabaseBackupInfo(
    string FilePath,
    string FileName,
    DatabaseBackupKind Kind,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    int? SchemaVersion,
    DatabaseBackupIntegrityStatus IntegrityStatus,
    string IntegrityMessage)
{
    public bool CanRestore => IntegrityStatus == DatabaseBackupIntegrityStatus.Valid;
}

public sealed record DatabaseRestorePreparation(
    string DatabasePath,
    string BackupPath,
    string PendingRequestPath,
    DateTimeOffset RequestedAtUtc);

public enum DatabaseRestoreStartupStatus
{
    None,
    Succeeded,
    Failed,
    Blocked,
}

public sealed record DatabaseRestoreStartupResult(
    DatabaseRestoreStartupStatus Status,
    string Message,
    string? DatabasePath = null,
    string? BackupPath = null,
    string? PreRestoreBackupPath = null)
{
    public static DatabaseRestoreStartupResult None { get; } =
        new(DatabaseRestoreStartupStatus.None, string.Empty);

    public bool HasResult => Status != DatabaseRestoreStartupStatus.None;

    public bool IsSuccess => Status == DatabaseRestoreStartupStatus.Succeeded;
}

public sealed record PendingDatabaseRestoreRequest(
    string DatabasePath,
    string BackupPath,
    string RequestingSessionId,
    DateTimeOffset RequestedAtUtc);
