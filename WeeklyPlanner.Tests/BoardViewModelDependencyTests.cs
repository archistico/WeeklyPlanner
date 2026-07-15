using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardViewModelDependencyTests
{
    [Fact]
    public async Task StartAsync_uses_injected_initializer_and_schedulers()
    {
        var context = BoardViewModelTestDoubles.Create();

        Assert.Equal(0, context.Initializer.EnsureInitializedCallCount);
        Assert.False(context.PollingScheduler.IsRunning);
        Assert.False(context.HeartbeatScheduler.IsRunning);

        await context.ViewModel.StartAsync();

        Assert.Equal(1, context.Initializer.EnsureInitializedCallCount);
        Assert.True(context.PollingScheduler.IsRunning);
        Assert.True(context.HeartbeatScheduler.IsRunning);
        Assert.Equal(1, context.PollingScheduler.StartCount);
        Assert.Equal(1, context.HeartbeatScheduler.StartCount);
        Assert.True(context.ViewModel.IsOnline);
        Assert.True(context.ViewModel.HasLastSuccessfulSync);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task ApplyRuntimeSettings_updates_injected_polling_scheduler()
    {
        var context = BoardViewModelTestDoubles.Create();
        var updatedSettings = new AppSettings
        {
            DatabasePath = context.ViewModel.CurrentDatabasePath,
            UserName = "Nuovo nome",
            PollingIntervalSeconds = 22,
        };

        context.ViewModel.ApplyRuntimeSettings(updatedSettings, databaseChangeRequiresRestart: false);

        Assert.Equal(TimeSpan.FromSeconds(22), context.PollingScheduler.Interval);
        Assert.Equal("Nuovo nome", context.ViewModel.CurrentUserName);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Injected_scheduler_can_trigger_poll_without_Avalonia_dispatcher()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        context.ChangeDetector.HasChanged = false;

        await context.PollingScheduler.TriggerAsync();

        Assert.Equal(1, context.ChangeDetector.CallCount);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_stops_schedulers_and_releases_current_session()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();

        await context.ViewModel.DisposeAsync();

        Assert.False(context.PollingScheduler.IsRunning);
        Assert.False(context.HeartbeatScheduler.IsRunning);
        Assert.True(context.PollingScheduler.IsDisposed);
        Assert.True(context.HeartbeatScheduler.IsDisposed);
        Assert.Equal(context.Session.SessionId, context.Locks.ReleasedSessionId);
    }
    [Fact]
    public async Task StartAsync_exposes_the_catalogs_and_revision_from_the_atomic_snapshot()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.Revision = 42;
        context.Snapshot.Priorities =
        [
            new WeeklyPlanner.Core.Models.PriorityDefinition
            {
                Id = 1,
                Code = "U",
                Name = "Urgente",
                DefaultDueHours = 72,
                SortOrder = 0,
                IsActive = true,
                Version = 1,
            },
        ];

        await context.ViewModel.StartAsync();

        Assert.Equal(42, context.ViewModel.BoardRevision);
        Assert.Single(context.ViewModel.Priorities);
        Assert.Single(
            context.ViewModel.CardTypes,
            item => item.SystemKey == WeeklyPlanner.Core.Models.SystemCardTypeKeys.Generic);

        await context.ViewModel.DisposeAsync();
    }

}
