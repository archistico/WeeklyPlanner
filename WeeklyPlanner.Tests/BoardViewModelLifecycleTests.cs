using Microsoft.Data.Sqlite;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardViewModelLifecycleTests
{
    [Fact]
    public async Task Polling_runs_only_after_the_configured_interval_has_elapsed()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();

        await context.PollingScheduler.AdvanceByAsync(TimeSpan.FromSeconds(6));
        Assert.Equal(0, context.ChangeDetector.CallCount);

        await context.PollingScheduler.AdvanceByAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, context.ChangeDetector.CallCount);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Runtime_polling_interval_is_applied_to_the_next_complete_cycle()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        await context.PollingScheduler.AdvanceByAsync(TimeSpan.FromSeconds(3));

        context.ViewModel.ApplyRuntimeSettings(
            new AppSettings
            {
                DatabasePath = context.ViewModel.CurrentDatabasePath,
                UserName = context.ViewModel.CurrentUserName,
                PollingIntervalSeconds = 4,
            },
            databaseChangeRequiresRestart: false);

        await context.PollingScheduler.AdvanceByAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, context.ChangeDetector.CallCount);

        await context.PollingScheduler.AdvanceByAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, context.ChangeDetector.CallCount);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Slow_poll_is_not_overlapped_by_a_second_tick()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.ChangeDetector.Handler = async cancellationToken =>
        {
            callbackStarted.TrySetResult();
            await releaseCallback.Task.WaitAsync(cancellationToken);
            return false;
        };

        var firstTick = context.PollingScheduler.TriggerAsync();
        await callbackStarted.Task;

        var secondTickStarted = await context.PollingScheduler.TriggerAsync();

        Assert.False(secondTickStarted);
        Assert.Equal(1, context.ChangeDetector.CallCount);

        releaseCallback.TrySetResult();
        Assert.True(await firstTick);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Three_transient_failures_move_offline_and_a_success_resets_the_counter()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        var contention = new SqliteException("locked", 5);

        context.ChangeDetector.EnqueueFailure(contention);
        context.Initializer.EnqueueFailure(new SqliteException("locked", 5));
        context.Initializer.EnqueueFailure(new SqliteException("locked", 5));

        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(BoardConnectionState.Recovering, context.ViewModel.ConnectionState);

        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(BoardConnectionState.Recovering, context.ViewModel.ConnectionState);

        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(BoardConnectionState.Offline, context.ViewModel.ConnectionState);

        context.Clock.Now = context.Clock.Now.AddMinutes(1);
        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(BoardConnectionState.Online, context.ViewModel.ConnectionState);
        Assert.True(context.ViewModel.HasLastSuccessfulSync);

        context.ChangeDetector.EnqueueFailure(new SqliteException("locked", 5));
        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(BoardConnectionState.Recovering, context.ViewModel.ConnectionState);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Structural_failure_stops_automatic_retry_until_manual_retry()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        context.ChangeDetector.EnqueueFailure(new SqliteException("corrupt", 11));

        await context.PollingScheduler.TriggerAsync();

        Assert.Equal(BoardConnectionState.Error, context.ViewModel.ConnectionState);
        Assert.Equal(1, context.ChangeDetector.CallCount);

        await context.PollingScheduler.TriggerAsync();
        Assert.Equal(1, context.ChangeDetector.CallCount);

        await context.ViewModel.RetryNowCommand.ExecuteAsync(null);

        Assert.Equal(BoardConnectionState.Online, context.ViewModel.ConnectionState);
        Assert.False(context.ViewModel.HasConnectionError);

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Heartbeat_marks_the_editing_card_when_the_lease_is_lost()
    {
        var context = BoardViewModelTestDoubles.Create();
        context.Cards.Items.Add(CreateCard());
        await context.ViewModel.StartAsync();
        var card = Assert.Single(context.ViewModel.Columns.Single(
            column => column.SystemKey == WorkflowColumnKeys.Backlog).Cards);

        Assert.True(await context.ViewModel.BeginEditCardAsync(card));
        context.Locks.RenewHandler = (_, _, _, _) =>
        {
            context.Locks.ActiveLocks.Clear();
            return Task.FromResult(false);
        };

        await context.HeartbeatScheduler.AdvanceByAsync(TimeSpan.FromSeconds(9));
        Assert.Equal(0, context.Locks.RenewCallCount);
        Assert.False(card.HasLostEditLock);

        await context.HeartbeatScheduler.AdvanceByAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, context.Locks.RenewCallCount);
        Assert.True(card.HasLostEditLock);
        Assert.NotNull(context.ViewModel.StatusMessage);
        Assert.Contains(
            "scaduti",
            context.ViewModel.StatusMessage!.ToLowerInvariant());

        await context.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_cancels_an_inflight_poll_and_releases_the_session_once()
    {
        var context = BoardViewModelTestDoubles.Create();
        await context.ViewModel.StartAsync();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.ChangeDetector.Handler = async cancellationToken =>
        {
            callbackStarted.TrySetResult();
            var cancellationRequested = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () => cancellationRequested.TrySetResult());
            try
            {
                await cancellationRequested.Task;
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            finally
            {
                callbackCompleted.TrySetResult();
            }
        };

        var tick = context.PollingScheduler.TriggerAsync();
        await callbackStarted.Task;

        await context.ViewModel.DisposeAsync();

        Assert.True(callbackCompleted.Task.IsCompletedSuccessfully);
        Assert.True(await tick);
        Assert.Equal(1, context.Locks.ReleaseSessionCallCount);
        Assert.Equal(context.Session.SessionId, context.Locks.ReleasedSessionId);
        Assert.True(context.PollingScheduler.IsDisposed);
        Assert.True(context.HeartbeatScheduler.IsDisposed);

        await context.ViewModel.DisposeAsync();
        Assert.Equal(1, context.Locks.ReleaseSessionCallCount);
    }

    private static Card CreateCard() => new()
    {
        Id = 1,
        ColumnId = 0,
        Title = "Card",
        Notes = "Note",
        SortOrder = 0,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-14T18:00:00.0000000Z",
        Version = 1,
    };
}
