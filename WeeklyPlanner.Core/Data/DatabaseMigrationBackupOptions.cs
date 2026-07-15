namespace WeeklyPlanner.Core.Data;

public sealed class DatabaseMigrationBackupOptions
{
    public const int DefaultRetentionCount = 5;

    public string BackupDirectory { get; init; } = GetDefaultBackupDirectory();

    public int RetentionCount { get; init; } = DefaultRetentionCount;

    public static string GetDefaultBackupDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = Path.GetTempPath();
        }

        return Path.Combine(
            localApplicationData,
            "WeeklyPlanner",
            "Backups",
            "Migrations");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BackupDirectory))
        {
            throw new ArgumentException("La cartella dei backup di migrazione è obbligatoria.");
        }

        if (!Path.IsPathFullyQualified(BackupDirectory))
        {
            throw new ArgumentException(
                "La cartella dei backup di migrazione deve avere un percorso assoluto.");
        }

        if (RetentionCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetentionCount),
                "Deve essere conservato almeno un backup di migrazione.");
        }
    }
}
