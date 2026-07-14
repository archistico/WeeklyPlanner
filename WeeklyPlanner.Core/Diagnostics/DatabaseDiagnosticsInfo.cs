namespace WeeklyPlanner.Core.Diagnostics;

public sealed record DatabaseDiagnosticsInfo(
    bool FileExists,
    long? FileSizeBytes,
    int? SchemaVersion,
    string? ErrorMessage);
