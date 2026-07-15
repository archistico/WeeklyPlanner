namespace WeeklyPlanner.Core.Data;

public sealed record DatabaseMigrationBackup(
    string FilePath,
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    DateTimeOffset CreatedAtUtc);
