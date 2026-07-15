namespace WeeklyPlanner.Core.Models;

public sealed record PriorityDeadlineOverrideInput(long CardTypeId, int DueHours);

public sealed record PrioritySaveRequest(
    long? Id,
    int ExpectedVersion,
    string Code,
    string Name,
    string? Description,
    int DefaultDueHours,
    bool IsActive,
    bool IsDefault,
    IReadOnlyList<PriorityDeadlineOverrideInput> DeadlineOverrides);

public sealed record CardTypeSaveRequest(
    long? Id,
    int ExpectedVersion,
    string Name,
    string ColorHex,
    bool IsActive);

public sealed record CardTypeDeleteRequest(
    long CardTypeId,
    int ExpectedVersion,
    long? DestinationCardTypeId,
    int? DestinationExpectedVersion,
    string UpdatedBy);

public sealed record CatalogOrderItem(long Id, int ExpectedVersion);
