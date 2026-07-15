using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Skia;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(WeeklyPlanner.Tests.AvaloniaHeadlessTestBootstrap))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WeeklyPlanner.Tests;

public static class AvaloniaHeadlessTestBootstrap
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WeeklyPlanner.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}
