namespace WeeklyPlanner.Core.Data;

public sealed class DatabaseSafetyOptions
{
    public string BackupDirectory { get; init; } = GetDefaultBackupDirectory();

    public string PendingRestoreRequestPath { get; init; } = GetDefaultPendingRestoreRequestPath();

    public int MaximumSupportedSchemaVersion { get; init; } = DatabaseInitializer.ExpectedSchemaVersion;

    public TimeSpan InstanceShutdownWaitTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan InstanceShutdownPollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(BackupDirectory))
        {
            throw new ArgumentException("La cartella dei backup deve essere un percorso assoluto.", nameof(BackupDirectory));
        }

        if (!Path.IsPathFullyQualified(PendingRestoreRequestPath))
        {
            throw new ArgumentException("Il file di richiesta restore deve avere un percorso assoluto.", nameof(PendingRestoreRequestPath));
        }

        if (MaximumSupportedSchemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSupportedSchemaVersion));
        }

        if (InstanceShutdownWaitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InstanceShutdownWaitTimeout));
        }

        if (InstanceShutdownPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InstanceShutdownPollInterval));
        }
    }

    public static string GetDefaultBackupDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeeklyPlanner",
        "Backups",
        "Manual");

    public static string GetDefaultPendingRestoreRequestPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeeklyPlanner",
        "Data",
        "pending-restore.json");
}
