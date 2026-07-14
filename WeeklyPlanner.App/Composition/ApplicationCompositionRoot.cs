using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.App.Composition;

public sealed class ApplicationCompositionRoot : IViewModelFactory
{
    private static readonly TimeSpan EditLockHeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly IApplicationSession _applicationSession;
    private readonly IClock _clock;
    private readonly IFolderLauncher _folderLauncher;

    public IAppSettingsService SettingsService { get; }

    public ApplicationCompositionRoot(
        IAppSettingsService? settingsService = null,
        IApplicationSession? applicationSession = null,
        IClock? clock = null,
        IFolderLauncher? folderLauncher = null)
    {
        SettingsService = settingsService ?? new AppSettingsService();
        _applicationSession = applicationSession ?? ApplicationSession.CreateDefault();
        _clock = clock ?? SystemClock.Instance;
        _folderLauncher = folderLauncher ?? new ShellFolderLauncher();
    }

    public OnboardingViewModel CreateOnboardingViewModel(AppSettings settings) =>
        new(SettingsService, settings);

    public SettingsViewModel CreateSettingsViewModel(
        AppSettings settings,
        bool canEditIdentityAndDatabase) =>
        new(
            SettingsService,
            settings,
            canEditIdentityAndDatabase,
            _folderLauncher);

    public BoardViewModel CreateBoardViewModel(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedSettings = settings.Clone();
        normalizedSettings.Normalize();

        var connectionFactory = new SqliteConnectionFactory(normalizedSettings.DatabasePath);
        var readPipeline = RetryPolicyFactory.CreateSqliteReadPipeline();
        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var databaseInitializer = new DatabaseInitializer(connectionFactory);
        var cardRepository = new CardRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline);
        var editLockRepository = new CardEditLockRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline);
        var columnRepository = new ColumnRepository(connectionFactory, readPipeline);
        var changeDetector = new BoardChangeDetector(
            new BoardRevisionRepository(connectionFactory, readPipeline));
        var pollingScheduler = new AvaloniaRecurringTaskScheduler(
            TimeSpan.FromSeconds(normalizedSettings.PollingIntervalSeconds));
        var heartbeatScheduler = new AvaloniaRecurringTaskScheduler(
            EditLockHeartbeatInterval);

        return new BoardViewModel(
            normalizedSettings,
            databaseInitializer,
            cardRepository,
            editLockRepository,
            columnRepository,
            changeDetector,
            pollingScheduler,
            heartbeatScheduler,
            _applicationSession,
            _clock);
    }
}
