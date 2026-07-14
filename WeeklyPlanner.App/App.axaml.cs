using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using WeeklyPlanner.App.Composition;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App;

public partial class App : Application
{
    private ApplicationCompositionRoot? _compositionRoot;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _compositionRoot = new ApplicationCompositionRoot();
            _compositionRoot.RegisterGlobalExceptionHandling();
            _compositionRoot.Logger.Information(
                "application.start",
                "Avvio di WeeklyPlanner.",
                new Dictionary<string, object?>
                {
                    ["version"] = ApplicationVersionInfo.ProductVersion,
                    ["milestone"] = ApplicationVersionInfo.Milestone,
                });

            desktop.Exit += OnDesktopExit;

            var settings = _compositionRoot.SettingsService.Load();
            ApplyThemePreference(settings.ThemePreference);

            if (!settings.IsComplete())
            {
                OpenOnboarding(desktop, _compositionRoot, settings);
            }
            else
            {
                desktop.MainWindow = CreateMainWindow(_compositionRoot, settings);
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

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_compositionRoot is null)
        {
            return;
        }

        try
        {
            _compositionRoot.Logger.Information(
                "application.stop",
                "Chiusura di WeeklyPlanner.",
                new Dictionary<string, object?>
                {
                    ["exitCode"] = e.ApplicationExitCode,
                });
            _compositionRoot.Logger.FlushAsync().GetAwaiter().GetResult();
            _compositionRoot.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // La chiusura del processo non deve essere bloccata da un errore del logger.
        }
        finally
        {
            _compositionRoot = null;
        }
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
