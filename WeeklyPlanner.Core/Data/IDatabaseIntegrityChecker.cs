using Microsoft.Data.Sqlite;

namespace WeeklyPlanner.Core.Data;

public interface IDatabaseIntegrityChecker
{
    void EnsureIntegrity(SqliteConnection connection, string operationDescription);
}
