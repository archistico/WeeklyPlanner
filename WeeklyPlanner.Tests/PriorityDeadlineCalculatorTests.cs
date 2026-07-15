using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class PriorityDeadlineCalculatorTests
{
    private static readonly DateTimeOffset AssignedAt =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_priority_has_no_due_date()
    {
        var dueAt = PriorityDeadlineCalculator.CalculateDueAt(
            AssignedAt,
            priorityId: null,
            cardTypeId: 5,
            priorities: [],
            deadlineRules: []);

        Assert.Null(dueAt);
    }

    [Fact]
    public void Default_duration_is_used_when_no_type_rule_exists()
    {
        var dueAt = PriorityDeadlineCalculator.CalculateDueAt(
            AssignedAt,
            priorityId: 3,
            cardTypeId: 4,
            priorities:
            [
                new PriorityDefinition { Id = 3, Code = "D", DefaultDueHours = 720 },
            ],
            deadlineRules: []);

        Assert.Equal(AssignedAt.AddDays(30), dueAt);
    }

    [Fact]
    public void Type_specific_rule_overrides_default_duration()
    {
        var dueAt = PriorityDeadlineCalculator.CalculateDueAt(
            AssignedAt,
            priorityId: 3,
            cardTypeId: 5,
            priorities:
            [
                new PriorityDefinition { Id = 3, Code = "D", DefaultDueHours = 720 },
            ],
            deadlineRules:
            [
                new PriorityTypeDeadline { PriorityId = 3, CardTypeId = 5, DueHours = 1440 },
            ]);

        Assert.Equal(AssignedAt.AddDays(60), dueAt);
    }

    [Fact]
    public void Unknown_priority_is_rejected()
    {
        Assert.Throws<KeyNotFoundException>(() => PriorityDeadlineCalculator.CalculateDueAt(
            AssignedAt,
            priorityId: 999,
            cardTypeId: null,
            priorities: [],
            deadlineRules: []));
    }
}
