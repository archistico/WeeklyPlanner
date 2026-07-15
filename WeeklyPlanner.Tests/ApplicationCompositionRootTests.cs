using WeeklyPlanner.App.Composition;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Time;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ApplicationCompositionRootTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-composition-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Composition_root_creates_the_complete_viewmodel_graph()
    {
        var settingsService = new AppSettingsService(
            Path.Combine(_tempDirectory, "settings.json"));
        await using var root = new ApplicationCompositionRoot(
            settingsService,
            new ApplicationSession("session-test", "PC-TEST"),
            SystemClock.Instance,
            new StubFolderLauncher(),
            NullAppLogger.Instance);
        var settings = CreateSettings();

        var onboarding = root.CreateOnboardingViewModel(settings);
        var board = root.CreateBoardViewModel(settings);
        var preferences = root.CreateSettingsViewModel(
            settings,
            canEditIdentityAndDatabase: true);
        var boardConfiguration = root.CreateBoardConfigurationViewModel(settings.DatabasePath, settings.UserName);
        var diagnostics = root.CreateDiagnosticsViewModel(
            settings,
            board.GetRuntimeDiagnostics());

        Assert.NotNull(onboarding);
        Assert.NotNull(board);
        Assert.NotNull(preferences);
        Assert.NotNull(boardConfiguration);
        Assert.NotNull(diagnostics);
        Assert.Same(settingsService, root.SettingsService);

        await board.DisposeAsync();
    }

    [Fact]
    public async Task Creating_board_viewmodel_does_not_create_or_open_database()
    {
        var settings = CreateSettings();
        var databasePath = settings.DatabasePath;
        await using var root = new ApplicationCompositionRoot(
            new AppSettingsService(Path.Combine(_tempDirectory, "settings.json")),
            new ApplicationSession("session-test", "PC-TEST"),
            SystemClock.Instance,
            new StubFolderLauncher(),
            NullAppLogger.Instance);

        var board = root.CreateBoardViewModel(settings);
        var boardConfiguration = root.CreateBoardConfigurationViewModel(databasePath, settings.UserName);

        Assert.NotNull(boardConfiguration);
        Assert.False(File.Exists(databasePath));

        await board.DisposeAsync();
        Assert.False(File.Exists(databasePath));
    }

    private AppSettings CreateSettings() => new()
    {
        DatabasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db"),
        UserName = "Emilie",
        PollingIntervalSeconds = 7,
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class StubFolderLauncher : IFolderLauncher
    {
        public void OpenFolder(string folderPath)
        {
        }
    }
}
