using Dapper;
using Microsoft.Data.Sqlite;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Verifica la struttura interna SQLite e i riferimenti foreign key prima e dopo le migrazioni.
/// </summary>
public sealed class SqliteDatabaseIntegrityChecker : IDatabaseIntegrityChecker
{
    public void EnsureIntegrity(SqliteConnection connection, string operationDescription)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationDescription);

        var integrityResults = connection.Query<string>("PRAGMA integrity_check;").ToList();
        if (integrityResults.Count != 1 ||
            !string.Equals(integrityResults[0], "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseIntegrityException(
                operationDescription,
                integrityResults.Count == 0
                    ? "SQLite non ha restituito alcun risultato."
                    : string.Join(" | ", integrityResults));
        }

        var foreignKeyFailureCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_foreign_key_check;");
        if (foreignKeyFailureCount > 0)
        {
            var details = connection.ExecuteScalar<string?>(
                """
                SELECT group_concat(
                    'tabella=' || "table" ||
                    ', rowid=' || COALESCE(CAST(rowid AS TEXT), 'null') ||
                    ', riferimento=' || parent ||
                    ', fk=' || CAST(fkid AS TEXT),
                    ' | ')
                FROM
                (
                    SELECT "table", rowid, parent, fkid
                    FROM pragma_foreign_key_check
                    LIMIT 10
                );
                """) ?? $"{foreignKeyFailureCount} riferimenti non validi.";

            throw new DatabaseIntegrityException(operationDescription, details);
        }
    }
}

public sealed class DatabaseIntegrityException : Exception
{
    public string OperationDescription { get; }

    public string IntegrityDetails { get; }

    public DatabaseIntegrityException(string operationDescription, string integrityDetails)
        : base(
            $"Il database non ha superato il controllo di integrità {operationDescription}. " +
            $"Dettagli: {integrityDetails}")
    {
        OperationDescription = operationDescription;
        IntegrityDetails = integrityDetails;
    }
}
