using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class BoardConfigurationViewModel : ViewModelBase
{
    private static readonly DeadlineUnitOption HoursUnit = new("ore", 1);
    private static readonly DeadlineUnitOption DaysUnit = new("giorni", 24);

    private readonly ICardCatalogRepository _repository;
    private readonly IAppLogger _logger;
    private readonly IErrorReferenceGenerator _errorReferences;
    private readonly string _userName;
    private CardCatalogSnapshot _snapshot = new([], [], []);

    private PriorityCatalogItemViewModel? _selectedPriority;
    private CardTypeCatalogItemViewModel? _selectedCardType;
    private bool _isBusy;
    private bool _isPriorityNew;
    private bool _isCardTypeNew;
    private long? _priorityId;
    private int _priorityExpectedVersion;
    private string _priorityCode = string.Empty;
    private string _priorityName = string.Empty;
    private string _priorityDescription = string.Empty;
    private int _priorityDueValue = 1;
    private DeadlineUnitOption _priorityDueUnit = DaysUnit;
    private bool _priorityIsActive = true;
    private bool _priorityIsDefault;
    private long? _cardTypeId;
    private int _cardTypeExpectedVersion;
    private string _cardTypeName = string.Empty;
    private string _cardTypeColorHex = "#3584E4";
    private bool _cardTypeIsActive = true;
    private CardTypeCatalogItemViewModel? _selectedCardTypeDeleteDestination;
    private string? _priorityMessage;
    private string? _cardTypeMessage;
    private bool _priorityMessageIsError;
    private bool _cardTypeMessageIsError;
    private bool _isPriorityDeleteConfirmationVisible;
    private bool _isCardTypeDeleteConfirmationVisible;

    public string ApplicationMilestone => ApplicationVersionInfo.Milestone;

    public IReadOnlyList<DeadlineUnitOption> DeadlineUnits { get; } = [HoursUnit, DaysUnit];

    public ObservableCollection<PriorityCatalogItemViewModel> Priorities { get; } = [];

    public ObservableCollection<CardTypeCatalogItemViewModel> CardTypes { get; } = [];

    public ObservableCollection<CardTypeCatalogItemViewModel> CardTypeDeleteDestinations { get; } = [];

    public ObservableCollection<PriorityDeadlineRuleViewModel> PriorityDeadlineRules { get; } = [];

    public PriorityCatalogItemViewModel? SelectedPriority
    {
        get => _selectedPriority;
        set
        {
            if (SetProperty(ref _selectedPriority, value))
            {
                LoadPriorityEditor(value);
                OnPrioritySelectionChanged();
            }
        }
    }

    public CardTypeCatalogItemViewModel? SelectedCardType
    {
        get => _selectedCardType;
        set
        {
            if (SetProperty(ref _selectedCardType, value))
            {
                LoadCardTypeEditor(value);
                OnCardTypeSelectionChanged();
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
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanEditCardTypeIdentity));
                OnPrioritySelectionChanged();
                OnCardTypeSelectionChanged();
            }
        }
    }

    public bool CanEdit => !IsBusy;

    public bool IsSelectedCardTypeSystem =>
        !IsCardTypeNew && SelectedCardType?.IsSystem == true;

    public bool CanEditCardTypeIdentity =>
        CanEdit && !IsSelectedCardTypeSystem;

    public bool IsPriorityEditorVisible => IsPriorityNew || SelectedPriority is not null;

    public bool IsCardTypeEditorVisible => IsCardTypeNew || SelectedCardType is not null;

    public bool IsPriorityNew
    {
        get => _isPriorityNew;
        private set
        {
            if (SetProperty(ref _isPriorityNew, value))
            {
                OnPropertyChanged(nameof(IsPriorityEditorVisible));
                OnPropertyChanged(nameof(PriorityEditorTitle));
                OnPropertyChanged(nameof(CanDeletePriority));
            }
        }
    }

    public bool IsCardTypeNew
    {
        get => _isCardTypeNew;
        private set
        {
            if (SetProperty(ref _isCardTypeNew, value))
            {
                OnPropertyChanged(nameof(IsCardTypeEditorVisible));
                OnPropertyChanged(nameof(CardTypeEditorTitle));
                OnPropertyChanged(nameof(IsSelectedCardTypeSystem));
                OnPropertyChanged(nameof(CanEditCardTypeIdentity));
                OnPropertyChanged(nameof(CanDeleteCardType));
            }
        }
    }

    public string PriorityEditorTitle => IsPriorityNew ? "Nuova priorità" : "Modifica priorità";

    public string CardTypeEditorTitle => IsCardTypeNew ? "Nuova fascia" : "Modifica fascia";

    public string PriorityCode
    {
        get => _priorityCode;
        set => SetProperty(ref _priorityCode, value);
    }

    public string PriorityName
    {
        get => _priorityName;
        set => SetProperty(ref _priorityName, value);
    }

    public string PriorityDescription
    {
        get => _priorityDescription;
        set => SetProperty(ref _priorityDescription, value);
    }

    public int PriorityDueValue
    {
        get => _priorityDueValue;
        set => SetProperty(ref _priorityDueValue, value);
    }

    public DeadlineUnitOption PriorityDueUnit
    {
        get => _priorityDueUnit;
        set => SetProperty(ref _priorityDueUnit, value);
    }

    public bool PriorityIsActive
    {
        get => _priorityIsActive;
        set => SetProperty(ref _priorityIsActive, value);
    }

    public bool PriorityIsDefault
    {
        get => _priorityIsDefault;
        set => SetProperty(ref _priorityIsDefault, value);
    }

    public string CardTypeName
    {
        get => _cardTypeName;
        set => SetProperty(ref _cardTypeName, value);
    }

    public string CardTypeColorHex
    {
        get => _cardTypeColorHex;
        set
        {
            if (SetProperty(ref _cardTypeColorHex, value))
            {
                OnPropertyChanged(nameof(CardTypeColorBrush));
            }
        }
    }

    public IBrush CardTypeColorBrush => ColorHexParser.ToBrush(CardTypeColorHex);

    public bool CardTypeIsActive
    {
        get => _cardTypeIsActive;
        set => SetProperty(ref _cardTypeIsActive, value);
    }

    public CardTypeCatalogItemViewModel? SelectedCardTypeDeleteDestination
    {
        get => _selectedCardTypeDeleteDestination;
        set
        {
            if (SetProperty(ref _selectedCardTypeDeleteDestination, value))
            {
                OnPropertyChanged(nameof(CanConfirmDeleteCardType));
            }
        }
    }

    public bool SelectedCardTypeHasCards => SelectedCardType?.CardCount > 0;

    public string CardTypeDeleteDescription => SelectedCardTypeHasCards
        ? $"La fascia contiene {SelectedCardType!.CardCountText}. Scegli la fascia di destinazione: stato e ordine saranno mantenuti."
        : "La fascia è vuota e può essere eliminata. Le regole di scadenza collegate verranno rimosse.";

    public bool CanConfirmDeleteCardType =>
        CanDeleteCardType &&
        (!SelectedCardTypeHasCards || SelectedCardTypeDeleteDestination is not null);

    public string? PriorityMessage
    {
        get => _priorityMessage;
        private set
        {
            if (SetProperty(ref _priorityMessage, value))
            {
                OnPropertyChanged(nameof(HasPriorityMessage));
                OnPropertyChanged(nameof(ShowPriorityError));
                OnPropertyChanged(nameof(ShowPrioritySuccess));
            }
        }
    }

    public bool HasPriorityMessage => !string.IsNullOrWhiteSpace(PriorityMessage);

    public bool ShowPriorityError => HasPriorityMessage && PriorityMessageIsError;

    public bool ShowPrioritySuccess => HasPriorityMessage && !PriorityMessageIsError;

    public bool PriorityMessageIsError
    {
        get => _priorityMessageIsError;
        private set
        {
            if (SetProperty(ref _priorityMessageIsError, value))
            {
                OnPropertyChanged(nameof(PriorityMessageIsSuccess));
                OnPropertyChanged(nameof(ShowPriorityError));
                OnPropertyChanged(nameof(ShowPrioritySuccess));
            }
        }
    }

    public bool PriorityMessageIsSuccess => !PriorityMessageIsError;

    public string? CardTypeMessage
    {
        get => _cardTypeMessage;
        private set
        {
            if (SetProperty(ref _cardTypeMessage, value))
            {
                OnPropertyChanged(nameof(HasCardTypeMessage));
                OnPropertyChanged(nameof(ShowCardTypeError));
                OnPropertyChanged(nameof(ShowCardTypeSuccess));
            }
        }
    }

    public bool HasCardTypeMessage => !string.IsNullOrWhiteSpace(CardTypeMessage);

    public bool ShowCardTypeError => HasCardTypeMessage && CardTypeMessageIsError;

    public bool ShowCardTypeSuccess => HasCardTypeMessage && !CardTypeMessageIsError;

    public bool CardTypeMessageIsError
    {
        get => _cardTypeMessageIsError;
        private set
        {
            if (SetProperty(ref _cardTypeMessageIsError, value))
            {
                OnPropertyChanged(nameof(CardTypeMessageIsSuccess));
                OnPropertyChanged(nameof(ShowCardTypeError));
                OnPropertyChanged(nameof(ShowCardTypeSuccess));
            }
        }
    }

    public bool CardTypeMessageIsSuccess => !CardTypeMessageIsError;

    public bool IsPriorityDeleteConfirmationVisible
    {
        get => _isPriorityDeleteConfirmationVisible;
        private set => SetProperty(ref _isPriorityDeleteConfirmationVisible, value);
    }

    public bool IsCardTypeDeleteConfirmationVisible
    {
        get => _isCardTypeDeleteConfirmationVisible;
        private set => SetProperty(ref _isCardTypeDeleteConfirmationVisible, value);
    }

    public bool CanDeletePriority => !IsPriorityNew && SelectedPriority is not null && !IsBusy;

    public bool CanDeleteCardType =>
        !IsCardTypeNew &&
        SelectedCardType is { IsSystem: false } &&
        !IsBusy;

    public bool CanMovePriorityUp =>
        SelectedPriority is not null && Priorities.IndexOf(SelectedPriority) > 0 && !IsBusy;

    public bool CanMovePriorityDown =>
        SelectedPriority is not null &&
        Priorities.IndexOf(SelectedPriority) >= 0 &&
        Priorities.IndexOf(SelectedPriority) < Priorities.Count - 1 &&
        !IsBusy;

    public bool CanMoveCardTypeUp =>
        SelectedCardType is { IsSystem: false } &&
        CardTypes.IndexOf(SelectedCardType) > 0 &&
        CardTypes[CardTypes.IndexOf(SelectedCardType) - 1].IsSystem == false &&
        !IsBusy;

    public bool CanMoveCardTypeDown =>
        SelectedCardType is { IsSystem: false } &&
        CardTypes.IndexOf(SelectedCardType) >= 0 &&
        CardTypes.IndexOf(SelectedCardType) < CardTypes.Count - 1 &&
        !IsBusy;

    public BoardConfigurationViewModel(
        ICardCatalogRepository repository,
        IAppLogger? logger = null,
        IErrorReferenceGenerator? errorReferences = null,
        string userName = "Sconosciuto")
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullAppLogger.Instance;
        _errorReferences = errorReferences ?? new ErrorReferenceGenerator();
        _userName = string.IsNullOrWhiteSpace(userName) ? "Sconosciuto" : userName.Trim();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ExecuteAsync(
            async () => await RefreshAsync(null, null),
            isPriorityOperation: true,
            successMessage: null);
    }

    [RelayCommand]
    private void NewPriority()
    {
        SelectedPriority = null;
        IsPriorityNew = true;
        _priorityId = null;
        _priorityExpectedVersion = 0;
        PriorityCode = string.Empty;
        PriorityName = string.Empty;
        PriorityDescription = string.Empty;
        PriorityDueValue = 1;
        PriorityDueUnit = DaysUnit;
        PriorityIsActive = true;
        PriorityIsDefault = false;
        IsPriorityDeleteConfirmationVisible = false;
        PriorityMessage = null;
        BuildPriorityRules(null);
    }

    [RelayCommand]
    private void CancelPriorityEdit()
    {
        IsPriorityDeleteConfirmationVisible = false;
        if (IsPriorityNew)
        {
            IsPriorityNew = false;
            SelectedPriority = Priorities.FirstOrDefault();
            return;
        }

        LoadPriorityEditor(SelectedPriority);
    }

    [RelayCommand]
    private async Task SavePriorityAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var request = new PrioritySaveRequest(
            _priorityId,
            _priorityExpectedVersion,
            PriorityCode,
            PriorityName,
            PriorityDescription,
            GetHours(PriorityDueValue, PriorityDueUnit),
            PriorityIsActive,
            PriorityIsDefault,
            PriorityDeadlineRules
                .Where(item => item.UseOverride)
                .Select(item => new PriorityDeadlineOverrideInput(item.CardTypeId, item.GetDueHours()))
                .ToArray());

        await ExecuteAsync(
            async () =>
            {
                var saved = await _repository.SavePriorityAsync(request);
                await RefreshAsync(saved.Id, SelectedCardType?.Id);
                _logger.Information(
                    "catalog.priority.saved",
                    "Priorità salvata.",
                    new Dictionary<string, object?> { ["priorityId"] = saved.Id });
            },
            isPriorityOperation: true,
            successMessage: "Priorità salvata.");
    }

    [RelayCommand]
    private void RequestDeletePriority()
    {
        if (CanDeletePriority)
        {
            IsPriorityDeleteConfirmationVisible = true;
        }
    }

    [RelayCommand]
    private void CancelDeletePriority() => IsPriorityDeleteConfirmationVisible = false;

    [RelayCommand]
    private async Task ConfirmDeletePriorityAsync()
    {
        if (!CanDeletePriority || SelectedPriority is null)
        {
            return;
        }

        var deletedId = SelectedPriority.Id;
        var deletedVersion = SelectedPriority.Version;
        await ExecuteAsync(
            async () =>
            {
                await _repository.DeletePriorityAsync(deletedId, deletedVersion);
                await RefreshAsync(null, SelectedCardType?.Id);
                _logger.Information(
                    "catalog.priority.deleted",
                    "Priorità eliminata.",
                    new Dictionary<string, object?> { ["priorityId"] = deletedId });
            },
            isPriorityOperation: true,
            successMessage: "Priorità eliminata.");
    }

    [RelayCommand]
    private Task MovePriorityUpAsync() => MovePriorityAsync(-1);

    [RelayCommand]
    private Task MovePriorityDownAsync() => MovePriorityAsync(1);

    private async Task MovePriorityAsync(int direction)
    {
        if (SelectedPriority is null || IsBusy)
        {
            return;
        }

        var currentIndex = Priorities.IndexOf(SelectedPriority);
        var targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Priorities.Count)
        {
            return;
        }

        var selectedId = SelectedPriority.Id;
        var order = Priorities.ToList();
        var item = order[currentIndex];
        order.RemoveAt(currentIndex);
        order.Insert(targetIndex, item);

        await ExecuteAsync(
            async () =>
            {
                await _repository.ReorderPrioritiesAsync(
                    order.Select(entry => new CatalogOrderItem(entry.Id, entry.Version)).ToArray());
                await RefreshAsync(selectedId, SelectedCardType?.Id);
            },
            isPriorityOperation: true,
            successMessage: "Ordine delle priorità aggiornato.");
    }

    [RelayCommand]
    private void NewCardType()
    {
        SelectedCardType = null;
        IsCardTypeNew = true;
        _cardTypeId = null;
        _cardTypeExpectedVersion = 0;
        CardTypeName = string.Empty;
        CardTypeColorHex = "#3584E4";
        CardTypeIsActive = true;
        ResetCardTypeDeleteConfirmation();
        CardTypeMessage = null;
    }

    [RelayCommand]
    private void CancelCardTypeEdit()
    {
        ResetCardTypeDeleteConfirmation();
        if (IsCardTypeNew)
        {
            IsCardTypeNew = false;
            SelectedCardType = CardTypes.FirstOrDefault();
            return;
        }

        LoadCardTypeEditor(SelectedCardType);
    }

    [RelayCommand]
    private async Task SaveCardTypeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var request = new CardTypeSaveRequest(
            _cardTypeId,
            _cardTypeExpectedVersion,
            CardTypeName,
            CardTypeColorHex,
            CardTypeIsActive);

        await ExecuteAsync(
            async () =>
            {
                var saved = await _repository.SaveCardTypeAsync(request);
                await RefreshAsync(SelectedPriority?.Id, saved.Id);
                _logger.Information(
                    "catalog.cardType.saved",
                    "Fascia salvata.",
                    new Dictionary<string, object?> { ["cardTypeId"] = saved.Id });
            },
            isPriorityOperation: false,
            successMessage: "Fascia salvata.");
    }

    [RelayCommand]
    private void RequestDeleteCardType()
    {
        if (CanDeleteCardType)
        {
            BuildCardTypeDeleteDestinations();
            IsCardTypeDeleteConfirmationVisible = true;
            OnPropertyChanged(nameof(CardTypeDeleteDescription));
            OnPropertyChanged(nameof(CanConfirmDeleteCardType));
        }
    }

    [RelayCommand]
    private void CancelDeleteCardType() => ResetCardTypeDeleteConfirmation();

    [RelayCommand]
    private async Task ConfirmDeleteCardTypeAsync()
    {
        if (!CanConfirmDeleteCardType || SelectedCardType is null)
        {
            return;
        }

        var deletedId = SelectedCardType.Id;
        var deletedVersion = SelectedCardType.Version;
        var destination = SelectedCardTypeDeleteDestination;
        await ExecuteAsync(
            async () =>
            {
                await _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                    deletedId,
                    deletedVersion,
                    destination?.Id,
                    destination?.Version,
                    _userName));
                await RefreshAsync(SelectedPriority?.Id, null);
                _logger.Information(
                    "catalog.cardType.deleted",
                    "Fascia eliminata.",
                    new Dictionary<string, object?> { ["cardTypeId"] = deletedId });
            },
            isPriorityOperation: false,
            successMessage: "Fascia eliminata.");
    }

    [RelayCommand]
    private Task MoveCardTypeUpAsync() => MoveCardTypeAsync(-1);

    [RelayCommand]
    private Task MoveCardTypeDownAsync() => MoveCardTypeAsync(1);

    private async Task MoveCardTypeAsync(int direction)
    {
        if (SelectedCardType is null || IsBusy)
        {
            return;
        }

        var currentIndex = CardTypes.IndexOf(SelectedCardType);
        var targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= CardTypes.Count)
        {
            return;
        }

        var selectedId = SelectedCardType.Id;
        var order = CardTypes.ToList();
        var item = order[currentIndex];
        order.RemoveAt(currentIndex);
        order.Insert(targetIndex, item);

        await ExecuteAsync(
            async () =>
            {
                await _repository.ReorderCardTypesAsync(
                    order
                        .Where(entry => !entry.IsSystem)
                        .Select(entry => new CatalogOrderItem(entry.Id, entry.Version))
                        .ToArray());
                await RefreshAsync(SelectedPriority?.Id, selectedId);
            },
            isPriorityOperation: false,
            successMessage: "Ordine delle fasce aggiornato.");
    }

    private async Task RefreshAsync(long? selectedPriorityId, long? selectedCardTypeId)
    {
        _snapshot = await _repository.GetSnapshotAsync();

        Priorities.Clear();
        foreach (var priority in _snapshot.Priorities)
        {
            Priorities.Add(new PriorityCatalogItemViewModel(priority));
        }

        CardTypes.Clear();
        foreach (var cardType in _snapshot.CardTypes)
        {
            CardTypes.Add(new CardTypeCatalogItemViewModel(cardType));
        }

        SelectedPriority = selectedPriorityId is long priorityId
            ? Priorities.FirstOrDefault(item => item.Id == priorityId)
            : Priorities.FirstOrDefault();
        SelectedCardType = selectedCardTypeId is long cardTypeId
            ? CardTypes.FirstOrDefault(item => item.Id == cardTypeId)
            : CardTypes.FirstOrDefault();
        IsPriorityNew = false;
        IsCardTypeNew = false;
        OnPrioritySelectionChanged();
        OnCardTypeSelectionChanged();
    }

    private void LoadPriorityEditor(PriorityCatalogItemViewModel? selected)
    {
        IsPriorityDeleteConfirmationVisible = false;
        PriorityMessage = null;

        if (selected is null)
        {
            if (!IsPriorityNew)
            {
                _priorityId = null;
                _priorityExpectedVersion = 0;
                PriorityDeadlineRules.Clear();
                OnPropertyChanged(nameof(IsPriorityEditorVisible));
            }

            return;
        }

        IsPriorityNew = false;
        var model = selected.Model;
        _priorityId = model.Id;
        _priorityExpectedVersion = model.Version;
        PriorityCode = model.Code;
        PriorityName = model.Name;
        PriorityDescription = model.Description ?? string.Empty;
        (PriorityDueValue, PriorityDueUnit) = SplitHours(model.DefaultDueHours);
        PriorityIsActive = model.IsActive;
        PriorityIsDefault = model.IsDefault;
        BuildPriorityRules(model.Id);
        OnPropertyChanged(nameof(IsPriorityEditorVisible));
        OnPropertyChanged(nameof(PriorityEditorTitle));
        OnPropertyChanged(nameof(CanDeletePriority));
    }

    private void BuildPriorityRules(long? priorityId)
    {
        PriorityDeadlineRules.Clear();
        foreach (var cardType in _snapshot.CardTypes.OrderBy(item => item.SortOrder).ThenBy(item => item.Id))
        {
            var existing = priorityId is long id
                ? _snapshot.DeadlineRules.FirstOrDefault(rule =>
                    rule.PriorityId == id && rule.CardTypeId == cardType.Id)
                : null;
            var dueHours = existing?.DueHours ?? 24;
            var split = SplitHours(dueHours);
            PriorityDeadlineRules.Add(new PriorityDeadlineRuleViewModel(
                cardType.Id,
                cardType.Name,
                cardType.ColorHex,
                cardType.IsActive,
                DeadlineUnits,
                existing is not null,
                split.Value,
                split.Unit));
        }
    }

    private void LoadCardTypeEditor(CardTypeCatalogItemViewModel? selected)
    {
        ResetCardTypeDeleteConfirmation();
        CardTypeMessage = null;

        if (selected is null)
        {
            if (!IsCardTypeNew)
            {
                _cardTypeId = null;
                _cardTypeExpectedVersion = 0;
                OnPropertyChanged(nameof(IsCardTypeEditorVisible));
            }

            return;
        }

        IsCardTypeNew = false;
        var model = selected.Model;
        _cardTypeId = model.Id;
        _cardTypeExpectedVersion = model.Version;
        CardTypeName = model.Name;
        CardTypeColorHex = model.ColorHex;
        CardTypeIsActive = model.IsActive;
        OnPropertyChanged(nameof(IsCardTypeEditorVisible));
        OnPropertyChanged(nameof(CardTypeEditorTitle));
        OnPropertyChanged(nameof(CanDeleteCardType));
    }

    private void BuildCardTypeDeleteDestinations()
    {
        CardTypeDeleteDestinations.Clear();
        if (SelectedCardType is null)
        {
            SelectedCardTypeDeleteDestination = null;
            return;
        }

        foreach (var candidate in CardTypes
                     .Where(item => item.Id != SelectedCardType.Id && item.IsActive)
                     .OrderBy(item => item.SortOrder)
                     .ThenBy(item => item.Id))
        {
            CardTypeDeleteDestinations.Add(candidate);
        }

        SelectedCardTypeDeleteDestination = CardTypeDeleteDestinations
            .FirstOrDefault(item => item.IsSystem)
            ?? CardTypeDeleteDestinations.FirstOrDefault();
    }

    private void ResetCardTypeDeleteConfirmation()
    {
        IsCardTypeDeleteConfirmationVisible = false;
        CardTypeDeleteDestinations.Clear();
        SelectedCardTypeDeleteDestination = null;
        OnPropertyChanged(nameof(CanConfirmDeleteCardType));
    }

    private async Task ExecuteAsync(
        Func<Task> operation,
        bool isPriorityOperation,
        string? successMessage)
    {
        IsBusy = true;
        if (isPriorityOperation)
        {
            PriorityMessage = null;
        }
        else
        {
            CardTypeMessage = null;
        }

        try
        {
            await operation();
            SetMessage(isPriorityOperation, successMessage, isError: false);
        }
        catch (Exception ex) when (ex is CardCatalogValidationException or
                                         CardCatalogConcurrencyException or
                                         CardCatalogItemInUseException)
        {
            if (ex is CardCatalogConcurrencyException)
            {
                await TryReloadAfterConcurrencyAsync();
            }

            SetMessage(isPriorityOperation, ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            var reference = _errorReferences.Create();
            _logger.Error(
                "catalog.operation.failed",
                "Operazione sul catalogo non riuscita.",
                ex,
                reference);
            SetMessage(
                isPriorityOperation,
                $"Impossibile completare l'operazione. Riferimento: {reference}",
                isError: true);
        }
        finally
        {
            IsBusy = false;
            OnPrioritySelectionChanged();
            OnCardTypeSelectionChanged();
        }
    }

    private async Task TryReloadAfterConcurrencyAsync()
    {
        try
        {
            await RefreshAsync(SelectedPriority?.Id, SelectedCardType?.Id);
        }
        catch
        {
            // Il messaggio di concorrenza resta visibile; il reload potrà essere ripetuto riaprendo la finestra.
        }
    }

    private void SetMessage(bool priority, string? message, bool isError)
    {
        if (priority)
        {
            PriorityMessageIsError = isError;
            PriorityMessage = message;
        }
        else
        {
            CardTypeMessageIsError = isError;
            CardTypeMessage = message;
        }
    }

    private void OnPrioritySelectionChanged()
    {
        OnPropertyChanged(nameof(CanMovePriorityUp));
        OnPropertyChanged(nameof(CanMovePriorityDown));
        OnPropertyChanged(nameof(CanDeletePriority));
        OnPropertyChanged(nameof(IsPriorityEditorVisible));
    }

    private void OnCardTypeSelectionChanged()
    {
        OnPropertyChanged(nameof(CanMoveCardTypeUp));
        OnPropertyChanged(nameof(CanMoveCardTypeDown));
        OnPropertyChanged(nameof(CanDeleteCardType));
        OnPropertyChanged(nameof(IsSelectedCardTypeSystem));
        OnPropertyChanged(nameof(CanEditCardTypeIdentity));
        OnPropertyChanged(nameof(IsCardTypeEditorVisible));
        OnPropertyChanged(nameof(SelectedCardTypeHasCards));
        OnPropertyChanged(nameof(CardTypeDeleteDescription));
        OnPropertyChanged(nameof(CanConfirmDeleteCardType));
    }

    private static int GetHours(int value, DeadlineUnitOption unit)
    {
        if (value <= 0)
        {
            return value;
        }

        try
        {
            return checked(value * unit.HoursMultiplier);
        }
        catch (OverflowException)
        {
            return int.MaxValue;
        }
    }

    private static (int Value, DeadlineUnitOption Unit) SplitHours(int hours) =>
        hours % 24 == 0
            ? (hours / 24, DaysUnit)
            : (hours, HoursUnit);
}
