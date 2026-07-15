using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface ICardEventRepository
{
    Task<IReadOnlyList<CardEvent>> GetByCardStableIdAsync(
        string cardStableId,
        int take = 50,
        long? beforeEventId = null,
        CancellationToken cancellationToken = default);
}
