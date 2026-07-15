using WeeklyPlanner.App.ViewModels;
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
        var column = Assert.IsType<ColumnViewModel>(context.ViewModel.BacklogColumn);

        await context.ViewModel.AddCardCommand.ExecuteAsync(column);

        var card = Assert.Single(
            context.ViewModel.Swimlanes.Single(lane => lane.IsGeneric).Backlog.Cards);
        Assert.True(card.HasSaveSuccess);
        Assert.Equal("Card inserita", card.SaveStatusText);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Header_actions_create_a_generic_card_in_each_workflow_state()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.Priorities =
        [
            new PriorityDefinition
            {
                Id = 7,
                Code = "N",
                Name = "Normale",
                DefaultDueHours = 48,
                SortOrder = 0,
                IsActive = true,
                IsDefault = true,
            },
        ];
        await context.ViewModel.StartAsync();

        var columns = new[]
        {
            Assert.IsType<ColumnViewModel>(context.ViewModel.BacklogColumn),
            Assert.IsType<ColumnViewModel>(context.ViewModel.TodoColumn),
            Assert.IsType<ColumnViewModel>(context.ViewModel.InProgressColumn),
            Assert.IsType<ColumnViewModel>(context.ViewModel.TestingColumn),
            Assert.IsType<ColumnViewModel>(context.ViewModel.DoneColumn),
        };

        foreach (var column in columns)
        {
            await context.ViewModel.AddCardCommand.ExecuteAsync(column);
        }

        var generic = context.ViewModel.Swimlanes.Single(lane => lane.IsGeneric);
        Assert.Single(generic.Backlog.Cards);
        Assert.Single(generic.Todo.Cards);
        Assert.Single(generic.InProgress.Cards);
        Assert.Single(generic.Testing.Cards);
        Assert.Single(generic.Done.Cards);
        Assert.All(context.Cards.Items, card => Assert.Equal(7L, card.PriorityId));

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Reorder_in_the_same_column_marks_the_moved_card()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Cards.Items.Add(CreateCard(1, 0, 0, "Prima"));
        context.Cards.Items.Add(CreateCard(2, 0, 1, "Seconda"));
        await context.ViewModel.StartAsync();
        var cell = context.ViewModel.Swimlanes.Single(lane => lane.IsGeneric).Backlog;
        var card = cell.Cards.Single(item => item.Model.Id == 1);

        await context.ViewModel.MoveCardAsync(card, cell, targetCellIndex: 2);

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
            new Column
            {
                Id = 0,
                Name = "BACKLOG",
                SortOrder = 0,
                SystemKey = WorkflowColumnKeys.Backlog,
                IsSystem = true,
            },
            new Column
            {
                Id = 1,
                Name = "TODO",
                SortOrder = 1,
                SystemKey = WorkflowColumnKeys.Todo,
                IsSystem = true,
            },
            new Column
            {
                Id = 2,
                Name = "IN PROGRESS",
                SortOrder = 2,
                SystemKey = WorkflowColumnKeys.InProgress,
                IsSystem = true,
            },
            new Column
            {
                Id = 3,
                Name = "TESTING",
                SortOrder = 3,
                SystemKey = WorkflowColumnKeys.Testing,
                IsSystem = true,
            },
            new Column
            {
                Id = 4,
                Name = "DONE",
                SortOrder = 4,
                SystemKey = WorkflowColumnKeys.Done,
                IsSystem = true,
            },
        ];
        context.Cards.Items.Add(CreateCard(1, 0, 0, "Da spostare"));
        await context.ViewModel.StartAsync();
        var lane = context.ViewModel.Swimlanes.Single(item => item.IsGeneric);
        var card = Assert.Single(lane.Backlog.Cards);

        await context.ViewModel.MoveCardAsync(card, lane.Todo, targetCellIndex: 0);

        Assert.True(card.HasSaveSuccess);
        Assert.Equal("Card spostata", card.SaveStatusText);
        Assert.Contains(
            card,
            context.ViewModel.Swimlanes.Single(item => item.IsGeneric).Todo.Cards);

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
