using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class PriorityDeadlineCalculatorTests
{
    private static readonly DateTimeOffset AssignedAt =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_duration_is_used_when_no_type_rule_exists()
    {
        PriorityDefinition[] priorities =
        [
            new() { Id = 3, Code = "D", DefaultDueHours = 720 },
        ];

        var dueHours = PriorityDeadlineCalculator.ResolveDueHours(
            priorityId: 3,
            cardTypeId: 4,
            priorities,
            deadlineRules: []);

        Assert.Equal(720, dueHours);
        Assert.Equal(AssignedAt.AddDays(30),
            PriorityDeadlineCalculator.CalculateDueAt(AssignedAt, dueHours));
    }

    [Fact]
    public void Type_specific_rule_overrides_default_duration()
    {
        PriorityDefinition[] priorities =
        [
            new() { Id = 3, Code = "D", DefaultDueHours = 720 },
        ];
        PriorityTypeDeadline[] rules =
        [
            new() { PriorityId = 3, CardTypeId = 5, DueHours = 1440 },
        ];

        var dueHours = PriorityDeadlineCalculator.ResolveDueHours(3, 5, priorities, rules);

        Assert.Equal(1440, dueHours);
        Assert.Equal(AssignedAt.AddDays(60),
            PriorityDeadlineCalculator.CalculateDueAt(AssignedAt, 720, dueHours));
    }

    [Fact]
    public void Unknown_priority_is_rejected()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            PriorityDeadlineCalculator.ResolveDueHours(
                priorityId: 999,
                cardTypeId: null,
                priorities: [],
                deadlineRules: []));
    }

    [Fact]
    public void Non_positive_duration_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PriorityDeadlineCalculator.CalculateDueAt(AssignedAt, defaultDueHours: 0));
    }
}
