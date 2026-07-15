using Avalonia;
using WeeklyPlanner.App.Interaction;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardPanCalculatorTests
{
    [Fact]
    public void DraggingPointerLeft_IncreasesHorizontalOffset()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 25),
            new Point(200, 50),
            new Point(150, 50));

        Assert.Equal(new Vector(150, 25), result);
    }

    [Fact]
    public void DraggingPointerRight_DecreasesHorizontalOffset()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 25),
            new Point(200, 50),
            new Point(240, 50));

        Assert.Equal(new Vector(60, 25), result);
    }

    [Fact]
    public void DraggingPointerUp_IncreasesVerticalOffset()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 25),
            new Point(200, 100),
            new Point(200, 40));

        Assert.Equal(new Vector(100, 85), result);
    }

    [Fact]
    public void Diagonal_drag_updates_both_offsets()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 75),
            new Point(200, 100),
            new Point(150, 140));

        Assert.Equal(new Vector(150, 35), result);
    }
}
