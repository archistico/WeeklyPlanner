using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardViewModelTests
{
    [Fact]
    public void Editor_is_read_only_until_the_current_session_acquires_the_lock()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.IsEditorReadOnly);

        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");

        Assert.True(viewModel.IsEditing);
        Assert.False(viewModel.IsEditorReadOnly);
    }

    [Fact]
    public void BeginEdit_allows_a_local_draft_without_changing_the_persisted_model()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");

        viewModel.Title = "OriginaleX";

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanSave);
        Assert.Equal("OriginaleX", viewModel.Title);
        Assert.Equal("Originale", viewModel.Model.Title);
    }

    [Fact]
    public void RefreshFromModel_does_not_overwrite_draft_while_editing()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Bozza locale";
        viewModel.Notes = "Testo non ancora salvato";

        viewModel.RefreshFromModel(new Card
        {
            Id = 1,
            ColumnId = 0,
            Title = "Titolo remoto",
            Notes = "Testo remoto",
            SortOrder = 0,
            CreatedBy = "Emilie",
            UpdatedBy = "Alice",
            UpdatedAtUtc = "2026-07-14T18:01:00.0000000Z",
            Version = 2,
        });

        Assert.Equal("Bozza locale", viewModel.Title);
        Assert.Equal("Testo non ancora salvato", viewModel.Notes);
        Assert.True(viewModel.HasExternalChanges);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void RefreshFromModel_updates_editor_when_card_is_not_being_edited()
    {
        var viewModel = CreateViewModel();

        viewModel.RefreshFromModel(new Card
        {
            Id = 1,
            ColumnId = 1,
            Title = "Aggiornata",
            Notes = "Nuove note",
            SortOrder = 3,
            CreatedBy = "Emilie",
            UpdatedBy = "Alice",
            UpdatedAtUtc = "2026-07-14T18:01:00.0000000Z",
            Version = 2,
        });

        Assert.Equal("Aggiornata", viewModel.Title);
        Assert.Equal("Nuove note", viewModel.Notes);
        Assert.Equal(1, viewModel.ColumnId);
        Assert.Equal(2, viewModel.Model.Version);
    }

    [Fact]
    public void CancelEdit_restores_persisted_values_and_clears_edit_state()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Bozza";
        viewModel.Notes = "Bozza note";

        viewModel.CancelEdit();

        Assert.Equal("Originale", viewModel.Title);
        Assert.Equal("Note originali", viewModel.Notes);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void ApplyLockState_exposes_other_user_and_makes_editor_read_only()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyLockState(CreateLock("session-b", "Alice"), "session-a");

        Assert.True(viewModel.IsLockedByAnotherUser);
        Assert.True(viewModel.IsEditorReadOnly);
        Assert.Contains("Alice", viewModel.LockStatusText);
        Assert.False(viewModel.CanDrag);
    }


    [Fact]
    public void Blank_title_is_invalid_and_cannot_be_saved()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");

        viewModel.Title = "   ";

        Assert.False(viewModel.IsTitleValid);
        Assert.True(viewModel.HasTitleValidationError);
        Assert.False(viewModel.CanSave);
        Assert.Contains("obbligatorio", viewModel.TitleValidationMessage);
    }

    [Fact]
    public void CreateEditedModel_trims_a_valid_title()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "  Titolo pulito  ";

        var edited = viewModel.CreateEditedModel("Emilie");

        Assert.Equal("Titolo pulito", edited.Title);
    }

    [Fact]
    public void Save_feedback_exposes_saving_success_and_error_states()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Aggiornata";

        viewModel.BeginSaving();
        Assert.True(viewModel.IsSaving);
        Assert.Equal("Salvataggio…", viewModel.SaveStatusText);
        Assert.True(viewModel.IsEditorReadOnly);

        viewModel.MarkSaveError("Errore di prova");
        Assert.True(viewModel.HasSaveError);
        Assert.False(viewModel.IsSaving);
        Assert.Equal("Errore di prova", viewModel.SaveStatusText);
    }

    [Fact]
    public void CompleteSave_keeps_saved_feedback_during_same_state_refresh()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Salvata";
        var persisted = viewModel.CreateEditedModel("Emilie");
        persisted.Version = 2;

        viewModel.CompleteSave(persisted);
        viewModel.RefreshFromModel(persisted);

        Assert.True(viewModel.HasSaveSuccess);
        Assert.Equal("Salvata", viewModel.SaveStatusText);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public void Drop_indicator_is_exclusive_and_can_be_cleared()
    {
        var viewModel = CreateViewModel();

        viewModel.SetDropIndicator(afterCard: false);
        Assert.True(viewModel.IsDropBeforeVisible);
        Assert.False(viewModel.IsDropAfterVisible);

        viewModel.SetDropIndicator(afterCard: true);
        Assert.False(viewModel.IsDropBeforeVisible);
        Assert.True(viewModel.IsDropAfterVisible);

        viewModel.ClearDropIndicator();
        Assert.False(viewModel.IsDropBeforeVisible);
        Assert.False(viewModel.IsDropAfterVisible);
    }

    [Fact]
    public void Delete_confirmation_is_available_only_when_card_is_not_being_edited_or_locked()
    {
        var viewModel = CreateViewModel();

        viewModel.RequestDeleteConfirmation();
        Assert.True(viewModel.IsDeleteConfirmationVisible);
        Assert.False(viewModel.CanDrag);

        viewModel.CancelDeleteConfirmation();
        viewModel.ApplyLockState(CreateLock("session-b", "Alice"), "session-a");
        viewModel.RequestDeleteConfirmation();

        Assert.False(viewModel.IsDeleteConfirmationVisible);
        Assert.False(viewModel.CanRequestDelete);
    }

    private static CardViewModel CreateViewModel() => new(new Card
    {
        Id = 1,
        ColumnId = 0,
        Title = "Originale",
        Notes = "Note originali",
        SortOrder = 0,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-14T18:00:00.0000000Z",
        Version = 1,
    });

    private static CardEditLock CreateLock(string sessionId, string userName) => new()
    {
        CardId = 1,
        SessionId = sessionId,
        UserName = userName,
        MachineName = "PC",
        AcquiredAtUtc = "2026-07-14T18:00:00.0000000Z",
        LastHeartbeatUtc = "2026-07-14T18:00:00.0000000Z",
        ExpiresAtUtc = "2026-07-14T18:00:30.0000000Z",
    };
}
