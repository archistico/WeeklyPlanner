using System.Collections.ObjectModel;
using System.Globalization;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class CardViewModel : ViewModelBase
{
    private string _title;
    private string? _notes;
    private string _originalTitle;
    private string? _originalNotes;
    private long? _originalPriorityId;
    private CardPriorityOptionViewModel? _selectedPriorityOption;
    private DateTimeOffset _displayNow;
    private int _editExpectedVersion;
    private bool _isEditing;
    private bool _hasExternalChanges;
    private bool _hasLostEditLock;
    private bool _isDeletedExternally;
    private bool _isLockedByAnotherUser;
    private string? _editingUserName;
    private bool _isDeleteConfirmationVisible;
    private bool _isSaving;
    private string? _saveStatusText;
    private bool _hasSaveError;
    private bool _hasSaveSuccess;
    private bool _isDropBeforeVisible;
    private bool _isDropAfterVisible;

    public Card Model { get; }

    public string Title
    {
        get => _title;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (SetProperty(ref _title, normalizedValue))
            {
                ClearSaveFeedbackAfterUserChange();
                RaiseDerivedStateChanged();
            }
        }
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value))
            {
                ClearSaveFeedbackAfterUserChange();
                RaiseDerivedStateChanged();
            }
        }
    }

    public ObservableCollection<CardPriorityOptionViewModel> PriorityOptions { get; } = [];

    public CardPriorityOptionViewModel? SelectedPriorityOption
    {
        get => _selectedPriorityOption;
        set
        {
            var normalized = value ?? PriorityOptions.FirstOrDefault(option => option.IsNone);
            if (SetProperty(ref _selectedPriorityOption, normalized))
            {
                ClearSaveFeedbackAfterUserChange();
                RaiseDerivedStateChanged();
            }
        }
    }

    public long? SelectedPriorityId => SelectedPriorityOption?.Id;

    public bool IsNotEditing => !IsEditing;

    public bool HasPriority => SelectedPriorityId is not null;

    public string PriorityBadgeCode => SelectedPriorityOption?.BadgeText ?? "—";

    public string PriorityBadgeText => SelectedPriorityOption is null || SelectedPriorityOption.IsNone
        ? "Nessuna priorità"
        : SelectedPriorityOption.Name;

    public string PriorityToolTipText
    {
        get
        {
            if (SelectedPriorityOption is null || SelectedPriorityOption.IsNone)
            {
                return "Nessuna priorità assegnata.";
            }

            var description = string.IsNullOrWhiteSpace(SelectedPriorityOption.Description)
                ? $"Priorità {SelectedPriorityOption.Name}."
                : SelectedPriorityOption.Description.Trim();
            if (!HasDueStatus)
            {
                return description;
            }

            return string.IsNullOrWhiteSpace(DueExactText)
                ? $"{description} {DueStatusText}."
                : $"{description} {DueStatusText}. Scadenza: {DueExactText}.";
        }
    }

    public bool HasDueStatus => !string.IsNullOrWhiteSpace(DueStatusText);

    public bool IsOverdue
    {
        get
        {
            var dueAt = GetDisplayedDueAt();
            return dueAt is not null && dueAt.Value < _displayNow.ToUniversalTime();
        }
    }

    public string? DueStatusText
    {
        get
        {
            var dueAt = GetDisplayedDueAt();
            if (dueAt is null)
            {
                return null;
            }

            var remaining = dueAt.Value - _displayNow.ToUniversalTime();
            if (remaining < TimeSpan.Zero)
            {
                return $"Scaduta da {FormatDuration(-remaining)}";
            }

            return $"Scade tra {FormatDuration(remaining)}";
        }
    }

    public string? DueExactText
    {
        get
        {
            var dueAt = GetDisplayedDueAt();
            return dueAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        }
    }

    public long ColumnId => Model.ColumnId;

    public string? Author => Model.CreatedBy;

    public int TitleMaxLength => Card.MaxTitleLength;

    public string TitleLengthText => $"{Title.Length}/{Card.MaxTitleLength}";

    public bool IsTitleValid =>
        !string.IsNullOrWhiteSpace(Title) &&
        Title.Trim().Length <= Card.MaxTitleLength;

    public bool HasTitleValidationError => IsEditing && !IsTitleValid;

    public string TitleValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                return "Il titolo è obbligatorio.";
            }

            if (Title.Trim().Length > Card.MaxTitleLength)
            {
                return $"Il titolo non può superare {Card.MaxTitleLength} caratteri.";
            }

            return string.Empty;
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool HasExternalChanges
    {
        get => _hasExternalChanges;
        private set
        {
            if (SetProperty(ref _hasExternalChanges, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool HasLostEditLock
    {
        get => _hasLostEditLock;
        private set
        {
            if (SetProperty(ref _hasLostEditLock, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool IsDeletedExternally
    {
        get => _isDeletedExternally;
        private set
        {
            if (SetProperty(ref _isDeletedExternally, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool IsLockedByAnotherUser
    {
        get => _isLockedByAnotherUser;
        private set
        {
            if (SetProperty(ref _isLockedByAnotherUser, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public string? EditingUserName
    {
        get => _editingUserName;
        private set
        {
            if (SetProperty(ref _editingUserName, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isDeleteConfirmationVisible, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public string? SaveStatusText
    {
        get => _saveStatusText;
        private set
        {
            if (SetProperty(ref _saveStatusText, value))
            {
                OnPropertyChanged(nameof(HasSaveStatus));
            }
        }
    }

    public bool HasSaveError
    {
        get => _hasSaveError;
        private set
        {
            if (SetProperty(ref _hasSaveError, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public bool HasSaveSuccess
    {
        get => _hasSaveSuccess;
        private set => SetProperty(ref _hasSaveSuccess, value);
    }

    public bool HasSaveStatus => !string.IsNullOrWhiteSpace(SaveStatusText);

    public bool HasLastSavedAt => GetLastSavedAt() is not null;

    public string LastSavedRelativeText
    {
        get
        {
            var savedAt = GetLastSavedAt();
            return savedAt is null
                ? string.Empty
                : FormatRelativeTime(_displayNow.ToUniversalTime() - savedAt.Value);
        }
    }

    public string LastSavedExactText
    {
        get
        {
            var savedAt = GetLastSavedAt();
            return savedAt is null
                ? string.Empty
                : savedAt.Value.ToLocalTime().ToString(
                    "dd/MM/yyyy HH:mm:ss",
                    CultureInfo.CurrentCulture);
        }
    }

    public string LastSavedToolTipText
    {
        get
        {
            if (!HasLastSavedAt)
            {
                return "Data dell'ultimo salvataggio non disponibile.";
            }

            var updatedBy = string.IsNullOrWhiteSpace(Model.UpdatedBy)
                ? string.Empty
                : $" da {Model.UpdatedBy}";
            return $"Ultimo salvataggio: {LastSavedExactText}{updatedBy}.";
        }
    }

    public bool ShowDirtyPersistenceState => IsDirty && !IsSaving && !HasSaveError;

    public bool ShowSavingPersistenceState => IsSaving;

    public bool ShowErrorPersistenceState => HasSaveError;

    public bool ShowSavedPersistenceState =>
        !IsDirty &&
        !IsSaving &&
        !HasSaveError &&
        HasLastSavedAt;

    public bool IsDropBeforeVisible
    {
        get => _isDropBeforeVisible;
        private set => SetProperty(ref _isDropBeforeVisible, value);
    }

    public bool IsDropAfterVisible
    {
        get => _isDropAfterVisible;
        private set => SetProperty(ref _isDropAfterVisible, value);
    }

    public bool IsDirty =>
        IsEditing &&
        (!string.Equals(Title, _originalTitle, StringComparison.Ordinal) ||
         !string.Equals(Notes, _originalNotes, StringComparison.Ordinal) ||
         SelectedPriorityId != _originalPriorityId);

    public bool IsEditorReadOnly =>
        !IsEditing ||
        IsSaving ||
        IsLockedByAnotherUser ||
        IsDeletedExternally ||
        HasLostEditLock ||
        HasExternalChanges;

    // Primo livello di difesa UX: non propone il trascinamento di una card in modifica.
    // Il repository ripete il controllo sul lock perché deve proteggere anche altre istanze,
    // chiamanti non UI e race condition fra la verifica visuale e la transazione SQLite.
    public bool CanDrag =>
        !IsEditing &&
        !IsLockedByAnotherUser &&
        !IsDeletedExternally &&
        !IsDeleteConfirmationVisible;

    public bool CanCancelEdit => IsEditing && !IsSaving;

    public bool CanEditDraft =>
        IsEditing &&
        !IsSaving &&
        !IsLockedByAnotherUser &&
        !IsDeletedExternally &&
        !HasLostEditLock &&
        !HasExternalChanges;

    public bool CanSave =>
        IsEditing &&
        IsDirty &&
        IsTitleValid &&
        !IsSaving &&
        !HasExternalChanges &&
        !HasLostEditLock &&
        !IsDeletedExternally;

    public bool CanRequestDelete =>
        !IsEditing &&
        !IsSaving &&
        !IsLockedByAnotherUser &&
        !IsDeletedExternally;

    public bool ShowDeleteButton => CanRequestDelete && !IsDeleteConfirmationVisible;

    public bool HasLockStatus =>
        (IsEditing && !(HasExternalChanges && HasSaveError)) ||
        IsLockedByAnotherUser ||
        (HasExternalChanges && !HasSaveError) ||
        HasLostEditLock ||
        IsDeletedExternally;

    public string LockStatusText
    {
        get
        {
            if (IsDeletedExternally)
            {
                return "⚠ Card eliminata altrove: la bozza non è stata persa";
            }

            if (HasLostEditLock)
            {
                return IsLockedByAnotherUser && !string.IsNullOrWhiteSpace(EditingUserName)
                    ? $"⚠ Il lock è passato a {EditingUserName}: la bozza è conservata"
                    : "⚠ Lock di modifica scaduto: la bozza è conservata";
            }

            if (HasExternalChanges)
            {
                return "⚠ La card è cambiata altrove: annulla per ricaricare";
            }

            if (IsEditing)
            {
                return "🔒 Stai modificando";
            }

            if (IsLockedByAnotherUser)
            {
                return string.IsNullOrWhiteSpace(EditingUserName)
                    ? "🔒 Card in modifica"
                    : $"🔒 {EditingUserName} sta modificando…";
            }

            return string.Empty;
        }
    }

    public CardViewModel(
        Card model,
        IReadOnlyList<PriorityDefinition>? priorities = null,
        IReadOnlyList<PriorityTypeDeadline>? deadlineRules = null,
        DateTimeOffset? displayNow = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        _title = model.Title;
        _notes = model.Notes;
        _originalTitle = model.Title;
        _originalNotes = model.Notes;
        _originalPriorityId = model.PriorityId;
        _displayNow = displayNow ?? DateTimeOffset.Now;
        _editExpectedVersion = model.Version;
        UpdatePriorityCatalog(priorities ?? [], deadlineRules ?? []);
    }

    public void UpdatePriorityCatalog(
        IReadOnlyList<PriorityDefinition> priorities,
        IReadOnlyList<PriorityTypeDeadline> deadlineRules)
    {
        ArgumentNullException.ThrowIfNull(priorities);
        ArgumentNullException.ThrowIfNull(deadlineRules);

        var selectedId = IsEditing ? SelectedPriorityId : Model.PriorityId;
        var cardTypeId = Model.CardTypeId;
        var options = priorities
            .Where(priority => priority.IsActive || priority.Id == selectedId)
            .OrderBy(priority => priority.SortOrder)
            .ThenBy(priority => priority.Id)
            .Select(priority =>
            {
                var effectiveDueHours = PriorityDeadlineCalculator.ResolveDueHours(
                    priority.Id,
                    cardTypeId,
                    priorities,
                    deadlineRules);
                return new CardPriorityOptionViewModel(priority, effectiveDueHours);
            })
            .ToList();

        PriorityOptions.Clear();
        PriorityOptions.Add(CardPriorityOptionViewModel.None);
        foreach (var option in options)
        {
            PriorityOptions.Add(option);
        }

        if (selectedId is long unresolvedId &&
            PriorityOptions.All(option => option.Id != unresolvedId))
        {
            PriorityOptions.Add(CardPriorityOptionViewModel.Unknown(unresolvedId));
        }

        _selectedPriorityOption = PriorityOptions.FirstOrDefault(option => option.Id == selectedId)
            ?? PriorityOptions[0];
        OnPropertyChanged(nameof(SelectedPriorityOption));
        RaiseDerivedStateChanged();
    }

    public void UpdateDisplayNow(DateTimeOffset now)
    {
        _displayNow = now;
        RaiseDerivedStateChanged();
    }

    public void SetDropIndicator(bool afterCard)
    {
        IsDropBeforeVisible = !afterCard;
        IsDropAfterVisible = afterCard;
    }

    public void ClearDropIndicator()
    {
        IsDropBeforeVisible = false;
        IsDropAfterVisible = false;
    }

    public void BeginEdit(CardEditLock editLock, string currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(editLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        if (!string.Equals(editLock.SessionId, currentSessionId, StringComparison.Ordinal))
        {
            ApplyLockState(editLock, currentSessionId);
            return;
        }

        _originalTitle = Model.Title;
        _originalNotes = Model.Notes;
        _originalPriorityId = Model.PriorityId;
        SelectPriority(Model.PriorityId);
        _editExpectedVersion = Model.Version;
        HasExternalChanges = false;
        HasLostEditLock = false;
        IsDeletedExternally = false;
        IsLockedByAnotherUser = false;
        EditingUserName = editLock.UserName;
        IsDeleteConfirmationVisible = false;
        ClearDropIndicator();
        ClearSaveFeedback();
        IsEditing = true;
    }

    public Card CreateEditedModel(string updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        if (!IsTitleValid)
        {
            throw new InvalidOperationException(TitleValidationMessage);
        }

        return new Card
        {
            Id = Model.Id,
            ColumnId = Model.ColumnId,
            StableId = Model.StableId,
            CreatedAtUtc = Model.CreatedAtUtc,
            CreatedAtIsEstimated = Model.CreatedAtIsEstimated,
            PriorityId = SelectedPriorityId,
            CardTypeId = Model.CardTypeId,
            PriorityAssignedAtUtc = Model.PriorityAssignedAtUtc,
            DueAtUtc = Model.DueAtUtc,
            Title = Title.Trim(),
            Notes = Notes,
            SortOrder = Model.SortOrder,
            CreatedBy = Model.CreatedBy,
            UpdatedBy = updatedBy,
            UpdatedAtUtc = Model.UpdatedAtUtc,
            Version = _editExpectedVersion,
        };
    }

    public void BeginSaving()
    {
        IsSaving = true;
        HasSaveError = false;
        HasSaveSuccess = false;
        SaveStatusText = "Salvataggio…";
    }

    public void CompleteSave(Card persistedCard)
    {
        ArgumentNullException.ThrowIfNull(persistedCard);

        CopyPersistedModel(persistedCard, updateEditorText: true);
        EndEditState();
        MarkPersistenceSuccess("Card salvata");
    }

    public void CompleteWithoutChanges()
    {
        EndEditState();
        ClearSaveFeedback();
    }

    public void MarkSaveError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        IsSaving = false;
        HasSaveSuccess = false;
        HasSaveError = true;
        SaveStatusText = message;
    }

    public const string ConcurrencyConflictMessage =
        "Conflitto rilevato: la bozza è conservata. Annulla per ricaricare la versione aggiornata.";

    public void MarkConcurrencyConflict()
    {
        HasExternalChanges = true;
        MarkSaveError(ConcurrencyConflictMessage);
    }

    /// <summary>
    /// Mostra il feedback di persistenza riuscita per qualunque operazione che
    /// modifica la card: creazione, salvataggio, spostamento o riordino.
    /// </summary>
    public void MarkPersistenceSuccess(string statusText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusText);

        IsSaving = false;
        HasSaveError = false;
        HasSaveSuccess = true;
        SaveStatusText = statusText;
    }

    public void CancelEdit()
    {
        Title = Model.Title;
        Notes = Model.Notes;
        SelectPriority(Model.PriorityId);
        EndEditState();
        ClearSaveFeedback();
    }

    public void RequestDeleteConfirmation()
    {
        if (CanRequestDelete)
        {
            ClearSaveFeedback();
            ClearDropIndicator();
            IsDeleteConfirmationVisible = true;
        }
    }

    public void CancelDeleteConfirmation()
    {
        IsDeleteConfirmationVisible = false;
    }

    public void MarkLockLost(CardEditLock? currentLock, string currentSessionId)
    {
        HasLostEditLock = true;
        ApplyLockState(currentLock, currentSessionId, preserveLostState: true);
    }

    public void MarkDeletedExternally()
    {
        ClearDropIndicator();
        IsDeletedExternally = true;
        HasExternalChanges = true;
        IsDeleteConfirmationVisible = false;
    }

    /// <summary>
    /// Aggiorna la card con i dati persistiti. Durante l'editing titolo, note, versione e posizione
    /// restano congelati: il polling segnala il conflitto ma non sovrascrive né sposta la bozza.
    /// </summary>
    public void RefreshFromModel(Card updatedModel)
    {
        ArgumentNullException.ThrowIfNull(updatedModel);

        if (IsEditing)
        {
            if (updatedModel.Version != _editExpectedVersion ||
                !string.Equals(updatedModel.Title, Model.Title, StringComparison.Ordinal) ||
                !string.Equals(updatedModel.Notes, Model.Notes, StringComparison.Ordinal) ||
                updatedModel.ColumnId != Model.ColumnId ||
                updatedModel.PriorityId != Model.PriorityId ||
                updatedModel.CardTypeId != Model.CardTypeId ||
                !string.Equals(
                    updatedModel.PriorityAssignedAtUtc,
                    Model.PriorityAssignedAtUtc,
                    StringComparison.Ordinal) ||
                !string.Equals(updatedModel.DueAtUtc, Model.DueAtUtc, StringComparison.Ordinal))
            {
                HasExternalChanges = true;
            }

            return;
        }

        var persistedStateChanged =
            updatedModel.Version != Model.Version ||
            updatedModel.ColumnId != Model.ColumnId ||
            updatedModel.SortOrder != Model.SortOrder ||
            !string.Equals(updatedModel.Title, Model.Title, StringComparison.Ordinal) ||
            !string.Equals(updatedModel.Notes, Model.Notes, StringComparison.Ordinal) ||
            updatedModel.PriorityId != Model.PriorityId ||
            updatedModel.CardTypeId != Model.CardTypeId ||
            !string.Equals(updatedModel.PriorityAssignedAtUtc, Model.PriorityAssignedAtUtc, StringComparison.Ordinal) ||
            !string.Equals(updatedModel.DueAtUtc, Model.DueAtUtc, StringComparison.Ordinal) ||
            !string.Equals(updatedModel.UpdatedBy, Model.UpdatedBy, StringComparison.Ordinal) ||
            !string.Equals(updatedModel.UpdatedAtUtc, Model.UpdatedAtUtc, StringComparison.Ordinal);

        if (persistedStateChanged)
        {
            ClearSaveFeedback();
        }

        CopyPersistedModel(updatedModel, updateEditorText: true);
        IsDeletedExternally = false;
        HasExternalChanges = false;
    }

    public void ApplyLockState(CardEditLock? editLock, string currentSessionId)
    {
        ApplyLockState(editLock, currentSessionId, preserveLostState: false);
    }

    private void ApplyLockState(
        CardEditLock? editLock,
        string currentSessionId,
        bool preserveLostState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        var ownedByCurrentSession = editLock is not null &&
            string.Equals(editLock.SessionId, currentSessionId, StringComparison.Ordinal);

        if (IsEditing && !ownedByCurrentSession)
        {
            HasLostEditLock = true;
        }
        else if (!preserveLostState && !IsEditing)
        {
            HasLostEditLock = false;
        }

        IsLockedByAnotherUser = editLock is not null && !ownedByCurrentSession;
        EditingUserName = editLock?.UserName;
        if (IsLockedByAnotherUser)
        {
            IsDeleteConfirmationVisible = false;
        }
    }

    private void CopyPersistedModel(Card source, bool updateEditorText)
    {
        Model.ColumnId = source.ColumnId;
        Model.StableId = source.StableId;
        Model.CreatedAtUtc = source.CreatedAtUtc;
        Model.CreatedAtIsEstimated = source.CreatedAtIsEstimated;
        Model.PriorityId = source.PriorityId;
        Model.CardTypeId = source.CardTypeId;
        Model.PriorityAssignedAtUtc = source.PriorityAssignedAtUtc;
        Model.DueAtUtc = source.DueAtUtc;
        Model.Title = source.Title;
        Model.Notes = source.Notes;
        Model.SortOrder = source.SortOrder;
        Model.CreatedBy = source.CreatedBy;
        Model.UpdatedBy = source.UpdatedBy;
        Model.UpdatedAtUtc = source.UpdatedAtUtc;
        Model.Version = source.Version;

        if (updateEditorText)
        {
            _title = source.Title;
            _notes = source.Notes;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Notes));
        }

        SelectPriority(source.PriorityId);
        OnPropertyChanged(nameof(ColumnId));
        OnPropertyChanged(nameof(Author));
        RaiseDerivedStateChanged();
    }

    private void EndEditState()
    {
        _originalTitle = Title;
        _originalNotes = Notes;
        _originalPriorityId = Model.PriorityId;
        SelectPriority(Model.PriorityId);
        _editExpectedVersion = Model.Version;
        IsSaving = false;
        IsEditing = false;
        HasExternalChanges = false;
        HasLostEditLock = false;
        IsDeletedExternally = false;
        IsLockedByAnotherUser = false;
        EditingUserName = null;
        RaiseDerivedStateChanged();
    }

    private void SelectPriority(long? priorityId)
    {
        var selected = PriorityOptions.FirstOrDefault(option => option.Id == priorityId)
            ?? PriorityOptions.FirstOrDefault(option => option.IsNone);
        if (!ReferenceEquals(_selectedPriorityOption, selected))
        {
            _selectedPriorityOption = selected;
            OnPropertyChanged(nameof(SelectedPriorityOption));
        }
    }

    private DateTimeOffset? GetDisplayedDueAt()
    {
        if (SelectedPriorityOption is null || SelectedPriorityOption.IsNone)
        {
            return null;
        }

        if (IsEditing && SelectedPriorityId != Model.PriorityId)
        {
            return SelectedPriorityOption.EffectiveDueHours is int hours
                ? PriorityDeadlineCalculator.CalculateDueAt(_displayNow.ToUniversalTime(), hours)
                : null;
        }

        return DateTimeOffset.TryParse(
            Model.DueAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dueAt)
            ? dueAt
            : null;
    }

    private DateTimeOffset? GetLastSavedAt()
    {
        var timestamp = string.IsNullOrWhiteSpace(Model.UpdatedAtUtc)
            ? Model.CreatedAtUtc
            : Model.UpdatedAtUtc;
        return DateTimeOffset.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var savedAt)
            ? savedAt
            : null;
    }

    private static string FormatRelativeTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || elapsed.TotalMinutes < 1)
        {
            return "adesso";
        }

        if (elapsed.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            return minutes == 1 ? "1 minuto fa" : $"{minutes} minuti fa";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return hours == 1 ? "1 ora fa" : $"{hours} ore fa";
        }

        var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
        return days == 1 ? "1 giorno fa" : $"{days} giorni fa";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return "meno di un minuto";
        }

        if (duration.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
            return minutes == 1 ? "1 minuto" : $"{minutes} minuti";
        }

        if (duration.TotalDays < 2)
        {
            var hours = Math.Max(1, (int)Math.Ceiling(duration.TotalHours));
            return hours == 1 ? "1 ora" : $"{hours} ore";
        }

        var days = Math.Max(1, (int)Math.Ceiling(duration.TotalDays));
        return days == 1 ? "1 giorno" : $"{days} giorni";
    }

    private void ClearSaveFeedbackAfterUserChange()
    {
        if (IsEditing && !IsSaving && HasSaveStatus)
        {
            ClearSaveFeedback();
        }
    }

    private void ClearSaveFeedback()
    {
        IsSaving = false;
        HasSaveError = false;
        HasSaveSuccess = false;
        SaveStatusText = null;
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(IsNotEditing));
        OnPropertyChanged(nameof(SelectedPriorityId));
        OnPropertyChanged(nameof(HasPriority));
        OnPropertyChanged(nameof(PriorityBadgeCode));
        OnPropertyChanged(nameof(PriorityBadgeText));
        OnPropertyChanged(nameof(PriorityToolTipText));
        OnPropertyChanged(nameof(HasDueStatus));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(DueStatusText));
        OnPropertyChanged(nameof(DueExactText));
        OnPropertyChanged(nameof(HasLastSavedAt));
        OnPropertyChanged(nameof(LastSavedRelativeText));
        OnPropertyChanged(nameof(LastSavedExactText));
        OnPropertyChanged(nameof(LastSavedToolTipText));
        OnPropertyChanged(nameof(ShowDirtyPersistenceState));
        OnPropertyChanged(nameof(ShowSavingPersistenceState));
        OnPropertyChanged(nameof(ShowErrorPersistenceState));
        OnPropertyChanged(nameof(ShowSavedPersistenceState));
        OnPropertyChanged(nameof(TitleLengthText));
        OnPropertyChanged(nameof(IsTitleValid));
        OnPropertyChanged(nameof(HasTitleValidationError));
        OnPropertyChanged(nameof(TitleValidationMessage));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEditorReadOnly));
        OnPropertyChanged(nameof(CanDrag));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancelEdit));
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(CanRequestDelete));
        OnPropertyChanged(nameof(ShowDeleteButton));
        OnPropertyChanged(nameof(HasLockStatus));
        OnPropertyChanged(nameof(LockStatusText));
    }
}
