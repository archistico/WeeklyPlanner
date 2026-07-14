using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ColumnViewModelTests
{
    [Fact]
    public void End_drop_indicator_can_be_shown_and_cleared()
    {
        var viewModel = new ColumnViewModel(new Column
        {
            Id = 1,
            Name = "Lunedì",
            SortOrder = 1,
        });

        viewModel.SetDropAtEnd(true);
        Assert.True(viewModel.IsDropAtEndVisible);

        viewModel.SetDropAtEnd(false);
        Assert.False(viewModel.IsDropAtEndVisible);
    }
}
