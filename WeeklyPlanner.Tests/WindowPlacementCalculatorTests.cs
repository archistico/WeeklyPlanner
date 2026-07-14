using Avalonia;
using WeeklyPlanner.App.Interaction;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class WindowPlacementCalculatorTests
{
    [Fact]
    public void FitSizeToWorkingArea_limits_a_large_window_to_the_current_screen()
    {
        var result = WindowPlacementCalculator.FitSizeToWorkingArea(
            requestedWidth: 3000,
            requestedHeight: 2000,
            workingArea: new PixelRect(0, 0, 1920, 1040),
            screenScaling: 1,
            minimumWidth: 720,
            minimumHeight: 480);

        Assert.Equal(new Size(1920, 1040), result);
    }

    [Fact]
    public void FitSizeToWorkingArea_converts_physical_pixels_using_screen_scaling()
    {
        var result = WindowPlacementCalculator.FitSizeToWorkingArea(
            requestedWidth: 1600,
            requestedHeight: 1000,
            workingArea: new PixelRect(0, 0, 1920, 1080),
            screenScaling: 1.5,
            minimumWidth: 720,
            minimumHeight: 480);

        Assert.Equal(new Size(1280, 720), result);
    }

    [Fact]
    public void ClampPosition_keeps_the_title_area_reachable()
    {
        var result = WindowPlacementCalculator.ClampPosition(
            new PixelPoint(5000, 5000),
            new Size(1100, 700),
            new PixelRect(0, 0, 1920, 1040),
            screenScaling: 1);

        Assert.Equal(new PixelPoint(1840, 960), result);
    }
}
