using System.Globalization;
using Dapper;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

/// <summary>
/// Legge revisione, workflow, cataloghi, card e lock in una sola transazione di lettura.
/// </summary>
public sealed class BoardSnapshotRepository : IBoardSnapshotRepository
{
    private const string CardSelect =
        "SELECT Id, ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, " +
        "PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc, " +
        "Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version " +
        "FROM Cards ORDER BY CardTypeId, ColumnId, SortOrder, Id;";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _readPipeline;
    private readonly TimeProvider _timeProvider;

    public BoardSnapshotRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline? readPipeline = null,
        TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<KanbanBoardSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction();

            var revision = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT Revision FROM BoardState WHERE Id = 1;",
                transaction: transaction,
                cancellationToken: token));

            var columns = (await connection.QueryAsync<Column>(new CommandDefinition(
                "SELECT Id, Name, SortOrder, SystemKey, IsSystem " +
                "FROM Columns ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var cards = (await connection.QueryAsync<Card>(new CommandDefinition(
                CardSelect,
                transaction: transaction,
                cancellationToken: token))).AsList();

            var priorities = (await connection.QueryAsync<PriorityDefinition>(new CommandDefinition(
                "SELECT Id, Code, Name, Description, DefaultDueHours, SortOrder, " +
                "IsActive, IsDefault, Version FROM Priorities ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var cardTypes = (await connection.QueryAsync<CardTypeDefinition>(new CommandDefinition(
                "SELECT Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version, " +
                "SystemKey, IsSystem FROM CardTypes ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var deadlineRules = (await connection.QueryAsync<PriorityTypeDeadline>(new CommandDefinition(
                "SELECT PriorityId, CardTypeId, DueHours, Version " +
                "FROM PriorityTypeDeadlines ORDER BY PriorityId, CardTypeId;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var activeLocks = (await connection.QueryAsync<CardEditLock>(new CommandDefinition(
                "SELECT CardId, SessionId, UserName, MachineName, AcquiredAtUtc, " +
                "LastHeartbeatUtc, ExpiresAtUtc FROM CardEditLocks " +
                "WHERE ExpiresAtUtc > @NowUtc ORDER BY CardId;",
                new { NowUtc = FormatUtc(_timeProvider.GetUtcNow()) },
                transaction,
                cancellationToken: token))).AsList();

            transaction.Commit();
            return new KanbanBoardSnapshot(
                revision,
                columns,
                cards,
                priorities,
                cardTypes,
                deadlineRules,
                activeLocks);
        }, cancellationToken);
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
