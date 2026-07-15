using Avalonia;

namespace WeeklyPlanner.App.Interaction;

public static class BoardPanCalculator
{
    public static Vector CalculateOffset(
        Vector startOffset,
        Point startPointerPosition,
        Point currentPointerPosition)
    {
        double horizontalDelta = currentPointerPosition.X - startPointerPosition.X;
        double verticalDelta = currentPointerPosition.Y - startPointerPosition.Y;
        return new Vector(
            startOffset.X - horizontalDelta,
            startOffset.Y - verticalDelta);
    }
}
