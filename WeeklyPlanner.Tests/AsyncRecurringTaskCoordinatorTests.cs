using WeeklyPlanner.App.Services;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class AsyncRecurringTaskCoordinatorTests
{
    [Fact]
    public async Task TryExecuteAsync_discards_a_tick_while_the_callback_is_running()
    {
        await using var coordinator = new AsyncRecurringTaskCoordinator();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentExecutions = 0;
        var maximumConcurrency = 0;
        var callCount = 0;

        coordinator.Start(
            async cancellationToken =>
            {
                callCount++;
                var currentConcurrency = Interlocked.Increment(ref concurrentExecutions);
                maximumConcurrency = Math.Max(maximumConcurrency, currentConcurrency);
                callbackStarted.TrySetResult();
                try
                {
                    await releaseCallback.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrentExecutions);
                }
            },
            CancellationToken.None);

        var firstExecution = coordinator.TryExecuteAsync();
        await callbackStarted.Task;

        var secondExecutionStarted = await coordinator.TryExecuteAsync();

        Assert.False(secondExecutionStarted);
        Assert.True(coordinator.IsExecuting);
        Assert.Equal(1, callCount);

        releaseCallback.TrySetResult();
        Assert.True(await firstExecution);

        Assert.False(coordinator.IsExecuting);
        Assert.Equal(1, maximumConcurrency);
    }

    [Fact]
    public async Task StopAsync_cancels_and_waits_for_the_active_callback()
    {
        await using var coordinator = new AsyncRecurringTaskCoordinator();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        coordinator.Start(
            async cancellationToken =>
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
                }
                finally
                {
                    callbackCompleted.TrySetResult();
                }
            },
            CancellationToken.None);

        var execution = coordinator.TryExecuteAsync();
        await callbackStarted.Task;

        await coordinator.StopAsync();

        Assert.True(callbackCompleted.Task.IsCompletedSuccessfully);
        Assert.True(await execution);
        Assert.False(coordinator.IsRunning);
        Assert.False(coordinator.IsExecuting);
        Assert.False(await coordinator.TryExecuteAsync());
    }

    [Fact]
    public async Task Callback_failure_is_observed_and_does_not_fault_the_tick_task()
    {
        await using var coordinator = new AsyncRecurringTaskCoordinator();
        var expected = new InvalidOperationException("failure");

        coordinator.Start(
            _ => Task.FromException(expected),
            CancellationToken.None);

        var executionStarted = await coordinator.TryExecuteAsync();

        Assert.True(executionStarted);
        Assert.Same(expected, coordinator.LastFailure);
        Assert.True(coordinator.IsRunning);
    }

    [Fact]
    public async Task DisposeAsync_prevents_future_start_and_execution()
    {
        var coordinator = new AsyncRecurringTaskCoordinator();

        await coordinator.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() =>
            coordinator.Start(_ => Task.CompletedTask, CancellationToken.None));
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = coordinator.TryExecuteAsync();
        });
    }
}
