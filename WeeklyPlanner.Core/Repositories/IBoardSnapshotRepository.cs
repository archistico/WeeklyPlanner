using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface IBoardSnapshotRepository
{
    Task<KanbanBoardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
