using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class CardInformationViewModel : ViewModelBase
{
    public const int HistoryPageSize = 20;

    private readonly Card _card;
    private readonly ICardEventRepository _eventRepository;
    private readonly ICardEditLockRepository _editLockRepository;
    private readonly string _currentSessionId;
    private bool _isLoading;
    private bool _isLoadingMore;
    private bool _hasMoreHistory;
    private bool _initialLoadCompleted;
    private long? _beforeEventId;
    private string? _errorMessage;
    private string _lockStatusText = "Verifica del lock in corso…";
    private string? _lockDetailsText;

    public ObservableCollection<CardHistoryItemViewModel> History { get; } = [];

    public string Title => _card.Title;

    public string IdentifierText => string.IsNullOrWhiteSpace(_card.StableId)
        ? $"Card #{_card.Id}"
        : $"Card #{_card.Id} · {_card.StableId}";

    public string CreatedAtText => FormatLocalDateTime(_card.CreatedAtUtc);

    public string CreatedAtNote => _card.CreatedAtIsEstimated
        ? "Data stimata durante la migrazione dei dati storici"
        : "Data registrata alla creazione";

    public string CreatedByText => NormalizePerson(_card.CreatedBy);

    public string UpdatedAtText => FormatLocalDateTime(_card.UpdatedAtUtc);

    public string UpdatedByText => NormalizePerson(_card.UpdatedBy);

    public string CardTypeName { get; }

    public string WorkflowStateName { get; }

    public string PriorityText { get; }

    public string DueAtText => string.IsNullOrWhiteSpace(_card.DueAtUtc)
        ? "Nessuna scadenza"
        : FormatLocalDateTime(_card.DueAtUtc);

    public string LockStatusText
    {
        get => _lockStatusText;
        private set => SetProperty(ref _lockStatusText, value);
    }

    public string? LockDetailsText
    {
        get => _lockDetailsText;
        private set
        {
            if (SetProperty(ref _lockDetailsText, value))
            {
                OnPropertyChanged(nameof(HasLockDetails));
            }
        }
    }

    public bool HasLockDetails => !string.IsNullOrWhiteSpace(LockDetailsText);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseLoadingStateChanged();
            }
        }
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        private set
        {
            if (SetProperty(ref _isLoadingMore, value))
            {
                RaiseLoadingStateChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsLoadingMore;

    public bool HasMoreHistory
    {
        get => _hasMoreHistory;
        private set
        {
            if (SetProperty(ref _hasMoreHistory, value))
            {
                OnPropertyChanged(nameof(CanLoadMore));
            }
        }
    }

    public bool CanLoadMore => HasMoreHistory && !IsBusy;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(IsHistoryEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasHistory => History.Count > 0;

    public bool IsHistoryEmpty => _initialLoadCompleted && !HasHistory && !HasError;

    public CardInformationViewModel(
        Card card,
        string workflowStateName,
        string cardTypeName,
        string priorityText,
        ICardEventRepository eventRepository,
        ICardEditLockRepository editLockRepository,
        string currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowStateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(priorityText);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        _card = CloneCard(card);
        WorkflowStateName = workflowStateName;
        CardTypeName = cardTypeName;
        PriorityText = priorityText;
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _editLockRepository = editLockRepository ?? throw new ArgumentNullException(nameof(editLockRepository));
        _currentSessionId = currentSessionId;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _initialLoadCompleted)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await LoadCurrentLockAsync(cancellationToken);
            await LoadNextHistoryPageAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossibile caricare la cronologia: {ex.Message}";
        }
        finally
        {
            _initialLoadCompleted = true;
            IsLoading = false;
            RaiseHistoryStateChanged();
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore)
        {
            return;
        }

        IsLoadingMore = true;
        ErrorMessage = null;
        try
        {
            await LoadNextHistoryPageAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossibile caricare altri eventi: {ex.Message}";
        }
        finally
        {
            IsLoadingMore = false;
            RaiseHistoryStateChanged();
        }
    }

    private async Task LoadCurrentLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeLocks = await _editLockRepository.GetActiveAsync(cancellationToken);
            var activeLock = activeLocks.SingleOrDefault(item => item.CardId == _card.Id);
            if (activeLock is null)
            {
                LockStatusText = "Nessun lock di modifica attivo";
                LockDetailsText = null;
                return;
            }

            var isCurrentSession = string.Equals(
                activeLock.SessionId,
                _currentSessionId,
                StringComparison.Ordinal);
            LockStatusText = isCurrentSession
                ? $"In modifica in questa sessione da {NormalizePerson(activeLock.UserName)}"
                : $"In modifica da {NormalizePerson(activeLock.UserName)}";

            var machine = string.IsNullOrWhiteSpace(activeLock.MachineName)
                ? "computer non disponibile"
                : activeLock.MachineName.Trim();
            LockDetailsText =
                $"Computer: {machine} · acquisito {FormatLocalDateTime(activeLock.AcquiredAtUtc)} · " +
                $"scade {FormatLocalDateTime(activeLock.ExpiresAtUtc)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LockStatusText = "Stato del lock non disponibile";
            LockDetailsText = ex.Message;
        }
    }

    private async Task LoadNextHistoryPageAsync(CancellationToken cancellationToken)
    {
        var page = await _eventRepository.GetByCardStableIdAsync(
            _card.StableId,
            HistoryPageSize,
            _beforeEventId,
            cancellationToken);

        foreach (var cardEvent in page)
        {
            History.Add(new CardHistoryItemViewModel(cardEvent));
        }

        if (page.Count > 0)
        {
            _beforeEventId = page[^1].Id;
        }

        HasMoreHistory = page.Count == HistoryPageSize;
    }

    private void RaiseLoadingStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private void RaiseHistoryStateChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsHistoryEmpty));
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private static string NormalizePerson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Non disponibile" : value.Trim();

    private static string FormatLocalDateTime(string? value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return "Non disponibile";
        }

        return parsed.ToLocalTime().ToString(
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.CurrentCulture);
    }

    private static Card CloneCard(Card card) => new()
    {
        Id = card.Id,
        ColumnId = card.ColumnId,
        StableId = card.StableId,
        CreatedAtUtc = card.CreatedAtUtc,
        CreatedAtIsEstimated = card.CreatedAtIsEstimated,
        PriorityId = card.PriorityId,
        CardTypeId = card.CardTypeId,
        PriorityAssignedAtUtc = card.PriorityAssignedAtUtc,
        DueAtUtc = card.DueAtUtc,
        Title = card.Title,
        Notes = card.Notes,
        SortOrder = card.SortOrder,
        CreatedBy = card.CreatedBy,
        UpdatedBy = card.UpdatedBy,
        UpdatedAtUtc = card.UpdatedAtUtc,
        Version = card.Version,
    };
}
