using Microsoft.Data.Sqlite;
using WeeklyPlanner.App.Diagnostics;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardViewModelLoggingTests
{
    [Fact]
    public async Task Card_operations_are_logged_using_identifiers_not_content()
    {
        var logger = new RecordingAppLogger();
        var context = BoardViewModelTestDoubles.Create(logger: logger);
        await context.ViewModel.StartAsync();
        var column = Assert.Single(context.ViewModel.Columns);

        await context.ViewModel.AddCardCommand.ExecuteAsync(column);

        var entry = Assert.Single(logger.Entries, item => item.EventName == "card.created");
        Assert.Equal("1", entry.Properties["cardId"]?.ToString());
        Assert.DoesNotContain(entry.Properties.Keys, key =>
            key.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Notes", StringComparison.OrdinalIgnoreCase));

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Database_failure_has_same_reference_in_ui_and_log()
    {
        var logger = new RecordingAppLogger();
        var errorReferences = new FixedErrorReferenceGenerator("WP-ABC123");
        var context = BoardViewModelTestDoubles.Create(
            logger: logger,
            errorReferences: errorReferences);
        context.Initializer.EnqueueFailure(new SqliteException("cannot open", 14));

        await context.ViewModel.StartAsync();

        Assert.Contains("WP-ABC123", context.ViewModel.StatusMessage!, StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries, item => item.EventName == "database.failure");
        Assert.Equal("WP-ABC123", entry.ErrorReference);
        Assert.Equal(AppLogLevel.Error, entry.Level);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Runtime_diagnostics_do_not_expose_card_text()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Cards.Items.Add(new WeeklyPlanner.Core.Models.Card
        {
            Id = 1,
            ColumnId = 0,
            Title = "Segreto",
            Notes = "Contenuto sensibile",
            SortOrder = 0,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        await context.ViewModel.StartAsync();

        var diagnostics = context.ViewModel.GetRuntimeDiagnostics();

        Assert.Equal(1, diagnostics.CardCount);
        Assert.Equal(1, diagnostics.ColumnCount);
        Assert.DoesNotContain("Segreto", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Contenuto sensibile", diagnostics.ToString(), StringComparison.Ordinal);

        await context.ViewModel.DisposeAsync();
    }
}
