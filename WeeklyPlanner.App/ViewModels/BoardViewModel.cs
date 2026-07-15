using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class BoardViewModel : ViewModelBase, IAsyncDisposable
{
    private const int ConsecutiveFailuresBeforeOffline = 3;
    private static readonly TimeSpan EditLockLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EditLockHeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly AppSettings _settings;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ICardRepository _cardRepository;
    private readonly ICardEventRepository _cardEventRepository;
    private readonly ICardEditLockRepository _editLockRepository;
    private readonly IBoardSnapshotRepository _snapshotRepository;
    private readonly IBoardChangeDetector _changeDetector;
    private readonly IRecurringTaskScheduler _pollingScheduler;
    private readonly IRecurringTaskScheduler _lockHeartbeatScheduler;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IApplicationSession _applicationSession;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly IErrorReferenceGenerator _errorReferences;
    private readonly IDatabaseInstanceLease? _databaseInstanceLease;

    private int _consecutiveFailures;
    private bool _automaticRetryAllowed = true;
    private bool _isInitialized;
    private bool _hasLoadedSuccessfully;
    private bool _isStarted;
    private bool _isDisposed;
    private bool _isBusy;
    private BoardConnectionState _connectionState = BoardConnectionState.Connecting;
    private string? _activityText;
    private string? _statusMessage;
    private DateTimeOffset? _lastSuccessfulSyncAt;

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();

    /// <summary>
    /// Proiezione bidimensionale della board usata dal layout a swimlane.
    /// Le collection Columns restano la sorgente tecnica per repository e operazioni esistenti.
    /// </summary>
    public ObservableCollection<SwimlaneViewModel> Swimlanes { get; } = new();

    public bool HasSwimlanes => Swimlanes.Count > 0;

    public ColumnViewModel? BacklogColumn => GetColumnBySystemKey(WorkflowColumnKeys.Backlog);

    public ColumnViewModel? TodoColumn => GetColumnBySystemKey(WorkflowColumnKeys.Todo);

    public ColumnViewModel? InProgressColumn => GetColumnBySystemKey(WorkflowColumnKeys.InProgress);

    public ColumnViewModel? TestingColumn => GetColumnBySystemKey(WorkflowColumnKeys.Testing);

    public ColumnViewModel? DoneColumn => GetColumnBySystemKey(WorkflowColumnKeys.Done);

    public IReadOnlyList<PriorityDefinition> Priorities { get; private set; } = [];

    public IReadOnlyList<CardTypeDefinition> CardTypes { get; private set; } = [];

    public IReadOnlyList<PriorityTypeDeadline> DeadlineRules { get; private set; } = [];

    public long BoardRevision { get; private set; }

    public BoardConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            var previous = _connectionState;
            if (SetProperty(ref _connectionState, value))
            {
                _logger.Information(
                    "connection.state_changed",
                    "Lo stato della connessione è cambiato.",
                    new Dictionary<string, object?>
                    {
                        ["previousState"] = previous,
                        ["currentState"] = value,
                    });
                RaiseOperationalStateChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(CanModifyBoard));
                OnPropertyChanged(nameof(CanRetryNow));
            }
        }
    }

    public bool IsLoading => IsBusy && ConnectionState == BoardConnectionState.Connecting;

    public bool IsOnline => ConnectionState == BoardConnectionState.Online;

    public bool IsConnecting => ConnectionState == BoardConnectionState.Connecting;

    public bool IsRecovering => ConnectionState == BoardConnectionState.Recovering;

    public bool IsConnectionPending => IsConnecting || IsRecovering;

    public bool IsOffline => ConnectionState is BoardConnectionState.Offline or BoardConnectionState.Error;

    public bool HasConnectionError => ConnectionState == BoardConnectionState.Error;

    public bool CanRetry => ConnectionState is
        BoardConnectionState.Recovering or BoardConnectionState.Offline or BoardConnectionState.Error;

    public bool CanRetryNow => CanRetry && !IsBusy && !_isDisposed;

    public bool CanModifyBoard => IsOnline && !IsBusy && !_isDisposed;

    public string ConnectionStatusText => ConnectionState switch
    {
        BoardConnectionState.Connecting => "Connessione al database...",
        BoardConnectionState.Online => "Database online",
        BoardConnectionState.Recovering => "Connessione instabile",
        BoardConnectionState.Offline => "Database non disponibile",
        BoardConnectionState.Error => "Errore database",
        BoardConnectionState.ShuttingDown => "Chiusura in corso...",
        _ => "Stato sconosciuto",
    };

    public string? ActivityText
    {
        get => _activityText;
        private set
        {
            if (SetProperty(ref _activityText, value))
            {
                OnPropertyChanged(nameof(HasActivityText));
            }
        }
    }

    public bool HasActivityText => !string.IsNullOrWhiteSpace(ActivityText);

    public string? LastSuccessfulSyncText => _lastSuccessfulSyncAt is null
        ? null
        : $"Ultimo aggiornamento {_lastSuccessfulSyncAt.Value.ToLocalTime():HH:mm:ss}";

    public bool HasLastSuccessfulSync => _lastSuccessfulSyncAt is not null;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
                OnPropertyChanged(nameof(HasWarningStatusMessage));
                OnPropertyChanged(nameof(HasErrorStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasWarningStatusMessage => HasStatusMessage && !HasConnectionError;

    public bool HasErrorStatusMessage => HasStatusMessage && HasConnectionError;

    public string CurrentUserName => _settings.UserName;

    public string CurrentDatabasePath => _settings.DatabasePath;

    public int PollingIntervalSeconds => _settings.PollingIntervalSeconds;

    public string ApplicationMilestone => ApplicationVersionInfo.Milestone;

    public string WindowTitle => ApplicationVersionInfo.WindowTitle;

    public bool HasActiveEdits => Columns
        .SelectMany(column => column.Cards)
        .Any(card => card.IsEditing);

    public bool CanChangeIdentityAndDatabaseSettings =>
        !_isDisposed && !IsBusy && !HasActiveEdits;

    public BoardRuntimeDiagnostics GetRuntimeDiagnostics() => new(
        ConnectionStatusText,
        _lastSuccessfulSyncAt,
        HasActiveEdits,
        Columns.Count,
        Columns.Sum(column => column.Cards.Count),
        CurrentDatabasePath);

    public CardInformationViewModel CreateCardInformationViewModel(CardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var columnName = Columns
            .FirstOrDefault(column => column.Id == card.Model.ColumnId)?.Name
            ?? $"Stato #{card.Model.ColumnId}";
        var cardTypeName = card.Model.CardTypeId is long cardTypeId
            ? CardTypes.FirstOrDefault(cardType => cardType.Id == cardTypeId)?.Name
                ?? $"Fascia #{cardTypeId}"
            : "Fascia non disponibile";
        var priorityText = card.Model.PriorityId is long priorityId
            ? BuildPriorityText(priorityId)
            : "Nessuna priorità";

        return new CardInformationViewModel(
            card.Model,
            columnName,
            cardTypeName,
            priorityText,
            _cardEventRepository,
            _editLockRepository,
            _applicationSession.SessionId);
    }

    private string BuildPriorityText(long priorityId)
    {
        var priority = Priorities.FirstOrDefault(candidate => candidate.Id == priorityId);
        if (priority is null)
        {
            return $"Priorità #{priorityId}";
        }

        return string.IsNullOrWhiteSpace(priority.Code)
            ? priority.Name
            : $"{priority.Code} — {priority.Name}";
    }

    public void ApplyRuntimeSettings(AppSettings settings, bool databaseChangeRequiresRestart)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Clone();
        normalized.Normalize();

        if (HasActiveEdits && !string.Equals(
                normalized.UserName,
                _settings.UserName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Non è possibile cambiare utente mentre una card è in modifica.");
        }

        _settings.UserName = normalized.UserName;
        _settings.PollingIntervalSeconds = normalized.PollingIntervalSeconds;
        _pollingScheduler.Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);

        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(PollingIntervalSeconds));

        StatusMessage = databaseChangeRequiresRestart
            ? "Impostazioni salvate. Il nuovo database verrà usato al prossimo avvio."
            : null;

        _logger.Information(
            "settings.runtime_applied",
            "Le impostazioni applicabili a runtime sono state aggiornate.",
            new Dictionary<string, object?>
            {
                ["pollingIntervalSeconds"] = _settings.PollingIntervalSeconds,
                ["databaseRestartRequired"] = databaseChangeRequiresRestart,
            });
    }

    public BoardViewModel(
        AppSettings settings,
        IDatabaseInitializer databaseInitializer,
        ICardRepository cardRepository,
        ICardEventRepository cardEventRepository,
        ICardEditLockRepository editLockRepository,
        IBoardSnapshotRepository snapshotRepository,
        IBoardChangeDetector changeDetector,
        IRecurringTaskScheduler pollingScheduler,
        IRecurringTaskScheduler lockHeartbeatScheduler,
        IApplicationSession applicationSession,
        IClock clock,
        IAppLogger? logger = null,
        IErrorReferenceGenerator? errorReferences = null,
        IDatabaseInstanceLease? databaseInstanceLease = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(databaseInitializer);
        ArgumentNullException.ThrowIfNull(cardRepository);
        ArgumentNullException.ThrowIfNull(cardEventRepository);
        ArgumentNullException.ThrowIfNull(editLockRepository);
        ArgumentNullException.ThrowIfNull(snapshotRepository);
        ArgumentNullException.ThrowIfNull(changeDetector);
        ArgumentNullException.ThrowIfNull(pollingScheduler);
        ArgumentNullException.ThrowIfNull(lockHeartbeatScheduler);
        ArgumentNullException.ThrowIfNull(applicationSession);
        ArgumentNullException.ThrowIfNull(clock);

        _settings = settings.Clone();
        _settings.Normalize();
        _databaseInitializer = databaseInitializer;
        _cardRepository = cardRepository;
        _cardEventRepository = cardEventRepository;
        _editLockRepository = editLockRepository;
        _snapshotRepository = snapshotRepository;
        _changeDetector = changeDetector;
        _pollingScheduler = pollingScheduler;
        _lockHeartbeatScheduler = lockHeartbeatScheduler;
        _applicationSession = applicationSession;
        _clock = clock;
        _logger = logger ?? NullAppLogger.Instance;
        _errorReferences = errorReferences ?? new ErrorReferenceGenerator();
        _databaseInstanceLease = databaseInstanceLease;

        _pollingScheduler.Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
        _lockHeartbeatScheduler.Interval = EditLockHeartbeatInterval;
    }

    public async Task StartAsync()
    {
        if (_isDisposed || _isStarted)
        {
            return;
        }

        _isStarted = true;
        _logger.Information(
            "board.start",
            "Avvio della board.",
            new Dictionary<string, object?>
            {
                ["databasePath"] = _settings.DatabasePath,
                ["pollingIntervalSeconds"] = _settings.PollingIntervalSeconds,
                ["sessionId"] = ShortSessionId(),
            });
        StatusMessage = null;
        ConnectionState = BoardConnectionState.Connecting;
        await RefreshBoardSafelyAsync(
            isInitialLoad: true,
            waitForGate: true,
            showActivity: true);

        if (!_isDisposed)
        {
            _pollingScheduler.Start(PollBoardAsync, _lifetimeCancellation.Token);
            _lockHeartbeatScheduler.Start(HeartbeatEditingLocksAsync, _lifetimeCancellation.Token);
            _logger.Information(
                "board.schedulers_started",
                "Polling e heartbeat sono stati avviati.",
                new Dictionary<string, object?>
                {
                    ["pollingIntervalSeconds"] = _settings.PollingIntervalSeconds,
                    ["heartbeatIntervalSeconds"] = EditLockHeartbeatInterval.TotalSeconds,
                });
        }
    }

    public async Task<bool> BeginEditCardAsync(CardViewModel? card)
    {
        if (_isDisposed || card is null)
        {
            return false;
        }

        if (card.IsEditing)
        {
            return true;
        }

        if (!IsOnline)
        {
            StatusMessage = "La modifica non è disponibile finché il database non torna online.";
            return false;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        SetActivity("Acquisizione lock...");
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                // Un secondo GotFocus può essersi accodato mentre il primo acquisiva il lease.
                if (card.IsEditing)
                {
                    return true;
                }

                var result = await _editLockRepository.TryAcquireAsync(
                    card.Model.Id,
                    _applicationSession.SessionId,
                    _settings.UserName,
                    _applicationSession.MachineName,
                    EditLockLeaseDuration,
                    cancellationToken);

                card.ApplyLockState(result.CurrentLock, _applicationSession.SessionId);
                if (!result.Acquired)
                {
                    card.RefreshFromModel(card.Model);
                    MarkConnectionHealthy();
                    StatusMessage = $"{result.CurrentLock.UserName} sta già modificando questa card.";
                    _logger.Warning(
                        "card.lock_denied",
                        "Il lock della card è già posseduto da un'altra sessione.",
                        properties: new Dictionary<string, object?>
                        {
                            ["cardId"] = card.Model.Id,
                            ["lockOwner"] = result.CurrentLock.UserName,
                        });
                    return false;
                }

                card.BeginEdit(result.CurrentLock, _applicationSession.SessionId);
                _logger.Information(
                    "card.lock_acquired",
                    "Lock di modifica acquisito.",
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = card.Model.Id,
                        ["sessionId"] = ShortSessionId(),
                    });
                MarkConnectionHealthy();
                StatusMessage = null;
                return true;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            card.RefreshFromModel(card.Model);
            SetOperationFailure(ex);
            return false;
        }
        finally
        {
            ClearActivity();
        }
    }

    [RelayCommand]
    private async Task CommitEditAsync(CardViewModel? card)
    {
        if (_isDisposed || card is null || !card.IsEditing)
        {
            return;
        }

        if (!IsOnline)
        {
            const string message =
                "Salvataggio non disponibile: il database non è online. La bozza resta conservata.";
            card.MarkSaveError(message);
            StatusMessage = message;
            return;
        }

        if (card.IsDirty && !card.IsTitleValid)
        {
            card.MarkSaveError(card.TitleValidationMessage);
            StatusMessage = card.TitleValidationMessage;
            return;
        }

        var isSavingContent = card.IsDirty;
        var contentPersisted = false;
        if (isSavingContent)
        {
            card.BeginSaving();
        }

        var cancellationToken = _lifetimeCancellation.Token;
        SetActivity(isSavingContent ? "Salvataggio card..." : "Chiusura modifica...");
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                if (card.IsDeletedExternally)
                {
                    const string message =
                        "La card è stata eliminata altrove. Annulla per chiudere la bozza.";
                    card.MarkSaveError(message);
                    StatusMessage = message;
                    return;
                }

                if (card.HasLostEditLock)
                {
                    const string message =
                        "Il lock di modifica non è più valido. La bozza è conservata.";
                    card.MarkSaveError(message);
                    StatusMessage = message;
                    return;
                }

                if (card.HasExternalChanges)
                {
                    const string message =
                        "La card è cambiata altrove. Annulla per ricaricare la versione corrente.";
                    card.MarkSaveError(message);
                    StatusMessage = message;
                    return;
                }

                if (!isSavingContent)
                {
                    card.CompleteWithoutChanges();
                    await _editLockRepository.ReleaseAsync(
                        card.Model.Id,
                        _applicationSession.SessionId,
                        cancellationToken);
                    await MergeCardsAsync(cancellationToken);
                    _logger.Information(
                        "card.edit_closed",
                        "Modifica chiusa senza variazioni.",
                        new Dictionary<string, object?>
                        {
                            ["cardId"] = card.Model.Id,
                        });
                    MarkConnectionHealthy();
                    StatusMessage = null;
                    return;
                }

                var editedCard = card.CreateEditedModel(_settings.UserName);
                var persistedCard = await _cardRepository.UpdateAsync(
                    editedCard,
                    _applicationSession.SessionId,
                    cancellationToken);

                card.CompleteSave(persistedCard);
                contentPersisted = true;
                await _editLockRepository.ReleaseAsync(
                    card.Model.Id,
                    _applicationSession.SessionId,
                    cancellationToken);
                await MergeCardsAsync(cancellationToken);
                _logger.Information(
                    "card.saved",
                    "Card salvata.",
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = persistedCard.Id,
                        ["version"] = persistedCard.Version,
                    });

                MarkConnectionHealthy();
                StatusMessage = null;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chiusura normale dell'applicazione.
        }
        catch (CardConcurrencyException ex)
        {
            card.MarkConcurrencyConflict();
            const string message = CardViewModel.ConcurrencyConflictMessage;
            _logger.Warning(
                "card.concurrency_conflict",
                "Conflitto di concorrenza durante il salvataggio.",
                properties: new Dictionary<string, object?>
                {
                    ["cardId"] = card.Model.Id,
                    ["actualVersion"] = ex.ActualVersion,
                });
            MarkConnectionHealthy();
            StatusMessage = message;
        }
        catch (CardEditLockException ex)
        {
            var activeLocks = await TryGetActiveLocksAsync();
            activeLocks.TryGetValue(card.Model.Id, out var currentLock);
            card.MarkLockLost(currentLock, _applicationSession.SessionId);
            card.MarkSaveError(ex.Message);
            _logger.Warning(
                "card.lock_lost",
                "Il lock della card non è più valido.",
                ex,
                new Dictionary<string, object?>
                {
                    ["cardId"] = card.Model.Id,
                });
            MarkConnectionHealthy();
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            if (contentPersisted)
            {
                card.MarkSaveError(
                    "Card salvata, ma non è stato possibile aggiornare completamente lo stato della board.");
            }
            else if (card.IsEditing)
            {
                card.MarkSaveError("Salvataggio non riuscito. La bozza è conservata.");
            }

            SetOperationFailure(ex);
        }
        finally
        {
            ClearActivity();
        }
    }

    [RelayCommand]
    private async Task CancelEditAsync(CardViewModel? card)
    {
        if (_isDisposed || card is null || !card.IsEditing)
        {
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        SetActivity("Annullamento modifica...");
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                card.CancelEdit();
                await _editLockRepository.ReleaseAsync(
                    card.Model.Id,
                    _applicationSession.SessionId,
                    cancellationToken);
                await MergeCardsAsync(cancellationToken);
                _logger.Information(
                    "card.edit_cancelled",
                    "Modifica annullata e lock rilasciato.",
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = card.Model.Id,
                    });
                MarkConnectionHealthy();
                StatusMessage = null;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chiusura normale dell'applicazione.
        }
        catch (Exception ex)
        {
            SetOperationFailure(ex);
        }
        finally
        {
            ClearActivity();
        }
    }

    private async Task PollBoardAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // M3.12: un solo ticker condiviso aggiorna tutte le etichette temporali,
        // anche quando il polling del database è temporaneamente sospeso.
        UpdateCardTimeIndicators();
        if (ConnectionState == BoardConnectionState.Error && !_automaticRetryAllowed)
        {
            return;
        }

        await RefreshBoardSafelyAsync(
            isInitialLoad: false,
            waitForGate: false,
            showActivity: false);
    }

    private async Task HeartbeatEditingLocksAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed ||
            cancellationToken.IsCancellationRequested ||
            (ConnectionState == BoardConnectionState.Error && !_automaticRetryAllowed))
        {
            return;
        }

        await RenewEditingLocksSafelyAsync();
    }

    [RelayCommand]
    private async Task RetryNowAsync()
    {
        if (_isDisposed || ConnectionState == BoardConnectionState.ShuttingDown)
        {
            return;
        }

        _logger.Information("connection.retry_requested", "Nuovo tentativo di connessione richiesto manualmente.");
        StatusMessage = null;
        ConnectionState = BoardConnectionState.Connecting;
        await RefreshBoardSafelyAsync(
            isInitialLoad: false,
            waitForGate: true,
            showActivity: true);
    }

    private async Task RefreshBoardSafelyAsync(
        bool isInitialLoad,
        bool waitForGate,
        bool showActivity)
    {
        var cancellationToken = _lifetimeCancellation.Token;
        var gateAcquired = false;

        try
        {
            if (waitForGate)
            {
                await _operationGate.WaitAsync(cancellationToken);
                gateAcquired = true;
            }
            else
            {
                gateAcquired = await _operationGate.WaitAsync(0, cancellationToken);
                if (!gateAcquired)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (showActivity)
        {
            SetActivity(isInitialLoad ? "Caricamento board..." : "Nuovo tentativo di connessione...");
        }

        try
        {
            if (!_isInitialized)
            {
                await InitializeAndLoadAsync(cancellationToken);
            }
            else if (await _changeDetector.HasChangedSinceLastCheckAsync(cancellationToken))
            {
                await MergeCardsAsync(cancellationToken);
                _logger.Information(
                    "board.external_changes_merged",
                    "Le modifiche esterne sono state integrate nella board.",
                    new Dictionary<string, object?>
                    {
                        ["columnCount"] = Columns.Count,
                        ["cardCount"] = Columns.Sum(column => column.Cards.Count),
                    });
            }
            else
            {
                // La scadenza naturale di un lease non produce una scrittura SQLite e quindi
                // non incrementa BoardState.Revision. I lock attivi vengono riletti comunque.
                await RefreshLockStatesAsync(cancellationToken);
            }

            MarkConnectionHealthy();
            if (!HasUnresolvedCardIssues())
            {
                StatusMessage = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chiusura normale dell'applicazione.
        }
        catch (Exception ex)
        {
            _isInitialized = false;
            RegisterDatabaseFailure(ex, isInitialLoad);
        }
        finally
        {
            if (showActivity)
            {
                ClearActivity();
            }

            if (gateAcquired)
            {
                _operationGate.Release();
            }
        }
    }

    private async Task RenewEditingLocksSafelyAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            if (!await _operationGate.WaitAsync(0, cancellationToken))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var editingCards = Columns
                .SelectMany(column => column.Cards)
                .Where(card => card.IsEditing)
                .ToList();

            if (editingCards.Count == 0)
            {
                return;
            }

            var lostLock = false;
            foreach (var card in editingCards)
            {
                if (!await _editLockRepository.RenewAsync(
                        card.Model.Id,
                        _applicationSession.SessionId,
                        EditLockLeaseDuration,
                        cancellationToken))
                {
                    lostLock = true;
                }
            }

            if (lostLock)
            {
                var activeLocks = await GetActiveLocksByCardIdAsync(cancellationToken);
                foreach (var card in editingCards)
                {
                    activeLocks.TryGetValue(card.Model.Id, out var currentLock);
                    if (currentLock is null ||
                        !string.Equals(currentLock.SessionId, _applicationSession.SessionId, StringComparison.Ordinal))
                    {
                        card.MarkLockLost(currentLock, _applicationSession.SessionId);
                    }
                }

                _logger.Warning(
                    "card.lock_heartbeat_lost",
                    "Uno o più lock non sono stati rinnovati.",
                    properties: new Dictionary<string, object?>
                    {
                        ["editingCardCount"] = editingCards.Count,
                    });
                MarkConnectionHealthy();
                StatusMessage = "Uno o più lock di modifica sono scaduti. Le bozze sono state conservate.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chiusura normale dell'applicazione.
        }
        catch (Exception ex)
        {
            SetOperationFailure(ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task InitializeAndLoadAsync(CancellationToken cancellationToken)
    {
        await Task.Run(
            () => _databaseInitializer.EnsureInitialized(allowCreate: !_hasLoadedSuccessfully),
            cancellationToken);
        _logger.Information(
            "database.initialized",
            "Database inizializzato e schema verificato.",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = DatabaseInitializer.ExpectedSchemaVersion,
                ["allowCreate"] = !_hasLoadedSuccessfully,
            });

        if (Columns.Count == 0)
        {
            // La UI viene sostituita soltanto dopo che tutte le letture sono riuscite.
            // Un errore intermedio non deve mostrare una board vuota o parziale.
            var snapshot = await ReadInitialSnapshotAsync(cancellationToken);
            ApplyInitialSnapshot(snapshot);
        }
        else
        {
            // Durante un recupero preservare i ViewModel esistenti e le eventuali bozze.
            await MergeCardsAsync(cancellationToken);
        }

        _isInitialized = true;
        _hasLoadedSuccessfully = true;
    }

    private Task<KanbanBoardSnapshot> ReadInitialSnapshotAsync(CancellationToken cancellationToken) =>
        _snapshotRepository.GetAsync(cancellationToken);

    private void ApplyInitialSnapshot(KanbanBoardSnapshot snapshot)
    {
        ApplyCatalogSnapshot(snapshot);

        var columnViewModels = snapshot.Columns
            .OrderBy(column => column.SortOrder)
            .Select(column => new ColumnViewModel(column))
            .ToList();
        var columnsById = columnViewModels.ToDictionary(column => column.Id);
        var locksByCardId = snapshot.ActiveLocks.ToDictionary(editLock => editLock.CardId);

        foreach (var card in snapshot.Cards.OrderBy(card => card.SortOrder).ThenBy(card => card.Id))
        {
            if (!columnsById.TryGetValue(card.ColumnId, out var column))
            {
                throw new InvalidOperationException(
                    $"La card {card.Id} fa riferimento alla colonna inesistente {card.ColumnId}.");
            }

            var cardViewModel = new CardViewModel(card, Priorities, DeadlineRules, _clock.Now);
            locksByCardId.TryGetValue(card.Id, out var editLock);
            cardViewModel.ApplyLockState(editLock, _applicationSession.SessionId);
            column.Cards.Add(cardViewModel);
        }

        Columns.Clear();
        foreach (var column in columnViewModels)
        {
            Columns.Add(column);
        }

        RaiseHeaderColumnsChanged();
        RebuildSwimlanes();
    }

    /// <summary>
    /// Aggiorna le collection riutilizzando i ViewModel esistenti. Le card in editing non vengono
    /// spostate, rimosse o sovrascritte: eventuali differenze vengono esposte come conflitto.
    /// </summary>
    private async Task MergeCardsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotRepository.GetAsync(cancellationToken);
        ApplyCatalogSnapshot(snapshot);
        MergeColumns(snapshot.Columns);

        var cards = snapshot.Cards;
        var activeLocks = snapshot.ActiveLocks.ToDictionary(editLock => editLock.CardId);
        var cardsById = cards.ToDictionary(card => card.Id);
        var existingByCardId = Columns
            .SelectMany(column => column.Cards)
            .ToDictionary(card => card.Model.Id);

        foreach (var persistedCard in cards)
        {
            var targetColumn = Columns.Single(column => column.Id == persistedCard.ColumnId);

            if (!existingByCardId.TryGetValue(persistedCard.Id, out var cardViewModel))
            {
                cardViewModel = new CardViewModel(persistedCard, Priorities, DeadlineRules, _clock.Now);
                targetColumn.Cards.Add(cardViewModel);
                existingByCardId.Add(persistedCard.Id, cardViewModel);
                continue;
            }

            if (!cardViewModel.IsEditing)
            {
                var currentColumn = Columns.First(column => column.Cards.Contains(cardViewModel));
                if (currentColumn.Id != targetColumn.Id)
                {
                    currentColumn.Cards.Remove(cardViewModel);
                    targetColumn.Cards.Add(cardViewModel);
                }
            }

            cardViewModel.RefreshFromModel(persistedCard);
        }

        foreach (var cardViewModel in existingByCardId.Values.ToList())
        {
            if (cardsById.ContainsKey(cardViewModel.Model.Id))
            {
                continue;
            }

            if (cardViewModel.IsEditing)
            {
                cardViewModel.MarkDeletedExternally();
                continue;
            }

            var currentColumn = Columns.First(column => column.Cards.Contains(cardViewModel));
            currentColumn.Cards.Remove(cardViewModel);
        }

        foreach (var column in Columns)
        {
            ReorderCollection(column);
        }

        foreach (var cardViewModel in Columns.SelectMany(column => column.Cards))
        {
            activeLocks.TryGetValue(cardViewModel.Model.Id, out var editLock);
            cardViewModel.ApplyLockState(editLock, _applicationSession.SessionId);
        }

        RefreshCardPresentationCatalogs();

        // Il polling può rilevare la revisione generata dall'acquisizione del lock.
        // Ricreare in quel momento tutte le swimlane distruggerebbe i controlli attivi
        // (focus e popup della priorità). La proiezione viene riallineata appena termina
        // l'ultima bozza, perché Commit e Cancel richiamano nuovamente MergeCardsAsync.
        if (!HasActiveEdits)
        {
            RebuildSwimlanes();
        }
    }

    private void RebuildSwimlanes()
    {
        var generic = CardTypes.SingleOrDefault(cardType =>
            string.Equals(
                cardType.SystemKey,
                SystemCardTypeKeys.Generic,
                StringComparison.Ordinal));
        if (generic is null)
        {
            throw new InvalidOperationException(
                "La board non contiene la fascia di sistema Generica.");
        }

        var cards = Columns.SelectMany(column => column.Cards).ToList();
        var assignedCardTypeIds = cards
            .Select(card => card.Model.CardTypeId ?? generic.Id)
            .ToHashSet();
        var cardTypesById = CardTypes.ToDictionary(cardType => cardType.Id);

        foreach (var assignedCardTypeId in assignedCardTypeIds)
        {
            if (!cardTypesById.ContainsKey(assignedCardTypeId))
            {
                throw new InvalidOperationException(
                    $"Una card fa riferimento alla fascia inesistente {assignedCardTypeId}.");
            }
        }

        var lanes = CardTypes
            .Where(cardType =>
                cardType.Id == generic.Id ||
                cardType.IsActive ||
                assignedCardTypeIds.Contains(cardType.Id))
            .OrderBy(cardType => cardType.Id == generic.Id ? 0 : 1)
            .ThenBy(cardType => cardType.SortOrder)
            .ThenBy(cardType => cardType.Id)
            .Select(cardType => new SwimlaneViewModel(cardType, Columns))
            .ToList();
        var lanesByCardTypeId = lanes.ToDictionary(lane => lane.Id);

        foreach (var card in cards
                     .OrderBy(card => card.Model.SortOrder)
                     .ThenBy(card => card.Model.Id))
        {
            var cardTypeId = card.Model.CardTypeId ?? generic.Id;
            lanesByCardTypeId[cardTypeId]
                .GetCell(card.Model.ColumnId)
                .Cards.Add(card);
        }

        Swimlanes.Clear();
        foreach (var lane in lanes)
        {
            Swimlanes.Add(lane);
        }

        OnPropertyChanged(nameof(HasSwimlanes));
    }

    private void ApplyCatalogSnapshot(KanbanBoardSnapshot snapshot)
    {
        BoardRevision = snapshot.Revision;
        Priorities = snapshot.Priorities;
        CardTypes = snapshot.CardTypes;
        DeadlineRules = snapshot.DeadlineRules;

        OnPropertyChanged(nameof(BoardRevision));
        OnPropertyChanged(nameof(Priorities));
        OnPropertyChanged(nameof(CardTypes));
        OnPropertyChanged(nameof(DeadlineRules));
    }

    private void MergeColumns(IReadOnlyList<Column> persistedColumns)
    {
        var persistedById = persistedColumns.ToDictionary(column => column.Id);
        var existingById = Columns.ToDictionary(column => column.Id);

        foreach (var persistedColumn in persistedColumns)
        {
            if (existingById.TryGetValue(persistedColumn.Id, out var existing))
            {
                existing.RefreshFromModel(persistedColumn);
            }
            else
            {
                Columns.Add(new ColumnViewModel(persistedColumn));
            }
        }

        foreach (var obsolete in Columns
                     .Where(column => !persistedById.ContainsKey(column.Id))
                     .ToList())
        {
            if (obsolete.Cards.Any(card => card.IsEditing))
            {
                throw new InvalidOperationException(
                    $"La colonna {obsolete.Name} è stata rimossa mentre contiene una card in modifica.");
            }

            Columns.Remove(obsolete);
        }

        var expectedOrder = Columns
            .OrderBy(column => column.Model.SortOrder)
            .ThenBy(column => column.Id)
            .ToList();
        for (var targetIndex = 0; targetIndex < expectedOrder.Count; targetIndex++)
        {
            var currentIndex = Columns.IndexOf(expectedOrder[targetIndex]);
            if (currentIndex != targetIndex)
            {
                Columns.Move(currentIndex, targetIndex);
            }
        }

        RaiseHeaderColumnsChanged();
    }

    private async Task RefreshLockStatesAsync(CancellationToken cancellationToken)
    {
        var activeLocks = await GetActiveLocksByCardIdAsync(cancellationToken);
        foreach (var cardViewModel in Columns.SelectMany(column => column.Cards))
        {
            activeLocks.TryGetValue(cardViewModel.Model.Id, out var editLock);
            cardViewModel.ApplyLockState(editLock, _applicationSession.SessionId);
        }
    }

    private async Task<Dictionary<long, CardEditLock>> GetActiveLocksByCardIdAsync(
        CancellationToken cancellationToken)
    {
        var activeLocks = await _editLockRepository.GetActiveAsync(cancellationToken);
        return activeLocks.ToDictionary(editLock => editLock.CardId);
    }

    private async Task<Dictionary<long, CardEditLock>> TryGetActiveLocksAsync()
    {
        try
        {
            return await GetActiveLocksByCardIdAsync(_lifetimeCancellation.Token);
        }
        catch
        {
            return new Dictionary<long, CardEditLock>();
        }
    }

    private ColumnViewModel? GetColumnBySystemKey(string systemKey) =>
        Columns.FirstOrDefault(column =>
            string.Equals(column.SystemKey, systemKey, StringComparison.Ordinal));

    private void RaiseHeaderColumnsChanged()
    {
        OnPropertyChanged(nameof(BacklogColumn));
        OnPropertyChanged(nameof(TodoColumn));
        OnPropertyChanged(nameof(InProgressColumn));
        OnPropertyChanged(nameof(TestingColumn));
        OnPropertyChanged(nameof(DoneColumn));
    }

    private void UpdateCardTimeIndicators()
    {
        var now = _clock.Now;
        foreach (var card in Columns.SelectMany(column => column.Cards))
        {
            card.UpdateDisplayNow(now);
        }
    }

    private void RefreshCardPresentationCatalogs()
    {
        foreach (var card in Columns.SelectMany(column => column.Cards))
        {
            card.UpdatePriorityCatalog(Priorities, DeadlineRules);
            card.UpdateDisplayNow(_clock.Now);
        }
    }

    private static void ReorderCollection(ColumnViewModel column)
    {
        // Una card in editing deve restare fisicamente ferma anche se un'altra istanza
        // modifica l'ordine circostante. Il riordino completo verrà applicato al termine
        // del salvataggio o dell'annullamento.
        if (column.Cards.Any(card => card.IsEditing))
        {
            return;
        }

        var expectedOrder = column.Cards
            .OrderBy(card => card.Model.SortOrder)
            .ThenBy(card => card.Model.Id)
            .ToList();

        for (var targetIndex = 0; targetIndex < expectedOrder.Count; targetIndex++)
        {
            var currentIndex = column.Cards.IndexOf(expectedOrder[targetIndex]);
            if (currentIndex != targetIndex)
            {
                column.Cards.Move(currentIndex, targetIndex);
            }
        }
    }

    [RelayCommand]
    private Task AddCardAsync(ColumnViewModel? column) => ExecuteWriteAsync(
        async cancellationToken =>
        {
            var systemKey = column?.SystemKey;
            if (column is null ||
                string.IsNullOrWhiteSpace(systemKey) ||
                !WorkflowColumnKeys.Ordered.Contains(systemKey, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "La colonna scelta non è uno stato operativo valido.");
            }

            var generic = CardTypes.SingleOrDefault(cardType =>
                string.Equals(
                    cardType.SystemKey,
                    SystemCardTypeKeys.Generic,
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    "La board non contiene la fascia di sistema Generica.");
            if (!generic.IsActive)
            {
                throw new InvalidOperationException(
                    "La fascia Generica è inattiva e non può ricevere nuove card.");
            }

            var genericLane = Swimlanes.Single(lane => lane.Id == generic.Id);
            var targetCell = genericLane.GetCell(column.Id);
            var defaultPriorityId = Priorities.SingleOrDefault(priority =>
                priority.IsActive && priority.IsDefault)?.Id;
            var newCard = new Card
            {
                ColumnId = column.Id,
                CardTypeId = generic.Id,
                PriorityId = defaultPriorityId,
                Title = "Nuova card",
                SortOrder = targetCell.Cards.Count,
                CreatedBy = _settings.UserName,
                UpdatedBy = _settings.UserName,
            };

            var createdCard = await _cardRepository.CreateAsync(newCard, cancellationToken);
            await MergeCardsAsync(cancellationToken);
            MarkCardPersistenceSuccess(createdCard.Id, "Card inserita");
            _logger.Information(
                "card.created",
                "Card creata nella fascia Generica.",
                new Dictionary<string, object?>
                {
                    ["cardId"] = createdCard.Id,
                    ["columnId"] = createdCard.ColumnId,
                    ["cardTypeId"] = createdCard.CardTypeId,
                    ["workflowState"] = systemKey,
                });
        },
        "Creazione card...");

    [RelayCommand]
    private void RequestDeleteCard(CardViewModel? card)
    {
        if (_isDisposed || card is null || !card.CanRequestDelete)
        {
            return;
        }

        foreach (var otherCard in Columns
                     .SelectMany(column => column.Cards)
                     .Where(candidate => !ReferenceEquals(candidate, card)))
        {
            otherCard.CancelDeleteConfirmation();
        }

        card.RequestDeleteConfirmation();
    }

    [RelayCommand]
    private void CancelDeleteCard(CardViewModel? card)
    {
        card?.CancelDeleteConfirmation();
    }

    [RelayCommand]
    private Task DeleteCardAsync(CardViewModel? card) => ExecuteWriteAsync(
        async cancellationToken =>
        {
            if (card is null || !card.IsDeleteConfirmationVisible)
            {
                return;
            }

            if (!card.CanRequestDelete)
            {
                throw new CardEditLockException(
                    card.Model.Id,
                    card.IsEditing
                        ? "Termina la modifica prima di eliminare la card."
                        : $"La card è in modifica da {card.EditingUserName ?? "un altro utente"}.",
                    card.EditingUserName);
            }

            var deletedCardId = card.Model.Id;
            var deletedColumnId = card.ColumnId;
            await _cardRepository.DeleteAsync(deletedCardId, _settings.UserName, cancellationToken);
            await MergeCardsAsync(cancellationToken);
            _logger.Information(
                "card.deleted",
                "Card eliminata.",
                new Dictionary<string, object?>
                {
                    ["cardId"] = deletedCardId,
                    ["columnId"] = deletedColumnId,
                });
        },
        "Eliminazione card...");

    public Task MoveCardAsync(
        CardViewModel card,
        SwimlaneCellViewModel targetCell,
        int targetCellIndex) =>
        ExecuteWriteAsync(
            async cancellationToken =>
            {
                ArgumentNullException.ThrowIfNull(card);
                ArgumentNullException.ThrowIfNull(targetCell);
                ArgumentOutOfRangeException.ThrowIfNegative(targetCellIndex);

                if (!card.CanDrag)
                {
                    throw new CardEditLockException(
                        card.Model.Id,
                        card.IsEditing
                            ? "Termina la modifica prima di spostare la card."
                            : $"La card è in modifica da {card.EditingUserName ?? "un altro utente"}.",
                        card.EditingUserName);
                }

                var sourceCell = Swimlanes
                    .SelectMany(lane => lane.Cells)
                    .SingleOrDefault(cell => cell.Cards.Contains(card));
                if (sourceCell is null)
                {
                    throw new InvalidOperationException(
                        "La card trascinata non appartiene più alla board corrente.");
                }

                if (!Swimlanes.SelectMany(lane => lane.Cells).Contains(targetCell))
                {
                    throw new InvalidOperationException(
                        "La cella di destinazione non appartiene più alla board corrente.");
                }

                if (!targetCell.IsCardTypeActive &&
                    targetCell.CardTypeId != sourceCell.CardTypeId)
                {
                    throw new InvalidOperationException(
                        $"La fascia {targetCell.CardTypeName} è inattiva e non può ricevere nuove card.");
                }

                var sourceColumnId = sourceCell.ColumnId;
                var sourceCardTypeId = sourceCell.CardTypeId;
                var cardId = card.Model.Id;

                await _cardRepository.MoveToCellAsync(
                    cardId,
                    targetCell.ColumnId,
                    targetCell.CardTypeId,
                    targetCellIndex,
                    _settings.UserName,
                    cancellationToken);

                await MergeCardsAsync(cancellationToken);
                var sameColumn = sourceColumnId == targetCell.ColumnId;
                var sameCardType = sourceCardTypeId == targetCell.CardTypeId;
                var statusText = sameColumn && sameCardType
                    ? "Ordine aggiornato"
                    : sameColumn
                        ? "Fascia aggiornata"
                        : "Card spostata";
                MarkCardPersistenceSuccess(cardId, statusText);
                _logger.Information(
                    sameColumn && sameCardType ? "card.reordered" : "card.moved",
                    sameColumn && sameCardType
                        ? "Ordine della card aggiornato nella cella."
                        : "Posizione bidimensionale della card aggiornata.",
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = cardId,
                        ["sourceColumnId"] = sourceColumnId,
                        ["targetColumnId"] = targetCell.ColumnId,
                        ["sourceCardTypeId"] = sourceCardTypeId,
                        ["targetCardTypeId"] = targetCell.CardTypeId,
                        ["targetCellIndex"] = targetCellIndex,
                    });
            },
            "Spostamento card...");

    private void MarkCardPersistenceSuccess(long cardId, string statusText)
    {
        var card = Columns
            .SelectMany(column => column.Cards)
            .SingleOrDefault(candidate => candidate.Model.Id == cardId);
        card?.MarkPersistenceSuccess(statusText);
    }

    private async Task ExecuteWriteAsync(
        Func<CancellationToken, Task> operation,
        string activityText)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!IsOnline)
        {
            StatusMessage = "Operazione non disponibile: il database non è online.";
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        SetActivity(activityText);
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
                MarkConnectionHealthy();
                StatusMessage = null;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chiusura normale dell'applicazione.
        }
        catch (Exception ex)
        {
            SetOperationFailure(ex);
        }
        finally
        {
            ClearActivity();
        }
    }

    private void SetOperationFailure(Exception exception)
    {
        if (exception is CardEditLockException or CardConcurrencyException or KeyNotFoundException or CardValidationException)
        {
            _logger.Warning(
                "operation.rejected",
                "Operazione rifiutata da una regola applicativa.",
                exception);
            MarkConnectionHealthy();
            StatusMessage = exception.Message;
            return;
        }

        _isInitialized = false;
        RegisterDatabaseFailure(exception, isInitialLoad: false);
    }

    private void RegisterDatabaseFailure(Exception exception, bool isInitialLoad)
    {
        var failure = DatabaseFailureClassifier.Classify(exception);
        _automaticRetryAllowed = failure.CanRetryAutomatically;
        _consecutiveFailures++;

        if (failure.RequiresAttention)
        {
            ConnectionState = BoardConnectionState.Error;
        }
        else if (failure.Kind == DatabaseFailureKind.Contention &&
                 _consecutiveFailures < ConsecutiveFailuresBeforeOffline)
        {
            ConnectionState = BoardConnectionState.Recovering;
        }
        else
        {
            ConnectionState = isInitialLoad ||
                              _consecutiveFailures >= ConsecutiveFailuresBeforeOffline
                ? BoardConnectionState.Offline
                : BoardConnectionState.Recovering;
        }

        var retryText = failure.CanRetryAutomatically
            ? " Verrà eseguito un nuovo tentativo automatico."
            : " È necessario correggere il problema prima di riprovare.";
        var pathText = failure.Kind == DatabaseFailureKind.Unavailable
            ? $" Percorso: '{_settings.DatabasePath}'."
            : string.Empty;

        var errorReference = _errorReferences.Create();
        _logger.Error(
            "database.failure",
            "Operazione SQLite non riuscita.",
            exception,
            errorReference,
            new Dictionary<string, object?>
            {
                ["failureKind"] = failure.Kind,
                ["connectionState"] = ConnectionState,
                ["consecutiveFailures"] = _consecutiveFailures,
                ["automaticRetry"] = failure.CanRetryAutomatically,
                ["databasePath"] = _settings.DatabasePath,
            });

        StatusMessage = failure.UserMessage + pathText + retryText +
                        $" Riferimento: {errorReference}.";
    }

    private void MarkConnectionHealthy()
    {
        _consecutiveFailures = 0;
        _automaticRetryAllowed = true;
        ConnectionState = BoardConnectionState.Online;
        _lastSuccessfulSyncAt = _clock.Now;
        OnPropertyChanged(nameof(LastSuccessfulSyncText));
        OnPropertyChanged(nameof(HasLastSuccessfulSync));
    }

    private bool HasUnresolvedCardIssues() => Columns
        .SelectMany(column => column.Cards)
        .Any(card => card.HasExternalChanges || card.HasLostEditLock || card.IsDeletedExternally);

    private void SetActivity(string activityText)
    {
        ActivityText = activityText;
        IsBusy = true;
    }

    private void ClearActivity()
    {
        IsBusy = false;
        ActivityText = null;
    }

    private void RaiseOperationalStateChanged()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsRecovering));
        OnPropertyChanged(nameof(IsConnectionPending));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(HasConnectionError));
        OnPropertyChanged(nameof(HasWarningStatusMessage));
        OnPropertyChanged(nameof(HasErrorStatusMessage));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanRetryNow));
        OnPropertyChanged(nameof(CanModifyBoard));
        OnPropertyChanged(nameof(ConnectionStatusText));
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _logger.Information(
            "board.shutdown_started",
            "Avvio della chiusura coordinata della board.",
            new Dictionary<string, object?>
            {
                ["activeEdits"] = HasActiveEdits,
                ["sessionId"] = ShortSessionId(),
            });
        ConnectionState = BoardConnectionState.ShuttingDown;
        SetActivity("Rilascio dei lock e chiusura...");
        _lifetimeCancellation.Cancel();

        var operationGateAcquired = false;
        try
        {
            // Nessun polling o heartbeat può sopravvivere alla finestra.
            await Task.WhenAll(
                _pollingScheduler.StopAsync(),
                _lockHeartbeatScheduler.StopAsync());

            // Attendere l'operazione eventualmente in corso prima di rilasciare i lease.
            await _operationGate.WaitAsync(CancellationToken.None);
            operationGateAcquired = true;

            if (_isStarted)
            {
                try
                {
                    await _editLockRepository.ReleaseSessionAsync(
                        _applicationSession.SessionId,
                        CancellationToken.None);
                    _logger.Information(
                        "card.session_locks_released",
                        "Tutti i lock della sessione sono stati rilasciati.",
                        new Dictionary<string, object?>
                        {
                            ["sessionId"] = ShortSessionId(),
                        });
                }
                catch
                {
                    // Cleanup best effort: in caso di errore il lease scadrà automaticamente.
                }
            }
        }
        finally
        {
            if (operationGateAcquired)
            {
                _operationGate.Release();
            }

            try
            {
                await _pollingScheduler.DisposeAsync();
            }
            finally
            {
                try
                {
                    await _lockHeartbeatScheduler.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        if (_databaseInstanceLease is not null)
                        {
                            await _databaseInstanceLease.DisposeAsync();
                        }
                    }
                    finally
                    {
                        _operationGate.Dispose();
                        _lifetimeCancellation.Dispose();
                        _logger.Information("board.shutdown_completed", "Chiusura della board completata.");
                    }
                }
            }
        }
    }

    private string ShortSessionId() => _applicationSession.SessionId.Length <= 8
        ? _applicationSession.SessionId
        : _applicationSession.SessionId[..8];

}
