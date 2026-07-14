using Avalonia.Threading;

namespace WeeklyPlanner.App.Services;

public sealed class AvaloniaRecurringTaskScheduler : IRecurringTaskScheduler
{
    private readonly DispatcherTimer _timer;
    private Func<CancellationToken, Task>? _callback;
    private CancellationToken _cancellationToken;
    private bool _isExecuting;
    private bool _isDisposed;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AvaloniaRecurringTaskScheduler));
            }
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _timer.Interval = value;
        }
    }

    public bool IsRunning => _timer.IsEnabled;

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
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AvaloniaRecurringTaskScheduler));
        }
        ArgumentNullException.ThrowIfNull(callback);

        _callback = callback;
        _cancellationToken = cancellationToken;
        _timer.Start();
    }

    public void Stop()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _callback = null;
        _cancellationToken = default;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed ||
            _isExecuting ||
            _callback is null ||
            _cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _isExecuting = true;
        try
        {
            await _callback(_cancellationToken);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // Arresto normale dello scheduler.
        }
        finally
        {
            _isExecuting = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _timer.Tick -= OnTimerTick;
        _isDisposed = true;
    }
}
