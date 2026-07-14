using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface ICardEditLockRepository
{
    Task<CardEditLockAcquisitionResult> TryAcquireAsync(
        long cardId,
        string sessionId,
        string userName,
        string? machineName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        long cardId,
        string sessionId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        long cardId,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task ReleaseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CardEditLock>> GetActiveAsync(
        CancellationToken cancellationToken = default);
}
