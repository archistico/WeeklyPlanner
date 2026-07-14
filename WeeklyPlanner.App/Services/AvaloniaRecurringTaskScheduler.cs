using Avalonia.Threading;

namespace WeeklyPlanner.App.Services;

public sealed class AvaloniaRecurringTaskScheduler : IRecurringTaskScheduler
{
    private readonly DispatcherTimer _timer;
    private readonly AsyncRecurringTaskCoordinator _coordinator = new();
    private bool _isDisposed;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set
        {
            ThrowIfDisposed();
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _timer.Interval = value;
        }
    }

    public bool IsRunning => !_isDisposed && _coordinator.IsRunning;

    public bool IsExecuting => !_isDisposed && _coordinator.IsExecuting;

    public AvaloniaRecurringTaskScheduler(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _timer = new DispatcherTimer
        {
            Interval = interval,
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start(
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(callback);

        _coordinator.Start(callback, cancellationToken);
        _timer.Start();
    }

    public async Task StopAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        await _coordinator.StopAsync();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        // Il coordinator osserva sempre il task e scarta il tick se una callback è già in corso.
        _ = _coordinator.TryExecuteAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        await _coordinator.DisposeAsync();
        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AvaloniaRecurringTaskScheduler));
        }
    }
}
