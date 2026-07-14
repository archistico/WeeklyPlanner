using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.Core.Resilience;

/// <summary>
/// Traduce eccezioni tecniche in categorie operative comprensibili senza affidarsi
/// al testo localizzato prodotto da SQLite o dal sistema operativo.
/// </summary>
public static class DatabaseFailureClassifier
{
    public static DatabaseFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var effectiveException = Unwrap(exception);
        return effectiveException switch
        {
            SqliteException sqliteException => ClassifySqlite(sqliteException.SqliteErrorCode),
            SchemaVersionMismatchException schemaException => CreateSchemaFailure(schemaException.Message),
            MissingMigrationException migrationException => CreateSchemaFailure(migrationException.Message),
            UnauthorizedAccessException => CreatePermissionDenied(),
            IOException => CreateUnavailable(),
            _ => new DatabaseFailure(
                DatabaseFailureKind.Unknown,
                "Si è verificato un errore inatteso durante l'accesso al database.",
                CanRetryAutomatically: false,
                RequiresAttention: true),
        };
    }

    private static DatabaseFailure ClassifySqlite(int errorCode) => errorCode switch
    {
        5 or 6 => new DatabaseFailure(
            DatabaseFailureKind.Contention,
            "Il database è temporaneamente occupato da un'altra operazione.",
            CanRetryAutomatically: true,
            RequiresAttention: false),
        8 => CreatePermissionDenied(),
        10 or 14 => CreateUnavailable(),
        11 or 26 => new DatabaseFailure(
            DatabaseFailureKind.Corrupt,
            "Il file del database non è valido o potrebbe essere danneggiato.",
            CanRetryAutomatically: false,
            RequiresAttention: true),
        13 => new DatabaseFailure(
            DatabaseFailureKind.StorageFull,
            "Il database non può essere scritto perché lo spazio disponibile è esaurito.",
            CanRetryAutomatically: false,
            RequiresAttention: true),
        17 => new DatabaseFailure(
            DatabaseFailureKind.Schema,
            "Lo schema del database è cambiato durante l'operazione.",
            CanRetryAutomatically: true,
            RequiresAttention: true),
        _ => new DatabaseFailure(
            DatabaseFailureKind.Unknown,
            "SQLite ha restituito un errore non previsto.",
            CanRetryAutomatically: false,
            RequiresAttention: true),
    };

    private static DatabaseFailure CreateSchemaFailure(string message) => new(
        DatabaseFailureKind.Schema,
        message,
        CanRetryAutomatically: false,
        RequiresAttention: true);

    private static DatabaseFailure CreateUnavailable() => new(
        DatabaseFailureKind.Unavailable,
        "Il file del database non è disponibile o non può essere aperto.",
        CanRetryAutomatically: true,
        RequiresAttention: false);

    private static DatabaseFailure CreatePermissionDenied() => new(
        DatabaseFailureKind.PermissionDenied,
        "Non disponi dei permessi necessari per leggere o scrivere il database.",
        CanRetryAutomatically: false,
        RequiresAttention: true);

    private static Exception Unwrap(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            var flattened = aggregateException.Flatten();
            if (flattened.InnerExceptions.Count == 1)
            {
                return Unwrap(flattened.InnerExceptions[0]);
            }
        }

        return exception.InnerException is null
            ? exception
            : Unwrap(exception.InnerException);
    }
}
