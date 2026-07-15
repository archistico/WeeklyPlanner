using Dapper;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class CardEventRepository : ICardEventRepository
{
    public const int MaxPageSize = 200;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _readPipeline;

    public CardEventRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline? readPipeline = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
    }

    public async Task<IReadOnlyList<CardEvent>> GetByCardStableIdAsync(
        string cardStableId,
        int take = 50,
        long? beforeEventId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardStableId);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxPageSize);

        const string sql =
            """
            SELECT Id, CardStableId, CardId, EventType, OccurredAtUtc, UserName,
                   SessionId, MachineName, Summary, DataJson, FormatVersion
            FROM CardEvents
            WHERE CardStableId = @CardStableId
              AND (@BeforeEventId IS NULL OR Id < @BeforeEventId)
            ORDER BY Id DESC
            LIMIT @Take;
            """;

        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var events = await connection.QueryAsync<CardEvent>(new CommandDefinition(
                sql,
                new { CardStableId = cardStableId, BeforeEventId = beforeEventId, Take = take },
                cancellationToken: token));
            return (IReadOnlyList<CardEvent>)events.AsList();
        }, cancellationToken);
    }
}
