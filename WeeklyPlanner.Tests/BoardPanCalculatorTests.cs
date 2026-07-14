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
            new Point(150, 80));

        Assert.Equal(new Vector(150, 25), result);
    }

    [Fact]
    public void DraggingPointerRight_DecreasesHorizontalOffset()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 25),
            new Point(200, 50),
            new Point(240, 20));

        Assert.Equal(new Vector(60, 25), result);
    }

    [Fact]
    public void VerticalPointerMovement_DoesNotChangeVerticalOffset()
    {
        Vector result = BoardPanCalculator.CalculateOffset(
            new Vector(100, 25),
            new Point(200, 50),
            new Point(200, 500));

        Assert.Equal(new Vector(100, 25), result);
    }
}
