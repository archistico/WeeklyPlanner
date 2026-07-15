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
        Assert.True(viewModel.CanEditDraft);
        Assert.True(viewModel.CanSave);
        Assert.Equal("OriginaleX", viewModel.Title);
        Assert.Equal("Originale", viewModel.Model.Title);
    }

    [Fact]
    public void MarkConcurrencyConflict_preserves_draft_and_shows_a_single_error_state()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Bozza locale";

        viewModel.MarkConcurrencyConflict();

        Assert.Equal("Bozza locale", viewModel.Title);
        Assert.True(viewModel.HasExternalChanges);
        Assert.True(viewModel.HasSaveError);
        Assert.Equal(CardViewModel.ConcurrencyConflictMessage, viewModel.SaveStatusText);
        Assert.False(viewModel.HasLockStatus);
        Assert.False(viewModel.CanSave);
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
    public void CreateEditedModel_preserves_schema_v4_metadata()
    {
        var model = new Card
        {
            Id = 1,
            ColumnId = 0,
            StableId = "stable-card",
            CreatedAtUtc = "2026-07-14T18:00:00.0000000Z",
            CreatedAtIsEstimated = true,
            PriorityId = 3,
            CardTypeId = 5,
            PriorityAssignedAtUtc = "2026-07-14T18:00:00.0000000Z",
            DueAtUtc = "2026-09-12T18:00:00.0000000Z",
            Title = "Originale",
            Notes = "Note originali",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
            UpdatedAtUtc = "2026-07-14T18:00:00.0000000Z",
            Version = 1,
        };
        var viewModel = new CardViewModel(model);
        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Aggiornata";

        var edited = viewModel.CreateEditedModel("Emilie");

        Assert.Equal(model.StableId, edited.StableId);
        Assert.Equal(model.CreatedAtUtc, edited.CreatedAtUtc);
        Assert.True(edited.CreatedAtIsEstimated);
        Assert.Equal(model.PriorityId, edited.PriorityId);
        Assert.Equal(model.CardTypeId, edited.CardTypeId);
        Assert.Equal(model.PriorityAssignedAtUtc, edited.PriorityAssignedAtUtc);
        Assert.Equal(model.DueAtUtc, edited.DueAtUtc);
    }

    [Fact]
    public void Priority_is_a_shared_draft_field_and_none_is_an_explicit_option()
    {
        var priorities = CreatePriorities();
        var viewModel = new CardViewModel(
            CreateCard(priorityId: null, cardTypeId: 5),
            priorities,
            CreateDeadlineRules(),
            new DateTimeOffset(2026, 7, 14, 18, 30, 0, TimeSpan.Zero));

        Assert.True(viewModel.IsNotEditing);
        Assert.Equal("Nessuna priorità", viewModel.PriorityBadgeText);
        Assert.True(Assert.Single(viewModel.PriorityOptions, option => option.IsNone).IsNone);

        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.SelectedPriorityOption = viewModel.PriorityOptions.Single(option => option.Id == 3);

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanSave);
        Assert.Equal("Scade tra 60 giorni", viewModel.DueStatusText);
        Assert.Equal(3L, viewModel.CreateEditedModel("Emilie").PriorityId);

        viewModel.CancelEdit();

        Assert.Null(viewModel.SelectedPriorityId);
        Assert.False(viewModel.IsDirty);
        Assert.Equal("Nessuna priorità", viewModel.PriorityBadgeText);
    }

    [Fact]
    public void Read_only_priority_badge_exposes_name_due_date_and_inactive_current_value()
    {
        var priorities = CreatePriorities();
        priorities.Add(new PriorityDefinition
        {
            Id = 9,
            Code = "X",
            Name = "Legacy",
            Description = "Priorità non più assegnabile.",
            DefaultDueHours = 24,
            SortOrder = 9,
            IsActive = false,
        });
        var viewModel = new CardViewModel(
            CreateCard(
                priorityId: 9,
                cardTypeId: 5,
                dueAtUtc: "2026-07-15T18:30:00.0000000Z"),
            priorities,
            CreateDeadlineRules(),
            new DateTimeOffset(2026, 7, 14, 18, 30, 0, TimeSpan.Zero));

        Assert.True(viewModel.IsNotEditing);
        Assert.Equal("X", viewModel.PriorityBadgeCode);
        Assert.Equal("Legacy", viewModel.PriorityBadgeText);
        Assert.Equal("Scade tra 24 ore", viewModel.DueStatusText);
        Assert.Contains("Priorità non più assegnabile", viewModel.PriorityToolTipText);
        Assert.Contains(viewModel.DueExactText!, viewModel.PriorityToolTipText);
        Assert.Contains(viewModel.PriorityOptions, option => option.Id == 9 && !option.IsActive);
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
        Assert.Equal("Card salvata", viewModel.SaveStatusText);
        Assert.False(viewModel.IsEditing);
    }

    [Theory]
    [InlineData("Card inserita")]
    [InlineData("Card salvata")]
    [InlineData("Card spostata")]
    [InlineData("Ordine aggiornato")]
    public void Persistence_feedback_supports_every_card_modification(string statusText)
    {
        var viewModel = CreateViewModel();

        viewModel.MarkPersistenceSuccess(statusText);

        Assert.True(viewModel.HasSaveSuccess);
        Assert.False(viewModel.HasSaveError);
        Assert.False(viewModel.IsSaving);
        Assert.Equal(statusText, viewModel.SaveStatusText);
    }

    [Fact]
    public void External_reorder_clears_a_previous_persistence_success()
    {
        var viewModel = CreateViewModel();
        viewModel.MarkPersistenceSuccess("Card salvata");

        viewModel.RefreshFromModel(new Card
        {
            Id = 1,
            ColumnId = 0,
            Title = "Originale",
            Notes = "Note originali",
            SortOrder = 1,
            CreatedBy = "Emilie",
            UpdatedBy = "Alice",
            UpdatedAtUtc = "2026-07-14T18:01:00.0000000Z",
            Version = 1,
        });

        Assert.False(viewModel.HasSaveSuccess);
        Assert.Null(viewModel.SaveStatusText);
    }

    [Theory]
    [InlineData(0, "adesso")]
    [InlineData(45, "adesso")]
    [InlineData(60, "1 minuto fa")]
    [InlineData(300, "5 minuti fa")]
    [InlineData(3600, "1 ora fa")]
    [InlineData(10800, "3 ore fa")]
    [InlineData(86400, "1 giorno fa")]
    [InlineData(259200, "3 giorni fa")]
    public void Last_saved_text_uses_the_required_relative_buckets(
        int elapsedSeconds,
        string expected)
    {
        var savedAt = new DateTimeOffset(2026, 7, 14, 18, 0, 0, TimeSpan.Zero);
        var model = CreateCard(priorityId: null, cardTypeId: 5);
        model.UpdatedAtUtc = savedAt.ToString("O");
        var viewModel = new CardViewModel(
            model,
            displayNow: savedAt.AddSeconds(elapsedSeconds));

        Assert.Equal(expected, viewModel.LastSavedRelativeText);
        Assert.True(viewModel.ShowSavedPersistenceState);
        Assert.Contains("14/07/2026", viewModel.LastSavedToolTipText);
        Assert.Contains("Emilie", viewModel.LastSavedToolTipText);
    }

    [Fact]
    public void Last_saved_timestamp_falls_back_to_creation_time_for_legacy_rows()
    {
        var createdAt = new DateTimeOffset(2026, 7, 14, 16, 0, 0, TimeSpan.Zero);
        var model = CreateCard(priorityId: null, cardTypeId: 5);
        model.CreatedAtUtc = createdAt.ToString("O");
        model.UpdatedAtUtc = string.Empty;
        var viewModel = new CardViewModel(
            model,
            displayNow: createdAt.AddHours(2));

        Assert.True(viewModel.HasLastSavedAt);
        Assert.Equal("2 ore fa", viewModel.LastSavedRelativeText);
    }

    [Fact]
    public void Persistence_indicator_distinguishes_dirty_saving_error_and_saved_states()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.ShowSavedPersistenceState);
        Assert.False(viewModel.ShowDirtyPersistenceState);

        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");
        viewModel.Title = "Bozza";

        Assert.True(viewModel.ShowDirtyPersistenceState);
        Assert.False(viewModel.ShowSavedPersistenceState);

        viewModel.BeginSaving();

        Assert.True(viewModel.ShowSavingPersistenceState);
        Assert.False(viewModel.ShowDirtyPersistenceState);

        viewModel.MarkSaveError("Errore di prova");

        Assert.True(viewModel.ShowErrorPersistenceState);
        Assert.False(viewModel.ShowSavingPersistenceState);
        Assert.False(viewModel.ShowSavedPersistenceState);
    }

    [Fact]
    public void External_refresh_replaces_the_timestamp_used_by_the_relative_indicator()
    {
        var now = new DateTimeOffset(2026, 7, 14, 18, 30, 0, TimeSpan.Zero);
        var model = CreateCard(priorityId: null, cardTypeId: 5);
        model.UpdatedAtUtc = now.AddHours(-2).ToString("O");
        var viewModel = new CardViewModel(model, displayNow: now);

        Assert.Equal("2 ore fa", viewModel.LastSavedRelativeText);

        var updated = CreateCard(priorityId: null, cardTypeId: 5);
        updated.Version = 2;
        updated.UpdatedBy = "Alice";
        updated.UpdatedAtUtc = now.AddMinutes(-3).ToString("O");
        viewModel.RefreshFromModel(updated);

        Assert.Equal("3 minuti fa", viewModel.LastSavedRelativeText);
        Assert.Contains("Alice", viewModel.LastSavedToolTipText);
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

    private static List<PriorityDefinition> CreatePriorities() =>
    [
        new PriorityDefinition
        {
            Id = 1,
            Code = "U",
            Name = "Urgente",
            Description = "Da eseguire rapidamente.",
            DefaultDueHours = 72,
            SortOrder = 0,
            IsActive = true,
        },
        new PriorityDefinition
        {
            Id = 3,
            Code = "D",
            Name = "Differibile",
            Description = "Gestibile nel medio periodo.",
            DefaultDueHours = 720,
            SortOrder = 1,
            IsActive = true,
        },
    ];

    private static IReadOnlyList<PriorityTypeDeadline> CreateDeadlineRules() =>
    [
        new PriorityTypeDeadline
        {
            PriorityId = 3,
            CardTypeId = 5,
            DueHours = 1440,
            Version = 1,
        },
    ];

    private static Card CreateCard(
        long? priorityId,
        long? cardTypeId,
        string? dueAtUtc = null) => new()
    {
        Id = 1,
        ColumnId = 0,
        PriorityId = priorityId,
        CardTypeId = cardTypeId,
        PriorityAssignedAtUtc = priorityId is null
            ? null
            : "2026-07-14T18:30:00.0000000Z",
        DueAtUtc = dueAtUtc,
        Title = "Originale",
        Notes = "Note originali",
        SortOrder = 0,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-14T18:00:00.0000000Z",
        Version = 1,
    };

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
