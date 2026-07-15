using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public static class PriorityDeadlineCalculator
{
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

        var priority = priorities.SingleOrDefault(item => item.Id == priorityId.Value)
            ?? throw new KeyNotFoundException($"La priorità {priorityId.Value} non esiste.");

        var dueHours = cardTypeId is null
            ? priority.DefaultDueHours
            : deadlineRules
                .SingleOrDefault(rule =>
                    rule.PriorityId == priorityId.Value &&
                    rule.CardTypeId == cardTypeId.Value)
                ?.DueHours ?? priority.DefaultDueHours;

        return assignedAtUtc.AddHours(dueHours);
    }
}
