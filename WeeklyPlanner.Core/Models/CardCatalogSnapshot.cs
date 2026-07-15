namespace WeeklyPlanner.Core.Models;

public sealed record CardCatalogSnapshot(
    IReadOnlyList<PriorityDefinition> Priorities,
    IReadOnlyList<CardTypeDefinition> CardTypes,
    IReadOnlyList<PriorityTypeDeadline> DeadlineRules);
