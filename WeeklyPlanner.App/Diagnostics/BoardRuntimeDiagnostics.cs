namespace WeeklyPlanner.App.Diagnostics;

public sealed record BoardRuntimeDiagnostics(
    string ConnectionState,
    DateTimeOffset? LastSuccessfulSyncAt,
    bool HasActiveEdits,
    int ColumnCount,
    int CardCount,
    string ActiveDatabasePath);
