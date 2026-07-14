using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardViewModelSettingsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-board-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ApplyRuntimeSettings_updates_user_and_polling_without_switching_database()
    {
        var originalDatabase = Path.Combine(_tempDirectory, "current", "weeklyplanner.db");
        var viewModel = new BoardViewModel(new AppSettings
        {
            DatabasePath = originalDatabase,
            UserName = "Emilie",
            PollingIntervalSeconds = 7,
        });
        var updatedSettings = new AppSettings
        {
            DatabasePath = Path.Combine(_tempDirectory, "future", "weeklyplanner.db"),
            UserName = "Nuovo nome",
            PollingIntervalSeconds = 18,
        };

        viewModel.ApplyRuntimeSettings(updatedSettings, databaseChangeRequiresRestart: true);

        Assert.Equal("Nuovo nome", viewModel.CurrentUserName);
        Assert.Equal(18, viewModel.PollingIntervalSeconds);
        Assert.Equal(originalDatabase, viewModel.CurrentDatabasePath);
        Assert.Contains("prossimo avvio", viewModel.StatusMessage!);

        await viewModel.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
