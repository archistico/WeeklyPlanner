using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class BoardViewModel : ViewModelBase, IAsyncDisposable
{
    private const int ConsecutiveFailuresBeforeOffline = 3;
    private static readonly TimeSpan EditLockLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EditLockHeartbeatInterval = TimeSpan.FromSeconds(10);

    private readonly AppSettings _settings;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly ICardRepository _cardRepository;
    private readonly ICardEditLockRepository _editLockRepository;
    private readonly IColumnRepository _columnRepository;
    private readonly IBoardChangeDetector _changeDetector;
    private readonly DispatcherTimer _pollingTimer;
    private readonly DispatcherTimer _lockHeartbeatTimer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _machineName = Environment.MachineName;

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

    public BoardConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (SetProperty(ref _connectionState, value))
            {
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

    public BoardViewModel(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _settings.Normalize();

        var connectionFactory = new SqliteConnectionFactory(_settings.DatabasePath);
        _databaseInitializer = new DatabaseInitializer(connectionFactory);

        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var readPipeline = RetryPolicyFactory.CreateSqliteReadPipeline();
        _cardRepository = new CardRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline);
        _editLockRepository = new CardEditLockRepository(
            connectionFactory,
            writePipeline,
            readPipeline: readPipeline);
        _columnRepository = new ColumnRepository(connectionFactory, readPipeline);
        _changeDetector = new BoardChangeDetector(
            new BoardRevisionRepository(connectionFactory, readPipeline));

        _pollingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds),
        };
        _pollingTimer.Tick += OnPollingTimerTick;

        _lockHeartbeatTimer = new DispatcherTimer
        {
            Interval = EditLockHeartbeatInterval,
        };
        _lockHeartbeatTimer.Tick += OnLockHeartbeatTimerTick;
    }

    public async Task StartAsync()
    {
        if (_isDisposed || _isStarted)
        {
            return;
        }

        _isStarted = true;
        StatusMessage = null;
        ConnectionState = BoardConnectionState.Connecting;
        await RefreshBoardSafelyAsync(
            isInitialLoad: true,
            waitForGate: true,
            showActivity: true);

        if (!_isDisposed)
        {
            _pollingTimer.Start();
            _lockHeartbeatTimer.Start();
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
                    _sessionId,
                    _settings.UserName,
                    _machineName,
                    EditLockLeaseDuration,
                    cancellationToken);

                card.ApplyLockState(result.CurrentLock, _sessionId);
                if (!result.Acquired)
                {
                    card.RefreshFromModel(card.Model);
                    MarkConnectionHealthy();
                    StatusMessage = $"{result.CurrentLock.UserName} sta già modificando questa card.";
                    return false;
                }

                card.BeginEdit(result.CurrentLock, _sessionId);
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
            StatusMessage = "Salvataggio non disponibile: il database non è online. La bozza resta conservata.";
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        SetActivity("Salvataggio card...");
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                if (card.IsDeletedExternally)
                {
                    StatusMessage = "La card è stata eliminata altrove. Annulla per chiudere la bozza.";
                    return;
                }

                if (card.HasLostEditLock)
                {
                    StatusMessage = "Il lock di modifica non è più valido. La bozza è conservata.";
                    return;
                }

                if (card.HasExternalChanges)
                {
                    StatusMessage = "La card è cambiata altrove. Annulla per ricaricare la versione corrente.";
                    return;
                }

                if (!card.IsDirty)
                {
                    card.CompleteWithoutChanges();
                    await _editLockRepository.ReleaseAsync(
                        card.Model.Id,
                        _sessionId,
                        cancellationToken);
                    await MergeCardsAsync(cancellationToken);
                    MarkConnectionHealthy();
                    StatusMessage = null;
                    return;
                }

                var editedCard = card.CreateEditedModel(_settings.UserName);
                var persistedCard = await _cardRepository.UpdateAsync(
                    editedCard,
                    _sessionId,
                    cancellationToken);

                card.CompleteSave(persistedCard);
                await _editLockRepository.ReleaseAsync(
                    card.Model.Id,
                    _sessionId,
                    cancellationToken);
                await MergeCardsAsync(cancellationToken);

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
            card.RefreshFromModel(new Card
            {
                Id = card.Model.Id,
                ColumnId = card.Model.ColumnId,
                Title = card.Model.Title,
                Notes = card.Model.Notes,
                SortOrder = card.Model.SortOrder,
                CreatedBy = card.Model.CreatedBy,
                UpdatedBy = card.Model.UpdatedBy,
                UpdatedAtUtc = card.Model.UpdatedAtUtc,
                Version = ex.ActualVersion,
            });
            MarkConnectionHealthy();
            StatusMessage = "Conflitto rilevato: la bozza è conservata e non è stata sovrascritta.";
        }
        catch (CardEditLockException ex)
        {
            var activeLocks = await TryGetActiveLocksAsync();
            activeLocks.TryGetValue(card.Model.Id, out var currentLock);
            card.MarkLockLost(currentLock, _sessionId);
            MarkConnectionHealthy();
            StatusMessage = ex.Message;
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
                    _sessionId,
                    cancellationToken);
                await MergeCardsAsync(cancellationToken);
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

    private async void OnPollingTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed ||
            (ConnectionState == BoardConnectionState.Error && !_automaticRetryAllowed))
        {
            return;
        }

        await RefreshBoardSafelyAsync(
            isInitialLoad: false,
            waitForGate: false,
            showActivity: false);
    }

    private async void OnLockHeartbeatTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed ||
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
                        _sessionId,
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
                        !string.Equals(currentLock.SessionId, _sessionId, StringComparison.Ordinal))
                    {
                        card.MarkLockLost(currentLock, _sessionId);
                    }
                }

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

    private async Task<BoardSnapshot> ReadInitialSnapshotAsync(CancellationToken cancellationToken)
    {
        var columns = await _columnRepository.GetAllAsync(cancellationToken);
        var cards = await _cardRepository.GetAllAsync(cancellationToken);
        var activeLocks = await _editLockRepository.GetActiveAsync(cancellationToken);
        return new BoardSnapshot(columns, cards, activeLocks);
    }

    private void ApplyInitialSnapshot(BoardSnapshot snapshot)
    {
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

            var cardViewModel = new CardViewModel(card);
            locksByCardId.TryGetValue(card.Id, out var editLock);
            cardViewModel.ApplyLockState(editLock, _sessionId);
            column.Cards.Add(cardViewModel);
        }

        Columns.Clear();
        foreach (var column in columnViewModels)
        {
            Columns.Add(column);
        }
    }

    /// <summary>
    /// Aggiorna le collection riutilizzando i ViewModel esistenti. Le card in editing non vengono
    /// spostate, rimosse o sovrascritte: eventuali differenze vengono esposte come conflitto.
    /// </summary>
    private async Task MergeCardsAsync(CancellationToken cancellationToken)
    {
        var cards = await _cardRepository.GetAllAsync(cancellationToken);
        var activeLocks = await GetActiveLocksByCardIdAsync(cancellationToken);
        var cardsById = cards.ToDictionary(card => card.Id);
        var existingByCardId = Columns
            .SelectMany(column => column.Cards)
            .ToDictionary(card => card.Model.Id);

        foreach (var persistedCard in cards)
        {
            var targetColumn = Columns.Single(column => column.Id == persistedCard.ColumnId);

            if (!existingByCardId.TryGetValue(persistedCard.Id, out var cardViewModel))
            {
                cardViewModel = new CardViewModel(persistedCard);
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
            cardViewModel.ApplyLockState(editLock, _sessionId);
        }
    }

    private async Task RefreshLockStatesAsync(CancellationToken cancellationToken)
    {
        var activeLocks = await GetActiveLocksByCardIdAsync(cancellationToken);
        foreach (var cardViewModel in Columns.SelectMany(column => column.Cards))
        {
            activeLocks.TryGetValue(cardViewModel.Model.Id, out var editLock);
            cardViewModel.ApplyLockState(editLock, _sessionId);
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
            if (column is null)
            {
                return;
            }

            var newCard = new Card
            {
                ColumnId = column.Id,
                Title = "Nuova card",
                SortOrder = column.Cards.Count,
                CreatedBy = _settings.UserName,
                UpdatedBy = _settings.UserName,
            };

            await _cardRepository.CreateAsync(newCard, cancellationToken);
            await MergeCardsAsync(cancellationToken);
        },
        "Creazione card...");

    [RelayCommand]
    private Task DeleteCardAsync(CardViewModel? card) => ExecuteWriteAsync(
        async cancellationToken =>
        {
            if (card is null)
            {
                return;
            }

            await _cardRepository.DeleteAsync(card.Model.Id, _settings.UserName, cancellationToken);
            await MergeCardsAsync(cancellationToken);
        },
        "Eliminazione card...");

    public Task MoveCardAsync(CardViewModel card, ColumnViewModel targetColumn, int targetIndex) =>
        ExecuteWriteAsync(
            async cancellationToken =>
            {
                ArgumentNullException.ThrowIfNull(card);
                ArgumentNullException.ThrowIfNull(targetColumn);

                if (!card.CanDrag)
                {
                    throw new CardEditLockException(
                        card.Model.Id,
                        card.IsEditing
                            ? "Termina la modifica prima di spostare la card."
                            : $"La card è in modifica da {card.EditingUserName ?? "un altro utente"}.",
                        card.EditingUserName);
                }

                if (!Columns.Any(column => column.Cards.Contains(card)))
                {
                    throw new InvalidOperationException(
                        "La card trascinata non appartiene più alla board corrente.");
                }

                await _cardRepository.MoveAsync(
                    card.Model.Id,
                    targetColumn.Id,
                    targetIndex,
                    _settings.UserName,
                    cancellationToken);

                await MergeCardsAsync(cancellationToken);
            },
            "Spostamento card...");

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
        if (exception is CardEditLockException or CardConcurrencyException or KeyNotFoundException)
        {
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

        StatusMessage = failure.UserMessage + pathText + retryText;
    }

    private void MarkConnectionHealthy()
    {
        _consecutiveFailures = 0;
        _automaticRetryAllowed = true;
        ConnectionState = BoardConnectionState.Online;
        _lastSuccessfulSyncAt = DateTimeOffset.Now;
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
        ConnectionState = BoardConnectionState.ShuttingDown;
        SetActivity("Rilascio dei lock e chiusura...");
        _pollingTimer.Stop();
        _pollingTimer.Tick -= OnPollingTimerTick;
        _lockHeartbeatTimer.Stop();
        _lockHeartbeatTimer.Tick -= OnLockHeartbeatTimerTick;
        _lifetimeCancellation.Cancel();

        // Attendere la conclusione dell'operazione eventualmente in corso prima di rilasciare
        // tutti i lease della sessione. La finestra resta aperta finché il cleanup termina.
        await _operationGate.WaitAsync(CancellationToken.None);
        try
        {
            try
            {
                await _editLockRepository.ReleaseSessionAsync(_sessionId, CancellationToken.None);
            }
            catch
            {
                // Cleanup best effort: in caso di errore il lease scadrà automaticamente.
            }
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private sealed record BoardSnapshot(
        IReadOnlyList<Column> Columns,
        IReadOnlyList<Card> Cards,
        IReadOnlyList<CardEditLock> ActiveLocks);

}
