namespace WeeklyPlanner.Core.Diagnostics;

public interface IDatabaseDiagnosticsReader
{
    Task<DatabaseDiagnosticsInfo> ReadAsync(
        string databasePath,
        CancellationToken cancellationToken = default);
}
