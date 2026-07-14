using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using WeeklyPlanner.App.Composition;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App;

public partial class App : Application
{
    private static readonly TimeSpan ApplicationShutdownTimeout = TimeSpan.FromSeconds(2);

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
        var compositionRoot = _compositionRoot;
        _compositionRoot = null;
        if (compositionRoot is null)
        {
            return;
        }

        try
        {
            compositionRoot.Logger.Information(
                "application.stop",
                "Chiusura di WeeklyPlanner.",
                new Dictionary<string, object?>
                {
                    ["exitCode"] = e.ApplicationExitCode,
                });

            // L'evento Exit è sincrono: il cleanup viene atteso, ma con un limite esplicito.
            // Il Dispose del logger completa la coda e scarica i record residui senza richiedere
            // un Flush separato, che in caso di worker guasto potrebbe attendere indefinitamente.
            BoundedAsyncDisposer.TryDispose(
                compositionRoot,
                ApplicationShutdownTimeout);
        }
        catch
        {
            // L'uscita del processo deve sempre proseguire, anche se la diagnostica è guasta.
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
