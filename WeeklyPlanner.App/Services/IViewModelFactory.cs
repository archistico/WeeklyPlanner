using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.Services;

public interface IViewModelFactory
{
    OnboardingViewModel CreateOnboardingViewModel(AppSettings settings);

    BoardViewModel CreateBoardViewModel(AppSettings settings);

    SettingsViewModel CreateSettingsViewModel(
        AppSettings settings,
        bool canEditIdentityAndDatabase);

    BoardConfigurationViewModel CreateBoardConfigurationViewModel(string databasePath, string userName);

    DiagnosticsViewModel CreateDiagnosticsViewModel(
        AppSettings settings,
        BoardRuntimeDiagnostics boardRuntime);

    DatabaseSafetyViewModel CreateDatabaseSafetyViewModel(AppSettings settings);
}
