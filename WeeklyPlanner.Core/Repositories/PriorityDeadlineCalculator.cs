using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

/// <summary>
/// Unica fonte di verità per la risoluzione delle scadenze di una priorità,
/// inclusi gli override specifici della fascia.
/// </summary>
public static class PriorityDeadlineCalculator
{
    public static int ResolveDueHours(
        long priorityId,
        long? cardTypeId,
        IReadOnlyCollection<PriorityDefinition> priorities,
        IReadOnlyCollection<PriorityTypeDeadline> deadlineRules)
    {
        ArgumentNullException.ThrowIfNull(priorities);
        ArgumentNullException.ThrowIfNull(deadlineRules);

        var priority = priorities.SingleOrDefault(item => item.Id == priorityId)
            ?? throw new KeyNotFoundException($"La priorità {priorityId} non esiste.");

        return cardTypeId is null
            ? priority.DefaultDueHours
            : deadlineRules
                .SingleOrDefault(rule => rule.PriorityId == priorityId && rule.CardTypeId == cardTypeId.Value)
                ?.DueHours ?? priority.DefaultDueHours;
    }

    public static DateTimeOffset? CalculateDueAt(
        DateTimeOffset assignedAtUtc,
        long? priorityId,
        long? cardTypeId,
        IReadOnlyCollection<PriorityDefinition> priorities,
        IReadOnlyCollection<PriorityTypeDeadline> deadlineRules)
    {
        if (priorityId is null)
        {
            return null;
        }

        return CalculateDueAt(
            assignedAtUtc,
            ResolveDueHours(priorityId.Value, cardTypeId, priorities, deadlineRules));
    }

    public static DateTimeOffset CalculateDueAt(
        DateTimeOffset assignedAtUtc,
        int defaultDueHours,
        int? overrideDueHours = null)
    {
        var dueHours = overrideDueHours ?? defaultDueHours;
        if (dueHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultDueHours), "La scadenza deve essere maggiore di zero ore.");
        }

        return assignedAtUtc.ToUniversalTime().AddHours(dueHours);
    }
}
