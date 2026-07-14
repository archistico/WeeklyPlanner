using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using WeeklyPlanner.App.Composition;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var compositionRoot = new ApplicationCompositionRoot();
            var settings = compositionRoot.SettingsService.Load();
            ApplyThemePreference(settings.ThemePreference);

            if (!settings.IsComplete())
            {
                OpenOnboarding(desktop, compositionRoot, settings);
            }
            else
            {
                desktop.MainWindow = CreateMainWindow(compositionRoot, settings);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyThemePreference(AppThemePreference preference)
    {
        RequestedThemeVariant = preference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private static void OpenOnboarding(
        IClassicDesktopStyleApplicationLifetime desktop,
        ApplicationCompositionRoot compositionRoot,
        AppSettings settings)
    {
        var viewModel = compositionRoot.CreateOnboardingViewModel(settings);
        var window = new OnboardingWindow
        {
            DataContext = viewModel,
        };

        viewModel.Completed += (_, completedSettings) =>
        {
            if (Application.Current is App app)
            {
                app.ApplyThemePreference(completedSettings.ThemePreference);
            }

            var mainWindow = CreateMainWindow(compositionRoot, completedSettings);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            window.Close();
        };

        desktop.MainWindow = window;
    }

    private static MainWindow CreateMainWindow(
        ApplicationCompositionRoot compositionRoot,
        AppSettings settings)
    {
        var viewModel = compositionRoot.CreateBoardViewModel(settings);
        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        mainWindow.ConfigureApplicationServices(
            compositionRoot.SettingsService,
            compositionRoot,
            settings);
        mainWindow.Opened += async (_, _) => await viewModel.StartAsync();
        return mainWindow;
    }
}
