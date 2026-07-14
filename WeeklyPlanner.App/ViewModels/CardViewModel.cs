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

    public Card Model { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
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
                RaiseDerivedStateChanged();
            }
        }
    }

    public long ColumnId => Model.ColumnId;

    public string? Author => Model.CreatedBy;

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

    public bool IsDirty =>
        IsEditing &&
        (!string.Equals(Title, _originalTitle, StringComparison.Ordinal) ||
         !string.Equals(Notes, _originalNotes, StringComparison.Ordinal));

    public bool IsEditorReadOnly =>
        !IsEditing ||
        IsLockedByAnotherUser ||
        IsDeletedExternally ||
        HasLostEditLock ||
        HasExternalChanges;

    public bool CanDrag =>
        !IsEditing && !IsLockedByAnotherUser && !IsDeletedExternally;

    public bool CanSave =>
        IsEditing && IsDirty && !HasExternalChanges && !HasLostEditLock && !IsDeletedExternally;

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

    public void BeginEdit(CardEditLock editLock, string currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(editLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        if (!string.Equals(editLock.SessionId, currentSessionId, StringComparison.Ordinal))
        {
            ApplyLockState(editLock, currentSessionId);
            return;
        }

        // I campi restano read-only finché il lease non è acquisito. La baseline dirty deve
        // quindi coincidere sempre con il valore persistito all'ingresso in modifica.
        _originalTitle = Model.Title;
        _originalNotes = Model.Notes;
        _editExpectedVersion = Model.Version;
        HasExternalChanges = false;
        HasLostEditLock = false;
        IsDeletedExternally = false;
        IsLockedByAnotherUser = false;
        EditingUserName = editLock.UserName;
        IsEditing = true;
    }

    public Card CreateEditedModel(string updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        return new Card
        {
            Id = Model.Id,
            ColumnId = Model.ColumnId,
            Title = Title,
            Notes = Notes,
            SortOrder = Model.SortOrder,
            CreatedBy = Model.CreatedBy,
            UpdatedBy = updatedBy,
            UpdatedAtUtc = Model.UpdatedAtUtc,
            Version = _editExpectedVersion,
        };
    }

    public void CompleteSave(Card persistedCard)
    {
        ArgumentNullException.ThrowIfNull(persistedCard);

        CopyPersistedModel(persistedCard, updateEditorText: true);
        EndEditState();
    }

    public void CompleteWithoutChanges()
    {
        EndEditState();
    }

    public void CancelEdit()
    {
        Title = Model.Title;
        Notes = Model.Notes;
        EndEditState();
    }

    public void MarkLockLost(CardEditLock? currentLock, string currentSessionId)
    {
        HasLostEditLock = true;
        ApplyLockState(currentLock, currentSessionId, preserveLostState: true);
    }

    public void MarkDeletedExternally()
    {
        IsDeletedExternally = true;
        HasExternalChanges = true;
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
            Title = source.Title;
            Notes = source.Notes;
        }

        OnPropertyChanged(nameof(ColumnId));
        OnPropertyChanged(nameof(Author));
    }

    private void EndEditState()
    {
        _originalTitle = Title;
        _originalNotes = Notes;
        _editExpectedVersion = Model.Version;
        IsEditing = false;
        HasExternalChanges = false;
        HasLostEditLock = false;
        IsDeletedExternally = false;
        IsLockedByAnotherUser = false;
        EditingUserName = null;
        RaiseDerivedStateChanged();
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEditorReadOnly));
        OnPropertyChanged(nameof(CanDrag));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasLockStatus));
        OnPropertyChanged(nameof(LockStatusText));
    }
}
