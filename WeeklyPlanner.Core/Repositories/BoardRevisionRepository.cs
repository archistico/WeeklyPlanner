using Dapper;
using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.Core.Repositories;

public sealed class BoardRevisionRepository : IBoardRevisionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public BoardRevisionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Revision FROM BoardState WHERE Id = 1;";

        using var connection = _connectionFactory.Create();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<long>(command);
    }
}
