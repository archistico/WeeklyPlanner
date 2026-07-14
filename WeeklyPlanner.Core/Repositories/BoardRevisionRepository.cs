using Dapper;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class BoardRevisionRepository : IBoardRevisionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _readPipeline;

    public BoardRevisionRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline? readPipeline = null)
    {
        _connectionFactory = connectionFactory;
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
    }

    public async Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Revision FROM BoardState WHERE Id = 1;";

        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(sql, cancellationToken: token);
            return await connection.QuerySingleAsync<long>(command);
        }, cancellationToken);
    }
}
