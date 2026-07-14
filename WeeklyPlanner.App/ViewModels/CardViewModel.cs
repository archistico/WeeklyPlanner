using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class CardViewModel : ViewModelBase
{
    private string _title;
    private string? _notes;
    private string _originalTitle;
    private string? _originalNotes;
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
        private set => SetProperty(ref _hasSaveError, value);
    }

    public bool HasSaveSuccess
    {
        get => _hasSaveSuccess;
        private set => SetProperty(ref _hasSaveSuccess, value);
    }

    public bool HasSaveStatus => !string.IsNullOrWhiteSpace(SaveStatusText);

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
         !string.Equals(Notes, _originalNotes, StringComparison.Ordinal));

    public bool IsEditorReadOnly =>
        !IsEditing ||
        IsSaving ||
        IsLockedByAnotherUser ||
        IsDeletedExternally ||
        HasLostEditLock ||
        HasExternalChanges;

    public bool CanDrag =>
        !IsEditing &&
        !IsLockedByAnotherUser &&
        !IsDeletedExternally &&
        !IsDeleteConfirmationVisible;

    public bool CanCancelEdit => IsEditing && !IsSaving;

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
        IsEditing || IsLockedByAnotherUser || HasExternalChanges || HasLostEditLock || IsDeletedExternally;

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

    public CardViewModel(Card model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        _title = model.Title;
        _notes = model.Notes;
        _originalTitle = model.Title;
        _originalNotes = model.Notes;
        _editExpectedVersion = model.Version;
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
                updatedModel.ColumnId != Model.ColumnId)
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

        OnPropertyChanged(nameof(ColumnId));
        OnPropertyChanged(nameof(Author));
        RaiseDerivedStateChanged();
    }

    private void EndEditState()
    {
        _originalTitle = Title;
        _originalNotes = Notes;
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
        OnPropertyChanged(nameof(TitleLengthText));
        OnPropertyChanged(nameof(IsTitleValid));
        OnPropertyChanged(nameof(HasTitleValidationError));
        OnPropertyChanged(nameof(TitleValidationMessage));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEditorReadOnly));
        OnPropertyChanged(nameof(CanDrag));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancelEdit));
        OnPropertyChanged(nameof(CanRequestDelete));
        OnPropertyChanged(nameof(ShowDeleteButton));
        OnPropertyChanged(nameof(HasLockStatus));
        OnPropertyChanged(nameof(LockStatusText));
    }
}
