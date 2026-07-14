using Microsoft.Data.Sqlite;
using Polly;
using Polly.Retry;

namespace WeeklyPlanner.Core.Resilience;

/// <summary>
/// Policy di retry per brevi contese sul database SQLite locale: tre tentativi con backoff
/// (200ms / 500ms / 1000ms) esclusivamente sui codici di lock temporaneo.
/// </summary>
public static class RetryPolicyFactory
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    public static ResiliencePipeline CreateSqliteWritePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SqliteException>(IsTransientLock),
                MaxRetryAttempts = Backoff.Length,
                DelayGenerator = args => new ValueTask<TimeSpan?>(
                    args.AttemptNumber < Backoff.Length ? Backoff[args.AttemptNumber] : Backoff[^1]),
            })
            .Build();
    }

    /// <summary>
    /// SQLITE_BUSY = 5, SQLITE_LOCKED = 6. Sono gli unici codici per cui ha senso ritentare:
    /// altri errori (schema non valido, disco pieno, ecc.) vanno propagati subito.
    /// </summary>
    private static bool IsTransientLock(SqliteException ex) => ex.SqliteErrorCode is 5 or 6;
}
