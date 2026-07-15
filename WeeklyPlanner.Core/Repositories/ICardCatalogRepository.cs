using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface ICardCatalogRepository
{
    Task<CardCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<PriorityDefinition> SavePriorityAsync(
        PrioritySaveRequest request,
        CancellationToken cancellationToken = default);

    Task DeletePriorityAsync(
        long priorityId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task ReorderPrioritiesAsync(
        IReadOnlyList<CatalogOrderItem> orderedItems,
        CancellationToken cancellationToken = default);

    Task<CardTypeDefinition> SaveCardTypeAsync(
        CardTypeSaveRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteCardTypeAsync(
        CardTypeDeleteRequest request,
        CancellationToken cancellationToken = default);

    Task ReorderCardTypesAsync(
        IReadOnlyList<CatalogOrderItem> orderedItems,
        CancellationToken cancellationToken = default);
}
