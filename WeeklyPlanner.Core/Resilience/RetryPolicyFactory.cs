using Microsoft.Data.Sqlite;
using Polly;
using Polly.Retry;

namespace WeeklyPlanner.Core.Resilience;

/// <summary>
/// Policy di retry limitate ai lock temporanei SQLite. Gli errori strutturali,
/// di percorso, permessi o spazio vengono propagati immediatamente alla UI.
/// </summary>
public static class RetryPolicyFactory
{
    private static readonly TimeSpan[] WriteBackoff =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    private static readonly TimeSpan[] ReadBackoff =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
    ];

    public static ResiliencePipeline CreateSqliteWritePipeline() =>
        CreateSqliteLockPipeline(WriteBackoff);

    public static ResiliencePipeline CreateSqliteReadPipeline() =>
        CreateSqliteLockPipeline(ReadBackoff);

    private static ResiliencePipeline CreateSqliteLockPipeline(TimeSpan[] backoff)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SqliteException>(IsTransientLock),
                MaxRetryAttempts = backoff.Length,
                DelayGenerator = args => new ValueTask<TimeSpan?>(
                    args.AttemptNumber < backoff.Length
                        ? backoff[args.AttemptNumber]
                        : backoff[^1]),
            })
            .Build();
    }

    /// <summary>
    /// SQLITE_BUSY = 5, SQLITE_LOCKED = 6. Sono gli unici codici per cui ha senso ritentare.
    /// </summary>
    private static bool IsTransientLock(SqliteException ex) => ex.SqliteErrorCode is 5 or 6;
}
