using Avalonia;

namespace WeeklyPlanner.App.Interaction;

public static class WindowPlacementCalculator
{
    private const double MinimumVisibleLogicalPixels = 80;

    public static Size FitSizeToWorkingArea(
        double requestedWidth,
        double requestedHeight,
        PixelRect workingArea,
        double screenScaling,
        double minimumWidth,
        double minimumHeight)
    {
        var scaling = screenScaling > 0 && double.IsFinite(screenScaling)
            ? screenScaling
            : 1;
        var availableWidth = workingArea.Width / scaling;
        var availableHeight = workingArea.Height / scaling;

        return new Size(
            Math.Clamp(requestedWidth, minimumWidth, Math.Max(minimumWidth, availableWidth)),
            Math.Clamp(requestedHeight, minimumHeight, Math.Max(minimumHeight, availableHeight)));
    }

    public static PixelPoint ClampPosition(
        PixelPoint requestedPosition,
        Size logicalWindowSize,
        PixelRect workingArea,
        double screenScaling)
    {
        var scaling = screenScaling > 0 && double.IsFinite(screenScaling)
            ? screenScaling
            : 1;
        var visiblePixels = (int)Math.Ceiling(MinimumVisibleLogicalPixels * scaling);
        var widthPixels = (int)Math.Ceiling(logicalWindowSize.Width * scaling);

        var minimumX = workingArea.X - Math.Max(0, widthPixels - visiblePixels);
        var maximumX = Math.Max(
            minimumX,
            workingArea.X + workingArea.Width - visiblePixels);
        var minimumY = workingArea.Y;
        var maximumY = Math.Max(
            minimumY,
            workingArea.Y + workingArea.Height - visiblePixels);

        return new PixelPoint(
            Math.Clamp(requestedPosition.X, minimumX, maximumX),
            Math.Clamp(requestedPosition.Y, minimumY, maximumY));
    }
}
