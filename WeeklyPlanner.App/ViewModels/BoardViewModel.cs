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
    private bool _isInitialized;
    private bool _isStarted;
    private bool _isDisposed;
    private bool _isOffline;
    private bool _isLoading;
    private string? _statusMessage;

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();

    public bool IsOffline
    {
        get => _isOffline;
        private set => SetProperty(ref _isOffline, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string CurrentUserName => _settings.UserName;

    public BoardViewModel(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _settings.Normalize();

        var connectionFactory = new SqliteConnectionFactory(_settings.DatabasePath);
        _databaseInitializer = new DatabaseInitializer(connectionFactory);

        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        _cardRepository = new CardRepository(connectionFactory, writePipeline);
        _editLockRepository = new CardEditLockRepository(connectionFactory, writePipeline);
        _columnRepository = new ColumnRepository(connectionFactory);
        _changeDetector = new BoardChangeDetector(new BoardRevisionRepository(connectionFactory));

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
        await RefreshBoardSafelyAsync(isInitialLoad: true);

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

        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                // Un secondo GotFocus può essersi accodato mentre il primo acquisiva il lease.
                // Ricontrollare lo stato dentro il gate evita un rinnovo/acquisizione duplicata.
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
                    // Il focus può aver consentito alcuni caratteri mentre l'acquisizione asincrona
                    // era in corso. Ripristinare sempre i valori persistiti quando il lock è negato.
                    card.RefreshFromModel(card.Model);
                    IsOffline = false;
                    StatusMessage = $"{result.CurrentLock.UserName} sta già modificando questa card.";
                    return false;
                }

                card.BeginEdit(result.CurrentLock, _sessionId);
                _consecutiveFailures = 0;
                IsOffline = false;
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
            // Se l'acquisizione fallisce, eventuali caratteri digitati durante l'attesa
            // non rappresentano una bozza protetta e devono essere ripristinati.
            card.RefreshFromModel(card.Model);
            SetOperationFailure(ex);
            return false;
        }
    }

    [RelayCommand]
    private async Task CommitEditAsync(CardViewModel? card)
    {
        if (_isDisposed || card is null || !card.IsEditing)
        {
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
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

                _consecutiveFailures = 0;
                IsOffline = false;
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
            IsOffline = false;
            StatusMessage = "Conflitto rilevato: la bozza è conservata e non è stata sovrascritta.";
        }
        catch (CardEditLockException ex)
        {
            var activeLocks = await TryGetActiveLocksAsync();
            activeLocks.TryGetValue(card.Model.Id, out var currentLock);
            card.MarkLockLost(currentLock, _sessionId);
            IsOffline = false;
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            SetOperationFailure(ex);
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
                IsOffline = false;
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
    }

    private async void OnPollingTimerTick(object? sender, EventArgs e)
    {
        await RefreshBoardSafelyAsync(isInitialLoad: false);
    }

    private async void OnLockHeartbeatTimerTick(object? sender, EventArgs e)
    {
        await RenewEditingLocksSafelyAsync();
    }

    private async Task RefreshBoardSafelyAsync(bool isInitialLoad)
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
            if (isInitialLoad)
            {
                IsLoading = true;
                StatusMessage = "Caricamento board...";
            }

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
                // non incrementa BoardState.Revision. I lock attivi vengono riletti comunque
                // a ogni polling per rimuovere indicatori scaduti senza attendere altre modifiche.
                await RefreshLockStatesAsync(cancellationToken);
            }

            _consecutiveFailures = 0;
            IsOffline = false;
            if (!Columns.SelectMany(column => column.Cards).Any(card =>
                    card.HasExternalChanges || card.HasLostEditLock || card.IsDeletedExternally))
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
            _consecutiveFailures++;

            if (isInitialLoad || _consecutiveFailures >= ConsecutiveFailuresBeforeOffline)
            {
                IsOffline = true;
                StatusMessage = isInitialLoad
                    ? $"Impossibile aprire il database '{_settings.DatabasePath}': {ex.Message}"
                    : $"Database non raggiungibile ({ex.GetType().Name}). Nuovo tentativo automatico...";
            }
        }
        finally
        {
            IsLoading = false;
            _operationGate.Release();
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

                IsOffline = false;
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
        await Task.Run(_databaseInitializer.EnsureInitialized, cancellationToken);

        var columns = await _columnRepository.GetAllAsync(cancellationToken);
        Columns.Clear();
        foreach (var column in columns)
        {
            Columns.Add(new ColumnViewModel(column));
        }

        await MergeCardsAsync(cancellationToken);
        _isInitialized = true;
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
    private Task AddCardAsync(ColumnViewModel? column) => ExecuteWriteAsync(async cancellationToken =>
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
    });

    [RelayCommand]
    private Task DeleteCardAsync(CardViewModel? card) => ExecuteWriteAsync(async cancellationToken =>
    {
        if (card is null)
        {
            return;
        }

        await _cardRepository.DeleteAsync(card.Model.Id, _settings.UserName, cancellationToken);
        await MergeCardsAsync(cancellationToken);
    });

    public Task MoveCardAsync(CardViewModel card, ColumnViewModel targetColumn, int targetIndex) =>
        ExecuteWriteAsync(async cancellationToken =>
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
        });

    private async Task ExecuteWriteAsync(Func<CancellationToken, Task> operation)
    {
        if (_isDisposed)
        {
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
                _consecutiveFailures = 0;
                IsOffline = false;
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
    }

    private void SetOperationFailure(Exception exception)
    {
        if (exception is CardEditLockException or CardConcurrencyException)
        {
            IsOffline = false;
            StatusMessage = exception.Message;
            return;
        }

        IsOffline = true;
        StatusMessage = $"Operazione non completata: {exception.Message}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pollingTimer.Stop();
        _pollingTimer.Tick -= OnPollingTimerTick;
        _lockHeartbeatTimer.Stop();
        _lockHeartbeatTimer.Tick -= OnLockHeartbeatTimerTick;
        _lifetimeCancellation.Cancel();

        // Attendere la conclusione dell'operazione eventualmente in corso prima di rilasciare
        // i lock e distruggere il gate. In questo modo nessun finally tenta di usare un
        // SemaphoreSlim già disposto.
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
}
