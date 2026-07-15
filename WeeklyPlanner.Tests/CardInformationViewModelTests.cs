using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardInformationViewModelTests
{
    [Fact]
    public async Task Load_exposes_card_details_active_lock_and_descriptive_history()
    {
        var events = new BoardViewModelTestDoubles.StubCardEventRepository();
        events.Items.AddRange(
        [
            CreateEvent(2, CardEventTypes.Moved, "Card spostata da Generica / BACKLOG a SQL / TODO."),
            CreateEvent(1, CardEventTypes.Created, "Card creata."),
        ]);
        var locks = new BoardViewModelTestDoubles.StubCardEditLockRepository();
        locks.ActiveLocks.Add(new CardEditLock
        {
            CardId = 7,
            SessionId = "current-session",
            UserName = "Emilie",
            MachineName = "PC-TEST",
            AcquiredAtUtc = "2026-07-15T10:00:00.0000000Z",
            LastHeartbeatUtc = "2026-07-15T10:00:10.0000000Z",
            ExpiresAtUtc = "2026-07-15T10:00:30.0000000Z",
        });
        var viewModel = new CardInformationViewModel(
            CreateCard(),
            "TODO",
            "SQL",
            "U — Urgente",
            events,
            locks,
            "current-session");

        await viewModel.LoadAsync();

        Assert.Equal("Card informativa", viewModel.Title);
        Assert.Equal("TODO", viewModel.WorkflowStateName);
        Assert.Equal("SQL", viewModel.CardTypeName);
        Assert.Equal("U — Urgente", viewModel.PriorityText);
        Assert.Contains("Emilie", viewModel.LockStatusText, StringComparison.Ordinal);
        Assert.Contains("PC-TEST", viewModel.LockDetailsText!, StringComparison.Ordinal);
        Assert.Equal(2, viewModel.History.Count);
        Assert.Equal("Movimento", viewModel.History[0].EventTypeText);
        Assert.True(viewModel.History[0].IsMovement);
        Assert.Contains("Generica / BACKLOG", viewModel.History[0].Summary, StringComparison.Ordinal);
        Assert.False(viewModel.HasMoreHistory);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task History_is_loaded_in_bounded_pages_using_the_last_event_id()
    {
        var events = new BoardViewModelTestDoubles.StubCardEventRepository();
        for (var id = 1; id <= CardInformationViewModel.HistoryPageSize + 1; id++)
        {
            events.Items.Add(CreateEvent(id, CardEventTypes.Updated, $"Evento {id}."));
        }

        var viewModel = new CardInformationViewModel(
            CreateCard(),
            "TODO",
            "SQL",
            "Nessuna priorità",
            events,
            new BoardViewModelTestDoubles.StubCardEditLockRepository(),
            "current-session");

        await viewModel.LoadAsync();

        Assert.Equal(CardInformationViewModel.HistoryPageSize, viewModel.History.Count);
        Assert.True(viewModel.HasMoreHistory);
        Assert.Equal(2, viewModel.History[^1].Id);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(CardInformationViewModel.HistoryPageSize + 1, viewModel.History.Count);
        Assert.False(viewModel.HasMoreHistory);
        Assert.Equal(2, events.Requests.Count);
        Assert.Equal(2L, events.Requests[1].BeforeEventId!.Value);
    }

    [Fact]
    public async Task Missing_lock_and_empty_history_are_reported_without_error()
    {
        var viewModel = new CardInformationViewModel(
            CreateCard(),
            "TODO",
            "SQL",
            "Nessuna priorità",
            new BoardViewModelTestDoubles.StubCardEventRepository(),
            new BoardViewModelTestDoubles.StubCardEditLockRepository(),
            "current-session");

        await viewModel.LoadAsync();

        Assert.Equal("Nessun lock di modifica attivo", viewModel.LockStatusText);
        Assert.True(viewModel.IsHistoryEmpty);
        Assert.False(viewModel.HasHistory);
        Assert.False(viewModel.HasError);
    }

    private static Card CreateCard() => new()
    {
        Id = 7,
        ColumnId = 1,
        StableId = "card-stable-id",
        CreatedAtUtc = "2026-07-14T08:00:00.0000000Z",
        PriorityId = 1,
        CardTypeId = 5,
        PriorityAssignedAtUtc = "2026-07-14T08:00:00.0000000Z",
        DueAtUtc = "2026-07-17T08:00:00.0000000Z",
        Title = "Card informativa",
        Notes = "Note",
        SortOrder = 0,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-15T09:00:00.0000000Z",
        Version = 3,
    };

    private static CardEvent CreateEvent(long id, string eventType, string summary) => new()
    {
        Id = id,
        CardStableId = "card-stable-id",
        CardId = 7,
        EventType = eventType,
        OccurredAtUtc = "2026-07-15T09:00:00.0000000Z",
        UserName = "Emilie",
        SessionId = "current-session",
        MachineName = "PC-TEST",
        Summary = summary,
        DataJson = "{}",
        FormatVersion = 1,
    };
}
