using System.Xml.Linq;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SwimlaneBoardProjectionTests
{
    [Fact]
    public async Task Atomic_snapshot_is_projected_into_ordered_lanes_and_five_workflow_cells()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes = CreateCardTypes();
        context.Cards.Items.AddRange(
        [
            CreateCard(1, columnId: 0, cardTypeId: null, sortOrder: 0, title: "Generica"),
            CreateCard(2, columnId: 1, cardTypeId: 2, sortOrder: 0, title: "WinCassa"),
            CreateCard(3, columnId: 4, cardTypeId: 3, sortOrder: 0, title: "Legacy"),
        ]);

        await context.ViewModel.StartAsync();

        Assert.Collection(
            context.ViewModel.Swimlanes,
            lane => Assert.Equal("Generica", lane.Name),
            lane => Assert.Equal("WinCassa", lane.Name),
            lane => Assert.Equal("Legacy", lane.Name));

        var generic = context.ViewModel.Swimlanes[0];
        Assert.True(generic.IsGeneric);
        Assert.Equal(5, generic.Cells.Count);
        Assert.Equal(
            WorkflowColumnKeys.Ordered.ToArray(),
            generic.Cells.Select(cell => cell.Column.SystemKey!).ToArray());
        Assert.Equal("Generica", Assert.Single(generic.Backlog.Cards).Title);
        Assert.All(generic.Cells, cell => Assert.True(cell.CanCreateCard));
        Assert.True(generic.Todo.IsEmpty);

        var winCassa = context.ViewModel.Swimlanes[1];
        Assert.Equal("WinCassa", Assert.Single(winCassa.Todo.Cards).Title);

        var legacy = context.ViewModel.Swimlanes[2];
        Assert.True(legacy.IsInactive);
        Assert.Equal("Legacy", Assert.Single(legacy.Done.Cards).Title);

        Assert.DoesNotContain(context.ViewModel.Swimlanes, lane => lane.Name == "Archiviata vuota");

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Refresh_reuses_the_card_view_model_while_moving_it_to_another_lane_cell()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes = CreateCardTypes();
        context.Cards.Items.Add(CreateCard(
            1,
            columnId: 0,
            cardTypeId: 1,
            sortOrder: 0,
            title: "Da riclassificare"));
        await context.ViewModel.StartAsync();

        var original = Assert.Single(context.ViewModel.Swimlanes[0].Backlog.Cards);
        context.Cards.Items[0].CardTypeId = 2;
        context.Cards.Items[0].ColumnId = 3;
        context.ChangeDetector.HasChanged = true;

        await context.PollingScheduler.TriggerAsync();

        var refreshed = Assert.Single(
            context.ViewModel.Swimlanes.Single(lane => lane.Id == 2).Testing.Cards);
        Assert.Same(original, refreshed);
        Assert.Empty(context.ViewModel.Swimlanes.Single(lane => lane.IsGeneric).Backlog.Cards);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Existing_move_contract_changes_state_without_changing_lane()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes = CreateCardTypes();
        context.Cards.Items.Add(CreateCard(
            1,
            columnId: 0,
            cardTypeId: 2,
            sortOrder: 0,
            title: "Da spostare"));
        await context.ViewModel.StartAsync();

        var lane = context.ViewModel.Swimlanes.Single(item => item.Id == 2);
        var card = Assert.Single(lane.Backlog.Cards);

        await context.ViewModel.MoveCardAsync(card, lane.Todo, targetCellIndex: 0);

        var refreshedLane = context.ViewModel.Swimlanes.Single(item => item.Id == 2);
        Assert.Empty(refreshedLane.Backlog.Cards);
        var moved = Assert.Single(refreshedLane.Todo.Cards);
        Assert.Same(card, moved);
        Assert.Equal(2, moved.Model.CardTypeId);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Bidimensional_move_changes_lane_and_state_in_one_operation()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes = CreateCardTypes();
        context.Cards.Items.Add(CreateCard(
            1,
            columnId: 0,
            cardTypeId: 2,
            sortOrder: 0,
            title: "Da riclassificare e spostare"));
        await context.ViewModel.StartAsync();

        var sourceLane = context.ViewModel.Swimlanes.Single(item => item.Id == 2);
        var targetLane = context.ViewModel.Swimlanes.Single(item => item.IsGeneric);
        var card = Assert.Single(sourceLane.Backlog.Cards);

        await context.ViewModel.MoveCardAsync(card, targetLane.Testing, targetCellIndex: 0);

        var refreshedTarget = context.ViewModel.Swimlanes.Single(item => item.IsGeneric);
        var moved = Assert.Single(refreshedTarget.Testing.Cards);
        Assert.Same(card, moved);
        Assert.Equal(1, moved.Model.CardTypeId);
        Assert.Equal(3, moved.Model.ColumnId);
        Assert.Equal("Card spostata", moved.SaveStatusText);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public void Main_window_declares_the_swimlane_matrix_and_global_scroll()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Views",
            "MainWindow.axaml"));
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollViewer = document
            .Descendants(avalonia + "ScrollViewer")
            .Single(element => (string?)element.Attribute(x + "Name") == "BoardScrollViewer");
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Stretch", (string?)scrollViewer.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Stretch", (string?)scrollViewer.Attribute("VerticalContentAlignment"));

        var textValues = document
            .Descendants(avalonia + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("TIPOLOGIA", textValues);
        Assert.Contains("BACKLOG", textValues);
        Assert.Contains("TODO", textValues);
        Assert.Contains("IN PROGRESS", textValues);
        Assert.Contains("TESTING", textValues);
        Assert.Contains("DONE", textValues);
        Assert.DoesNotContain("Nessuna card", textValues);

        var swimlanesItemsControl = document
            .Descendants(avalonia + "ItemsControl")
            .Single(element => (string?)element.Attribute(x + "Name") == "SwimlanesItemsControl");
        Assert.Equal("{Binding Swimlanes}", (string?)swimlanesItemsControl.Attribute("ItemsSource"));

        var cardBorder = document
            .Descendants(avalonia + "Border")
            .Single(element =>
                string.Equals((string?)element.Attribute("Classes"), "card", StringComparison.Ordinal));
        Assert.Equal("OnCardKeyDown", (string?)cardBorder.Attribute("KeyDown"));

        var swimlaneCellBorder = document
            .Descendants(avalonia + "Border")
            .Single(element =>
                string.Equals((string?)element.Attribute("Classes"), "swimlaneCell", StringComparison.Ordinal));
        Assert.Equal("True", (string?)swimlaneCellBorder.Attribute("DragDrop.AllowDrop"));

        var addButtons = document
            .Descendants(avalonia + "Button")
            .Where(element =>
                ((string?)element.Attribute("AutomationProperties.Name"))?.StartsWith(
                    "Aggiungi card in ",
                    StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(5, addButtons.Count);
        Assert.All(addButtons, button =>
        {
            Assert.Equal("{Binding AddCardCommand}", (string?)button.Attribute("Command"));
            var commandParameter = (string?)button.Attribute("CommandParameter");
            Assert.NotNull(commandParameter);
            Assert.StartsWith("{Binding ", commandParameter);
        });
        Assert.DoesNotContain(
            document.Descendants(avalonia + "Button"),
            element => string.Equals(
                (string?)element.Attribute("IsVisible"),
                "{Binding CanCreateCard}",
                StringComparison.Ordinal));

        Assert.Equal("Maximized", (string?)document.Root?.Attribute("WindowState"));
        Assert.Equal("True", (string?)document.Root?.Attribute("ShowActivated"));
        Assert.Equal("True", (string?)document.Root?.Attribute("ShowInTaskbar"));

        var priorityCombo = document
            .Descendants(avalonia + "ComboBox")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute("ItemsSource"),
                    "{Binding PriorityOptions}",
                    StringComparison.Ordinal));
        Assert.Equal(
            "{Binding SelectedPriorityOption}",
            (string?)priorityCombo.Attribute("SelectedItem"));
        Assert.Equal(
            "{Binding CanEditDraft}",
            (string?)priorityCombo.Attribute("IsEnabled"));
        var prioritySummary = document
            .Descendants(avalonia + "Border")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute("Classes"),
                    "prioritySummary",
                    StringComparison.Ordinal));
        Assert.Equal("{Binding IsNotEditing}", (string?)prioritySummary.Attribute("IsVisible"));
        Assert.Equal(
            "OnPrioritySummaryPointerPressed",
            (string?)prioritySummary.Attribute("PointerPressed"));
        Assert.Equal("Hand", (string?)prioritySummary.Attribute("Cursor"));

        Assert.Equal("OnCardFieldGotFocus", (string?)priorityCombo.Attribute("GotFocus"));
        Assert.Null((string?)priorityCombo.Attribute("LostFocus"));
        Assert.Equal("OnCardFieldKeyDown", (string?)priorityCombo.Attribute("KeyDown"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("LostFocus"),
                "OnCardFieldLostFocus",
                StringComparison.Ordinal));

        var controlsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Themes",
            "Controls.axaml"));
        var controlsDocument = XDocument.Load(controlsPath);
        var prioritySummaryStyle = controlsDocument
            .Descendants(avalonia + "Style")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute("Selector"),
                    "Border.prioritySummary",
                    StringComparison.Ordinal));
        var prioritySummarySetters = prioritySummaryStyle
            .Elements(avalonia + "Setter")
            .ToDictionary(
                element => (string?)element.Attribute("Property") ?? string.Empty,
                element => (string?)element.Attribute("Value"),
                StringComparer.Ordinal);
        Assert.Equal("Transparent", prioritySummarySetters["Background"]);
        Assert.Equal("0", prioritySummarySetters["BorderThickness"]);

        var matrixGrid = document
            .Descendants(avalonia + "Grid")
            .Single(element => (string?)element.Attribute("RowDefinitions") == "Auto,Auto,160");
        Assert.Equal("1630", (string?)matrixGrid.Attribute("MinWidth"));

        Assert.Contains(
            document.Descendants(avalonia + "Grid"),
            element => (string?)element.Attribute("ColumnDefinitions") == "230,*,*,*,*,*");
    }

    [Fact]
    public void Main_window_is_forced_maximized_before_show_and_after_native_opening()
    {
        var appSourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "App.axaml.cs"));
        var appSource = File.ReadAllText(appSourcePath);
        Assert.Contains(
            "mainWindow.WindowState = Avalonia.Controls.WindowState.Maximized;",
            appSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "mainWindow.ShowActivated = true;",
            appSource,
            StringComparison.Ordinal);

        var windowSourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Views",
            "MainWindow.axaml.cs"));
        var windowSource = File.ReadAllText(windowSourcePath);
        Assert.Contains("ActivateWindowAtStartup();", windowSource, StringComparison.Ordinal);
        Assert.Contains("Topmost = true;", windowSource, StringComparison.Ordinal);
        Assert.Contains("Topmost = false;", windowSource, StringComparison.Ordinal);
        Assert.Contains("Activate();", windowSource, StringComparison.Ordinal);
        Assert.Contains("priorityCombo.IsDropDownOpen = true;", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnCardFieldLostFocus", windowSource, StringComparison.Ordinal);

        var boardViewModelPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "ViewModels",
            "BoardViewModel.cs"));
        var boardViewModelSource = File.ReadAllText(boardViewModelPath);
        Assert.Contains("if (!HasActiveEdits)", boardViewModelSource, StringComparison.Ordinal);
        Assert.Contains("RebuildSwimlanes();", boardViewModelSource, StringComparison.Ordinal);
    }

    private static IReadOnlyList<CardTypeDefinition> CreateCardTypes() =>
    [
        new CardTypeDefinition
        {
            Id = 1,
            Name = "Generica",
            ColorHex = "#64748B",
            SortOrder = 99,
            IsActive = true,
            IsDefault = true,
            Version = 1,
            SystemKey = SystemCardTypeKeys.Generic,
            IsSystem = true,
        },
        new CardTypeDefinition
        {
            Id = 2,
            Name = "WinCassa",
            ColorHex = "#3584E4",
            SortOrder = 1,
            IsActive = true,
            Version = 1,
        },
        new CardTypeDefinition
        {
            Id = 3,
            Name = "Legacy",
            ColorHex = "#A51D2D",
            SortOrder = 2,
            IsActive = false,
            Version = 1,
        },
        new CardTypeDefinition
        {
            Id = 4,
            Name = "Archiviata vuota",
            ColorHex = "#77767B",
            SortOrder = 3,
            IsActive = false,
            Version = 1,
        },
    ];

    private static Card CreateCard(
        long id,
        long columnId,
        long? cardTypeId,
        int sortOrder,
        string title) => new()
    {
        Id = id,
        ColumnId = columnId,
        StableId = $"card-{id}",
        CreatedAtUtc = "2026-07-15T12:00:00.0000000Z",
        CardTypeId = cardTypeId,
        Title = title,
        SortOrder = sortOrder,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-15T12:00:00.0000000Z",
        Version = 1,
    };
}
