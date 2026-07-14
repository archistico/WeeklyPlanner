using WeeklyPlanner.App.Interaction;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardMovePlannerTests
{
    [Fact]
    public void Up_moves_to_previous_insertion_index()
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: 1,
            sourceCardIndex: 2,
            cardCountsByColumn: [1, 4, 2],
            direction: CardMoveDirection.Up,
            plan: out var plan);

        Assert.True(result);
        Assert.Equal(new CardMovePlan(1, 1), plan);
    }

    [Fact]
    public void Down_uses_pre_removal_insertion_index_expected_by_repository()
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: 1,
            sourceCardIndex: 1,
            cardCountsByColumn: [1, 4, 2],
            direction: CardMoveDirection.Down,
            plan: out var plan);

        Assert.True(result);
        Assert.Equal(new CardMovePlan(1, 3), plan);
    }

    [Theory]
    [InlineData(CardMoveDirection.Up, 0)]
    [InlineData(CardMoveDirection.Down, 2)]
    public void Vertical_boundary_returns_false(CardMoveDirection direction, int sourceCardIndex)
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: 0,
            sourceCardIndex: sourceCardIndex,
            cardCountsByColumn: [3],
            direction: direction,
            plan: out _);

        Assert.False(result);
    }

    [Fact]
    public void Previous_column_preserves_vertical_position_when_possible()
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: 1,
            sourceCardIndex: 2,
            cardCountsByColumn: [5, 4],
            direction: CardMoveDirection.PreviousColumn,
            plan: out var plan);

        Assert.True(result);
        Assert.Equal(new CardMovePlan(0, 2), plan);
    }

    [Fact]
    public void Next_column_clamps_position_to_shorter_target_column()
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: 0,
            sourceCardIndex: 3,
            cardCountsByColumn: [4, 1],
            direction: CardMoveDirection.NextColumn,
            plan: out var plan);

        Assert.True(result);
        Assert.Equal(new CardMovePlan(1, 1), plan);
    }

    [Theory]
    [InlineData(CardMoveDirection.PreviousColumn, 0)]
    [InlineData(CardMoveDirection.NextColumn, 1)]
    public void Horizontal_boundary_returns_false(CardMoveDirection direction, int sourceColumnIndex)
    {
        var result = CardMovePlanner.TryCreate(
            sourceColumnIndex: sourceColumnIndex,
            sourceCardIndex: 0,
            cardCountsByColumn: [1, 1],
            direction: direction,
            plan: out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Drop_before_or_after_the_same_card_is_a_no_op(int targetIndex)
    {
        Assert.False(CardMovePlanner.WouldChangePosition(
            sourceColumnIndex: 0,
            sourceCardIndex: 1,
            sourceColumnCount: 3,
            targetColumnIndex: 0,
            targetIndex: targetIndex));
    }

    [Fact]
    public void Drop_after_the_next_card_changes_position()
    {
        Assert.True(CardMovePlanner.WouldChangePosition(
            sourceColumnIndex: 0,
            sourceCardIndex: 1,
            sourceColumnCount: 4,
            targetColumnIndex: 0,
            targetIndex: 3));
    }

    [Fact]
    public void Drop_in_another_column_always_changes_position()
    {
        Assert.True(CardMovePlanner.WouldChangePosition(
            sourceColumnIndex: 0,
            sourceCardIndex: 0,
            sourceColumnCount: 1,
            targetColumnIndex: 1,
            targetIndex: 0));
    }
}
