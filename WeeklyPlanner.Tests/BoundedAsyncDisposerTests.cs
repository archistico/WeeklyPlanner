using WeeklyPlanner.App.Services;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoundedAsyncDisposerTests
{
    [Fact]
    public void Completed_disposal_returns_true()
    {
        var disposable = new CompletedDisposable();

        var completed = BoundedAsyncDisposer.TryDispose(disposable, TimeSpan.Zero);

        Assert.True(completed);
        Assert.Equal(1, disposable.DisposeCallCount);
    }

    [Fact]
    public void Incomplete_disposal_returns_false_without_waiting_indefinitely()
    {
        var disposable = new PendingDisposable();

        var completed = BoundedAsyncDisposer.TryDispose(disposable, TimeSpan.Zero);

        Assert.False(completed);
        Assert.Equal(1, disposable.DisposeCallCount);
        disposable.Complete();
    }

    [Fact]
    public void Faulted_disposal_returns_false()
    {
        var disposable = new FaultedDisposable();

        var completed = BoundedAsyncDisposer.TryDispose(disposable, TimeSpan.Zero);

        Assert.False(completed);
    }

    private sealed class CompletedDisposable : IAsyncDisposable
    {
        public int DisposeCallCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PendingDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCallCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return new ValueTask(_completion.Task);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FaultedDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            new(Task.FromException(new InvalidOperationException("Errore di cleanup simulato.")));
    }
}
