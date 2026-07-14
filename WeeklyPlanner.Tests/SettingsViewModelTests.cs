using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-settings-viewmodel-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Save_persists_polling_theme_and_user_name_when_session_is_idle()
    {
        var service = CreateService();
        var existing = CreateCompleteSettings();
        service.Save(existing);
        var viewModel = new SettingsViewModel(
            service,
            existing,
            canEditIdentityAndDatabase: true,
            new RecordingFolderLauncher())
        {
            UserName = "  Nuovo nome  ",
            PollingIntervalSeconds = 15,
        };
        viewModel.SelectedThemeOption = viewModel.ThemeOptions.Single(option =>
            option.Value == AppThemePreference.Dark);
        SettingsSaveResult? result = null;
        viewModel.Completed += (_, saved) => result = saved;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Equal("Nuovo nome", result.Settings.UserName);
        Assert.Equal(15, result.Settings.PollingIntervalSeconds);
        Assert.Equal(AppThemePreference.Dark, result.Settings.ThemePreference);
        Assert.True(result.UserNameChanged);
        Assert.False(result.DatabasePathChanged);

        var reloaded = service.Load();
        Assert.Equal("Nuovo nome", reloaded.UserName);
        Assert.Equal(15, reloaded.PollingIntervalSeconds);
        Assert.Equal(AppThemePreference.Dark, reloaded.ThemePreference);
    }

    [Fact]
    public void Save_blocks_user_and_database_changes_during_active_editing()
    {
        var service = CreateService();
        var existing = CreateCompleteSettings();
        var viewModel = new SettingsViewModel(
            service,
            existing,
            canEditIdentityAndDatabase: false,
            new RecordingFolderLauncher())
        {
            UserName = "Altro utente",
        };
        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        viewModel.SaveCommand.Execute(null);

        Assert.False(completed);
        Assert.True(viewModel.HasValidationMessage);
        Assert.Contains("Termina la modifica", viewModel.ValidationMessage!);
    }

    [Fact]
    public void Save_allows_theme_and_polling_changes_during_active_editing()
    {
        var service = CreateService();
        var existing = CreateCompleteSettings();
        var viewModel = new SettingsViewModel(
            service,
            existing,
            canEditIdentityAndDatabase: false,
            new RecordingFolderLauncher())
        {
            PollingIntervalSeconds = 20,
        };
        viewModel.SelectedThemeOption = viewModel.ThemeOptions.Single(option =>
            option.Value == AppThemePreference.Light);
        SettingsSaveResult? result = null;
        viewModel.Completed += (_, saved) => result = saved;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Equal(20, result.Settings.PollingIntervalSeconds);
        Assert.Equal(AppThemePreference.Light, result.Settings.ThemePreference);
    }

    [Fact]
    public void Save_marks_database_change_as_requiring_restart()
    {
        var service = CreateService();
        var existing = CreateCompleteSettings();
        var viewModel = new SettingsViewModel(
            service,
            existing,
            canEditIdentityAndDatabase: true,
            new RecordingFolderLauncher())
        {
            DatabasePath = Path.Combine(_tempDirectory, "second", "planner.db"),
        };
        SettingsSaveResult? result = null;
        viewModel.Completed += (_, saved) => result = saved;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(result);
        Assert.True(result.DatabasePathChanged);
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void Open_folder_commands_use_the_launcher_without_starting_a_real_process()
    {
        var service = CreateService();
        var existing = CreateCompleteSettings();
        var launcher = new RecordingFolderLauncher();
        var viewModel = new SettingsViewModel(
            service,
            existing,
            canEditIdentityAndDatabase: true,
            launcher);

        viewModel.OpenDatabaseFolderCommand.Execute(null);
        viewModel.OpenApplicationDataFolderCommand.Execute(null);

        Assert.Contains(Path.GetDirectoryName(existing.DatabasePath)!, launcher.OpenedFolders);
        Assert.Contains(service.SettingsDirectoryPath, launcher.OpenedFolders);
    }

    private AppSettingsService CreateService() => new(
        Path.Combine(_tempDirectory, "config", "settings.json"));

    private AppSettings CreateCompleteSettings() => new()
    {
        DatabasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db"),
        UserName = "Emilie",
        PollingIntervalSeconds = 7,
        ThemePreference = AppThemePreference.System,
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingFolderLauncher : IFolderLauncher
    {
        public List<string> OpenedFolders { get; } = [];

        public void OpenFolder(string folderPath) => OpenedFolders.Add(folderPath);
    }
}
