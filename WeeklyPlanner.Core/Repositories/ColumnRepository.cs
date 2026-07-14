using Dapper;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public sealed class ColumnRepository : IColumnRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ColumnRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Column>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.Create();
        var command = new CommandDefinition(
            "SELECT Id, Name, SortOrder FROM Columns ORDER BY SortOrder;",
            cancellationToken: cancellationToken);
        var columns = await connection.QueryAsync<Column>(command);
        return columns.AsList();
    }
}
