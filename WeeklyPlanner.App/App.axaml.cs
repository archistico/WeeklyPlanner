using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using WeeklyPlanner.App.Composition;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;

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

            var restoreResult = ProcessPendingDatabaseRestore(_compositionRoot);
            var settings = _compositionRoot.SettingsService.Load();
            ApplyThemePreference(settings.ThemePreference);

            if (restoreResult.Status == DatabaseRestoreStartupStatus.Blocked)
            {
                desktop.MainWindow = new DatabaseRestoreResultWindow(restoreResult)
                {
                    ShowActivated = true,
                    ShowInTaskbar = true,
                };
            }
            else if (!settings.IsComplete())
            {
                OpenOnboarding(desktop, _compositionRoot, settings, restoreResult);
            }
            else
            {
                var mainWindow = CreateMainWindow(_compositionRoot, settings);
                AttachRestoreResultNotice(mainWindow, restoreResult);
                desktop.MainWindow = mainWindow;
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
        AppSettings settings,
        DatabaseRestoreStartupResult restoreResult)
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
            AttachRestoreResultNotice(mainWindow, restoreResult);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            window.Close();
            Dispatcher.UIThread.Post(() => mainWindow.Activate(), DispatcherPriority.Background);
        };

        desktop.MainWindow = window;
    }


    private static DatabaseRestoreStartupResult ProcessPendingDatabaseRestore(
        ApplicationCompositionRoot compositionRoot)
    {
        try
        {
            var result = compositionRoot
                .ProcessPendingDatabaseRestoreAsync()
                .GetAwaiter()
                .GetResult();
            if (result.HasResult)
            {
                compositionRoot.Logger.Information(
                    "database.restore_startup_result",
                    result.Message,
                    new Dictionary<string, object?>
                    {
                        ["status"] = result.Status,
                        ["databasePath"] = result.DatabasePath,
                        ["backupPath"] = result.BackupPath,
                        ["preRestoreBackupPath"] = result.PreRestoreBackupPath,
                    });
            }

            return result;
        }
        catch (Exception ex)
        {
            compositionRoot.Logger.Log(
                AppLogLevel.Error,
                "database.restore_startup_failed",
                "Errore non gestito durante il ripristino differito.",
                exception: ex);
            return new DatabaseRestoreStartupResult(
                DatabaseRestoreStartupStatus.Failed,
                $"Il ripristino non è stato eseguito a causa di un errore inatteso: {ex.Message}");
        }
    }

    private static void AttachRestoreResultNotice(
        MainWindow mainWindow,
        DatabaseRestoreStartupResult restoreResult)
    {
        if (!restoreResult.HasResult)
        {
            return;
        }

        mainWindow.Opened += async (_, _) =>
        {
            var noticeWindow = new DatabaseRestoreResultWindow(restoreResult);
            await noticeWindow.ShowDialog(mainWindow);
        };
    }

    private static MainWindow CreateMainWindow(
        ApplicationCompositionRoot compositionRoot,
        AppSettings settings)
    {
        var viewModel = compositionRoot.CreateBoardViewModel(settings);
        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
            ShowActivated = true,
            ShowInTaskbar = true,
            WindowState = Avalonia.Controls.WindowState.Maximized,
        };

        mainWindow.ConfigureApplicationServices(
            compositionRoot,
            settings,
            compositionRoot.ApplicationRestarter);
        mainWindow.Opened += async (_, _) => await viewModel.StartAsync();
        return mainWindow;
    }
}
