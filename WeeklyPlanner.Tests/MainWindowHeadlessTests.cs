using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WeeklyPlanner.App;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Main_window_loads_real_xaml_and_exposes_the_five_accessible_add_actions()
    {
        var window = new MainWindow
        {
            DataContext = new object(),
            Width = 1440,
            Height = 900,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(window.FindControl<ScrollViewer>("BoardScrollViewer"));
            Assert.NotNull(window.FindControl<ItemsControl>("SwimlanesItemsControl"));

            var addButtons = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Select(button => AutomationProperties.GetName(button))
                .Where(name => name?.StartsWith("Aggiungi card in ", StringComparison.Ordinal) == true)
                .ToList();

            Assert.Equal(5, addButtons.Count);
            Assert.Contains("Aggiungi card in BACKLOG", addButtons);
            Assert.Contains("Aggiungi card in DONE", addButtons);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(AppThemePreference.Light)]
    [InlineData(AppThemePreference.Dark)]
    public void Main_window_loads_under_each_explicit_theme(AppThemePreference preference)
    {
        var app = Assert.IsType<global::WeeklyPlanner.App.App>(Application.Current);
        app.ApplyThemePreference(preference);

        var expectedVariant = preference == AppThemePreference.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        Assert.Equal(expectedVariant, app.RequestedThemeVariant);

        var window = new MainWindow
        {
            DataContext = new object(),
            Width = 1200,
            Height = 800,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(window.Background);
            Assert.True(window.Bounds.Width > 0);
            Assert.True(window.Bounds.Height > 0);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
            app.ApplyThemePreference(AppThemePreference.System);
        }
    }
}
