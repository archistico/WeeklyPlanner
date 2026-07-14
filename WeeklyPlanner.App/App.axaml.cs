using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using WeeklyPlanner.App.ViewModels;
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
            var settingsService = new AppSettingsService();
            var settings = settingsService.Load();
            ApplyThemePreference(settings.ThemePreference);

            if (!settings.IsComplete())
            {
                OpenOnboarding(desktop, settingsService, settings);
            }
            else
            {
                desktop.MainWindow = CreateMainWindow(settingsService, settings);
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
        AppSettingsService settingsService,
        AppSettings settings)
    {
        var viewModel = new OnboardingViewModel(settingsService, settings);
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

            var mainWindow = CreateMainWindow(settingsService, completedSettings);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            window.Close();
        };

        desktop.MainWindow = window;
    }

    private static MainWindow CreateMainWindow(
        AppSettingsService settingsService,
        AppSettings settings)
    {
        var viewModel = new BoardViewModel(settings);
        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        mainWindow.ConfigureSettings(settingsService, settings);
        mainWindow.Opened += async (_, _) => await viewModel.StartAsync();
        return mainWindow;
    }
}
