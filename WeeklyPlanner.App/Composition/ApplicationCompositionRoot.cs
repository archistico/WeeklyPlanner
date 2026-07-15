using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Diagnostics;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.App.Composition;

public sealed class ApplicationCompositionRoot : IViewModelFactory, IAsyncDisposable
{
    private static readonly TimeSpan EditLockHeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly IApplicationSession _applicationSession;
    private readonly IClock _clock;
    private readonly IFolderLauncher _folderLauncher;
    private readonly IErrorReferenceGenerator _errorReferences;
    private readonly IApplicationDiagnosticsProvider _diagnosticsProvider;
    private readonly GlobalExceptionMonitor _globalExceptionMonitor;
    private int _disposed;

    public IAppSettingsService SettingsService { get; }

    public IAppLogger Logger { get; }

    public ApplicationCompositionRoot(
        IAppSettingsService? settingsService = null,
        IApplicationSession? applicationSession = null,
        IClock? clock = null,
        IFolderLauncher? folderLauncher = null,
        IAppLogger? logger = null,
        IErrorReferenceGenerator? errorReferences = null,
        IDatabaseDiagnosticsReader? databaseDiagnosticsReader = null)
    {
        SettingsService = settingsService ?? new AppSettingsService();
        _applicationSession = applicationSession ?? ApplicationSession.CreateDefault();
        _clock = clock ?? SystemClock.Instance;
        _folderLauncher = folderLauncher ?? new ShellFolderLauncher();
        Logger = logger ?? new FileAppLogger(clock: _clock);
        _errorReferences = errorReferences ?? new ErrorReferenceGenerator();
        _globalExceptionMonitor = new GlobalExceptionMonitor(Logger, _errorReferences);
        _diagnosticsProvider = new ApplicationDiagnosticsProvider(
            Logger,
            SettingsService,
            _applicationSession,
            databaseDiagnosticsReader ?? new DatabaseDiagnosticsReader(),
            _clock);
    }

    public void RegisterGlobalExceptionHandling() => _globalExceptionMonitor.Register();

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

    public BoardConfigurationViewModel CreateBoardConfigurationViewModel(
        string databasePath,
        string userName)
    {
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var auditContextProvider = new ApplicationSessionCardAuditContextProvider(_applicationSession);
        var repository = new CardCatalogRepository(
            connectionFactory,
            RetryPolicyFactory.CreateSqliteReadPipeline(),
            RetryPolicyFactory.CreateSqliteWritePipeline(),
            auditContextProvider: auditContextProvider);
        return new BoardConfigurationViewModel(
            repository,
            Logger,
            _errorReferences,
            userName);
    }

    public DiagnosticsViewModel CreateDiagnosticsViewModel(
        AppSettings settings,
        BoardRuntimeDiagnostics boardRuntime) =>
        new(settings, boardRuntime, _diagnosticsProvider, _folderLauncher);

    public BoardViewModel CreateBoardViewModel(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedSettings = settings.Clone();
        normalizedSettings.Normalize();

        var connectionFactory = new SqliteConnectionFactory(normalizedSettings.DatabasePath);
        var readPipeline = RetryPolicyFactory.CreateSqliteReadPipeline();
        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var integrityChecker = new SqliteDatabaseIntegrityChecker();
        var migrationBackupService = new SqliteDatabaseMigrationBackupService(
            integrityChecker: integrityChecker,
            clock: _clock);
        var databaseInitializer = new DatabaseInitializer(
            connectionFactory,
            new EmbeddedDatabaseMigrationCatalog(),
            integrityChecker,
            migrationBackupService);
        var cardRepository = new CardRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline,
            auditContextProvider: new ApplicationSessionCardAuditContextProvider(_applicationSession));
        var editLockRepository = new CardEditLockRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline);
        var snapshotRepository = new BoardSnapshotRepository(
            connectionFactory,
            readPipeline);
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
            snapshotRepository,
            changeDetector,
            pollingScheduler,
            heartbeatScheduler,
            _applicationSession,
            _clock,
            Logger,
            _errorReferences);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _globalExceptionMonitor.Dispose();
        await Logger.DisposeAsync().ConfigureAwait(false);
    }
}
