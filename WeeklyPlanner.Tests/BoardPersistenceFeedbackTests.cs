using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardPersistenceFeedbackTests
{
    [Fact]
    public async Task Add_card_marks_the_created_card_as_persisted()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        var column = Assert.Single(context.ViewModel.Columns);

        await context.ViewModel.AddCardCommand.ExecuteAsync(column);

        var card = Assert.Single(column.Cards);
        Assert.True(card.HasSaveSuccess);
        Assert.Equal("Card inserita", card.SaveStatusText);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Reorder_in_the_same_column_marks_the_moved_card()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Cards.Items.Add(CreateCard(1, 0, 0, "Prima"));
        context.Cards.Items.Add(CreateCard(2, 0, 1, "Seconda"));
        await context.ViewModel.StartAsync();
        var column = Assert.Single(context.ViewModel.Columns);
        var card = column.Cards.Single(item => item.Model.Id == 1);

        await context.ViewModel.MoveCardAsync(card, column, targetIndex: 2);

        Assert.True(card.HasSaveSuccess);
        Assert.Equal("Ordine aggiornato", card.SaveStatusText);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Move_to_another_column_marks_the_moved_card()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Columns.Items =
        [
            new Column { Id = 0, Name = "Backlog", SortOrder = 0 },
            new Column { Id = 1, Name = "Lunedì", SortOrder = 1 },
        ];
        context.Cards.Items.Add(CreateCard(1, 0, 0, "Da spostare"));
        await context.ViewModel.StartAsync();
        var source = context.ViewModel.Columns.Single(column => column.Id == 0);
        var target = context.ViewModel.Columns.Single(column => column.Id == 1);
        var card = Assert.Single(source.Cards);

        await context.ViewModel.MoveCardAsync(card, target, targetIndex: 0);

        Assert.True(card.HasSaveSuccess);
        Assert.Equal("Card spostata", card.SaveStatusText);
        Assert.Contains(card, target.Cards);

        await context.ViewModel.DisposeAsync();
    }

    private static Card CreateCard(long id, long columnId, int sortOrder, string title) => new()
    {
        Id = id,
        ColumnId = columnId,
        Title = title,
        SortOrder = sortOrder,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-14T18:00:00.0000000Z",
        Version = 1,
    };
}
