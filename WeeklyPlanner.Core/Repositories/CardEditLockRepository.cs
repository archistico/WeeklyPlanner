using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class CardEditLockRepository : ICardEditLockRepository
{
    private const string LockSelect =
        "SELECT CardId, SessionId, UserName, MachineName, AcquiredAtUtc, " +
        "LastHeartbeatUtc, ExpiresAtUtc FROM CardEditLocks";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _writePipeline;
    private readonly ResiliencePipeline _readPipeline;
    private readonly TimeProvider _timeProvider;

    public CardEditLockRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline writePipeline,
        TimeProvider? timeProvider = null,
        ResiliencePipeline? readPipeline = null)
    {
        _connectionFactory = connectionFactory;
        _writePipeline = writePipeline;
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CardEditLockAcquisitionResult> TryAcquireAsync(
        long cardId,
        string sessionId,
        string userName,
        string? machineName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ValidateLeaseDuration(leaseDuration);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            await EnsureCardExistsAsync(connection, transaction, cardId, token);

            var now = _timeProvider.GetUtcNow();
            var nowUtc = FormatUtc(now);
            var expiresAtUtc = FormatUtc(now.Add(leaseDuration));

            await DeleteExpiredLocksAsync(connection, transaction, nowUtc, token);

            var command = new CommandDefinition(
                """
                INSERT INTO CardEditLocks
                    (CardId, SessionId, UserName, MachineName,
                     AcquiredAtUtc, LastHeartbeatUtc, ExpiresAtUtc)
                VALUES
                    (@CardId, @SessionId, @UserName, @MachineName,
                     @NowUtc, @NowUtc, @ExpiresAtUtc)
                ON CONFLICT(CardId) DO UPDATE SET
                    SessionId = excluded.SessionId,
                    UserName = excluded.UserName,
                    MachineName = excluded.MachineName,
                    AcquiredAtUtc = excluded.AcquiredAtUtc,
                    LastHeartbeatUtc = excluded.LastHeartbeatUtc,
                    ExpiresAtUtc = excluded.ExpiresAtUtc
                WHERE CardEditLocks.SessionId = excluded.SessionId;
                """,
                new
                {
                    CardId = cardId,
                    SessionId = sessionId,
                    UserName = userName,
                    MachineName = string.IsNullOrWhiteSpace(machineName) ? null : machineName.Trim(),
                    NowUtc = nowUtc,
                    ExpiresAtUtc = expiresAtUtc,
                },
                transaction,
                cancellationToken: token);

            await connection.ExecuteAsync(command);

            var currentLock = await GetByCardIdAsync(connection, transaction, cardId, token)
                ?? throw new InvalidOperationException(
                    $"Il lock della card {cardId} non è stato creato né recuperato.");

            transaction.Commit();
            return new CardEditLockAcquisitionResult(
                string.Equals(currentLock.SessionId, sessionId, StringComparison.Ordinal),
                currentLock);
        }, cancellationToken);
    }

    public async Task<bool> RenewAsync(
        long cardId,
        string sessionId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ValidateLeaseDuration(leaseDuration);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var now = _timeProvider.GetUtcNow();
            var command = new CommandDefinition(
                """
                UPDATE CardEditLocks
                SET LastHeartbeatUtc = @NowUtc,
                    ExpiresAtUtc = @ExpiresAtUtc
                WHERE CardId = @CardId
                  AND SessionId = @SessionId
                  AND ExpiresAtUtc > @NowUtc;
                """,
                new
                {
                    CardId = cardId,
                    SessionId = sessionId,
                    NowUtc = FormatUtc(now),
                    ExpiresAtUtc = FormatUtc(now.Add(leaseDuration)),
                },
                cancellationToken: token);

            return await connection.ExecuteAsync(command) == 1;
        }, cancellationToken);
    }

    public async Task ReleaseAsync(
        long cardId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(
                "DELETE FROM CardEditLocks WHERE CardId = @CardId AND SessionId = @SessionId;",
                new { CardId = cardId, SessionId = sessionId },
                cancellationToken: token);
            await connection.ExecuteAsync(command);
        }, cancellationToken);
    }

    public async Task ReleaseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(
                "DELETE FROM CardEditLocks WHERE SessionId = @SessionId;",
                new { SessionId = sessionId },
                cancellationToken: token);
            await connection.ExecuteAsync(command);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CardEditLock>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var sql = $"{LockSelect} WHERE ExpiresAtUtc > @NowUtc ORDER BY CardId;";

        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(
                sql,
                new { NowUtc = FormatUtc(_timeProvider.GetUtcNow()) },
                cancellationToken: token);
            var locks = await connection.QueryAsync<CardEditLock>(command);
            return (IReadOnlyList<CardEditLock>)locks.AsList();
        }, cancellationToken);
    }

    private static async Task EnsureCardExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM Cards WHERE Id = @CardId;",
            new { CardId = cardId },
            transaction,
            cancellationToken: cancellationToken);

        if (await connection.QuerySingleAsync<int>(command) == 0)
        {
            throw new KeyNotFoundException($"La card {cardId} non esiste.");
        }
    }

    private static async Task DeleteExpiredLocksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string nowUtc,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "DELETE FROM CardEditLocks WHERE ExpiresAtUtc <= @NowUtc;",
            new { NowUtc = nowUtc },
            transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static async Task<CardEditLock?> GetByCardIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            $"{LockSelect} WHERE CardId = @CardId;",
            new { CardId = cardId },
            transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CardEditLock>(command);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                leaseDuration,
                "La durata del lock deve essere maggiore di zero.");
        }
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
