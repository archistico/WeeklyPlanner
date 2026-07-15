using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseSafetyViewModelTests
{
    [Fact]
    public async Task Load_exposes_backup_metadata_and_folder_command()
    {
        var service = new StubDatabaseSafetyService
        {
            ListedBackups =
            [
                CreateBackup("backup-one.db", DatabaseBackupIntegrityStatus.Valid),
                CreateBackup("backup-two.db", DatabaseBackupIntegrityStatus.Incompatible),
            ],
        };
        var launcher = new RecordingFolderLauncher();
        var viewModel = CreateViewModel(service, launcher);

        await viewModel.LoadAsync();
        viewModel.OpenBackupFolderCommand.Execute(null);

        Assert.Equal(2, viewModel.Backups.Count);
        Assert.True(viewModel.HasBackups);
        Assert.False(viewModel.HasNoBackups);
        Assert.NotNull(viewModel.SelectedBackup);
        Assert.Equal(service.BackupDirectory, Assert.Single(launcher.Paths));
    }

    [Fact]
    public async Task CreateBackup_refreshes_and_selects_the_new_copy()
    {
        var service = new StubDatabaseSafetyService();
        var created = CreateBackup("new-backup.db", DatabaseBackupIntegrityStatus.Valid);
        service.CreatedBackup = created;
        service.ListedBackups = [created];
        var viewModel = CreateViewModel(service, new RecordingFolderLauncher());

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(created.FilePath, viewModel.SelectedBackup?.FilePath);
        Assert.True(viewModel.HasOperationMessage);
        Assert.Contains("Backup creato", viewModel.OperationMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmRestore_prepares_request_and_raises_restart_event()
    {
        var backup = CreateBackup("selected.db", DatabaseBackupIntegrityStatus.Valid);
        var service = new StubDatabaseSafetyService
        {
            ListedBackups = [backup],
            Preparation = new DatabaseRestorePreparation(
                CreateSettings().DatabasePath,
                backup.FilePath,
                Path.Combine(Path.GetTempPath(), "pending.json"),
                DateTimeOffset.UtcNow),
        };
        var viewModel = CreateViewModel(service, new RecordingFolderLauncher());
        DatabaseRestorePreparation? raised = null;
        viewModel.RestorePrepared += (_, preparation) => raised = preparation;
        await viewModel.LoadAsync();

        viewModel.BeginRestoreCommand.Execute(null);
        Assert.True(viewModel.IsRestoreConfirmationVisible);

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.NotNull(raised);
        Assert.Equal(service.Preparation, raised);
        Assert.Equal(1, service.PrepareCalls);
        Assert.False(viewModel.IsRestoreConfirmationVisible);
    }

    [Fact]
    public async Task Invalid_backup_cannot_enter_restore_confirmation()
    {
        var service = new StubDatabaseSafetyService
        {
            ListedBackups = [CreateBackup("invalid.db", DatabaseBackupIntegrityStatus.Corrupt)],
        };
        var viewModel = CreateViewModel(service, new RecordingFolderLauncher());
        await viewModel.LoadAsync();

        viewModel.BeginRestoreCommand.Execute(null);

        Assert.False(viewModel.CanPrepareRestore);
        Assert.False(viewModel.IsRestoreConfirmationVisible);
        Assert.Equal(0, service.PrepareCalls);
    }

    private static DatabaseSafetyViewModel CreateViewModel(
        IDatabaseSafetyService service,
        IFolderLauncher launcher) =>
        new(CreateSettings(), "session-test", service, launcher);

    private static AppSettings CreateSettings() => new()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), "weeklyplanner-vm-tests", "weeklyplanner.db"),
        UserName = "Emilie",
        PollingIntervalSeconds = 7,
    };

    private static DatabaseBackupInfo CreateBackup(
        string fileName,
        DatabaseBackupIntegrityStatus status)
    {
        var path = Path.Combine(Path.GetTempPath(), "weeklyplanner-vm-backups", fileName);
        return new DatabaseBackupInfo(
            path,
            fileName,
            DatabaseBackupKind.Manual,
            DateTimeOffset.UtcNow,
            1024,
            status == DatabaseBackupIntegrityStatus.Valid ? 5 : null,
            status,
            status == DatabaseBackupIntegrityStatus.Valid ? "Integrità verificata." : "Non valido.");
    }

    private sealed class StubDatabaseSafetyService : IDatabaseSafetyService
    {
        public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), "weeklyplanner-vm-backups");

        public IReadOnlyList<DatabaseBackupInfo> ListedBackups { get; set; } = [];

        public DatabaseBackupInfo? CreatedBackup { get; set; }

        public DatabaseRestorePreparation? Preparation { get; set; }

        public int CreateCalls { get; private set; }

        public int PrepareCalls { get; private set; }

        public Task<DatabaseBackupInfo> CreateBackupAsync(
            string databasePath,
            DatabaseBackupKind kind = DatabaseBackupKind.Manual,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(CreatedBackup ?? throw new InvalidOperationException("Backup non configurato."));
        }

        public Task<IReadOnlyList<DatabaseBackupInfo>> ListBackupsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ListedBackups);

        public Task<DatabaseBackupInfo> InspectBackupAsync(
            string backupPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ListedBackups.Single(item => item.FilePath == backupPath));

        public Task<DatabaseRestorePreparation> PrepareRestoreAsync(
            string databasePath,
            string backupPath,
            string currentSessionId,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(Preparation ?? throw new InvalidOperationException("Preparazione non configurata."));
        }

        public Task CancelPreparedRestoreAsync(
            DatabaseRestorePreparation preparation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DatabaseRestoreStartupResult> ProcessPendingRestoreAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DatabaseRestoreStartupResult.None);
    }

    private sealed class RecordingFolderLauncher : IFolderLauncher
    {
        public List<string> Paths { get; } = [];

        public void OpenFolder(string folderPath) => Paths.Add(folderPath);
    }
}
