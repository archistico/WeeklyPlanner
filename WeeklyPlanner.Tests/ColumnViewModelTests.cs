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
            Name = "TODO",
            SystemKey = WorkflowColumnKeys.Todo,
            IsSystem = true,
            SortOrder = 1,
        });

        viewModel.SetDropAtEnd(true);
        Assert.True(viewModel.IsDropAtEndVisible);

        viewModel.SetDropAtEnd(false);
        Assert.False(viewModel.IsDropAtEndVisible);
    }
    [Fact]
    public void RefreshFromModel_updates_workflow_metadata()
    {
        var viewModel = new ColumnViewModel(new Column
        {
            Id = 1,
            Name = "TODO",
            SortOrder = 1,
            SystemKey = WorkflowColumnKeys.Todo,
            IsSystem = true,
        });

        viewModel.RefreshFromModel(new Column
        {
            Id = 1,
            Name = "IN PROGRESS",
            SortOrder = 2,
            SystemKey = WorkflowColumnKeys.InProgress,
            IsSystem = true,
        });

        Assert.Equal("IN PROGRESS", viewModel.Name);
        Assert.Equal(WorkflowColumnKeys.InProgress, viewModel.SystemKey);
        Assert.True(viewModel.IsSystem);
        Assert.Equal(2, viewModel.Model.SortOrder);
    }

}
