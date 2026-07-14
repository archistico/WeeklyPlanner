using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface IColumnRepository
{
    Task<IReadOnlyList<Column>> GetAllAsync(CancellationToken cancellationToken = default);
}
