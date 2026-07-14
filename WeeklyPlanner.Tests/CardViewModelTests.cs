using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardViewModelTests
{
    [Fact]
    public void BeginEdit_keeps_characters_entered_while_lock_was_being_acquired_as_dirty()
    {
        var viewModel = CreateViewModel();
        viewModel.Title = "OriginaleX";

        viewModel.BeginEdit(CreateLock("session-a", "Emilie"), "session-a");

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanSave);
        Assert.Equal("OriginaleX", viewModel.Title);
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
