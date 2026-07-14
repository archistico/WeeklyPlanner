namespace WeeklyPlanner.Core.Repositories;

/// <summary>
/// Espone la revisione monotona della board usata dal polling per rilevare ogni modifica.
/// </summary>
public interface IBoardRevisionRepository
{
    Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default);
}
