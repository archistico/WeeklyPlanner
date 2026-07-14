using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public async Task Load_populates_snapshot_and_plain_text()
    {
        var settings = CreateSettings();
        var snapshot = CreateSnapshot(settings);
        var launcher = new RecordingFolderLauncher();
        var viewModel = new DiagnosticsViewModel(
            settings,
            new BoardRuntimeDiagnostics("Online", null, false, 8, 1, settings.DatabasePath),
            new StubProvider(snapshot),
            launcher);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasSnapshot);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasError);
        Assert.Contains("WeeklyPlanner", viewModel.DiagnosticsText, StringComparison.Ordinal);

        viewModel.OpenLogFolderCommand.Execute(null);
        viewModel.OpenDatabaseFolderCommand.Execute(null);

        Assert.Contains(snapshot.LogDirectoryPath, launcher.Paths);
        Assert.Contains(Path.GetDirectoryName(settings.DatabasePath)!, launcher.Paths);
    }

    [Fact]
    public async Task Provider_error_is_exposed_without_throwing()
    {
        var viewModel = new DiagnosticsViewModel(
            CreateSettings(),
            new BoardRuntimeDiagnostics("Online", null, false, 8, 0, CreateSettings().DatabasePath),
            new ThrowingProvider(),
            new RecordingFolderLauncher());

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.HasSnapshot);
        Assert.Contains("Impossibile", viewModel.ErrorMessage!, StringComparison.Ordinal);
    }

    private static AppSettings CreateSettings() => new()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), "wp-data", "weeklyplanner.db"),
        UserName = "Emilie",
        PollingIntervalSeconds = 7,
    };

    private static ApplicationDiagnosticsSnapshot CreateSnapshot(AppSettings settings) => new(
        DateTimeOffset.UtcNow,
        "WeeklyPlanner 1.0",
        "M3.3",
        ".NET 10",
        "11.3.18",
        "Windows",
        "X64",
        "Emilie",
        "PC",
        "session",
        "Online",
        "Ora",
        8,
        1,
        settings.DatabasePath,
        "Disponibile",
        "1 MB",
        "3",
        "3",
        Path.Combine(Path.GetTempPath(), "settings.json"),
        Path.Combine(Path.GetTempPath(), "logs"),
        "Disponibile",
        Path.Combine(Path.GetTempPath(), "logs", "weeklyplanner.log"));

    private sealed class StubProvider(ApplicationDiagnosticsSnapshot snapshot)
        : IApplicationDiagnosticsProvider
    {
        public Task<ApplicationDiagnosticsSnapshot> CollectAsync(
            AppSettings settings,
            BoardRuntimeDiagnostics boardRuntime,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class ThrowingProvider : IApplicationDiagnosticsProvider
    {
        public Task<ApplicationDiagnosticsSnapshot> CollectAsync(
            AppSettings settings,
            BoardRuntimeDiagnostics boardRuntime,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test");
    }

    private sealed class RecordingFolderLauncher : IFolderLauncher
    {
        public List<string> Paths { get; } = [];

        public void OpenFolder(string folderPath) => Paths.Add(folderPath);
    }
}
