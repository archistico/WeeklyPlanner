using Dapper;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class ColumnRepository : IColumnRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _readPipeline;

    public ColumnRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline? readPipeline = null)
    {
        _connectionFactory = connectionFactory;
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
    }

    public async Task<IReadOnlyList<Column>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(
                "SELECT Id, Name, SortOrder FROM Columns ORDER BY SortOrder;",
                cancellationToken: token);
            var columns = await connection.QueryAsync<Column>(command);
            return (IReadOnlyList<Column>)columns.AsList();
        }, cancellationToken);
    }
}
