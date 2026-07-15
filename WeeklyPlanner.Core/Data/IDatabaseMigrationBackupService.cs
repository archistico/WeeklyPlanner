using Microsoft.Data.Sqlite;

namespace WeeklyPlanner.Core.Data;

public interface IDatabaseMigrationBackupService
{
    DatabaseMigrationBackup CreateBackup(
        SqliteConnection sourceConnection,
        int sourceSchemaVersion,
        int targetSchemaVersion);

    void RestoreBackup(DatabaseMigrationBackup backup, string databasePath);

    void ApplyRetention(DatabaseMigrationBackup backupToKeep);
}
