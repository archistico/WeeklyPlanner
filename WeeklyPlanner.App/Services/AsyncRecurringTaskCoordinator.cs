namespace WeeklyPlanner.App.Services;

/// <summary>
/// Coordina una callback asincrona ricorrente garantendo che una sola esecuzione sia attiva.
/// La sorgente dei tick resta separata: in produzione è un DispatcherTimer, nei test può essere
/// uno scheduler deterministico controllato senza attese reali.
/// </summary>
public sealed class AsyncRecurringTaskCoordinator : IAsyncDisposable
{
    private readonly object _syncRoot = new();

    private Func<CancellationToken, Task>? _callback;
    private CancellationTokenSource? _executionCancellation;
    private Task _activeExecution = Task.CompletedTask;
    private Task _stopTask = Task.CompletedTask;
    private Exception? _lastFailure;
    private bool _isRunning;
    private bool _disposeRequested;
    private bool _isDisposed;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public bool IsExecuting
    {
        get
        {
            lock (_syncRoot)
            {
                return !_activeExecution.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Conserva l'ultima eccezione inattesa prodotta dalla callback. Le callback applicative
    /// gestiscono normalmente i propri errori; questa proprietà evita comunque task faulted non
    /// osservati e sarà collegata al logging tecnico in una milestone successiva.
    /// </summary>
    public Exception? LastFailure
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastFailure;
            }
        }
    }

    public void Start(
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_disposeRequested)
            {
                throw new ObjectDisposedException(nameof(AsyncRecurringTaskCoordinator));
            }

            if (_isRunning || !_stopTask.IsCompleted)
            {
                throw new InvalidOperationException("Lo scheduler ricorrente è già avviato.");
            }

            _executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _callback = callback;
            _lastFailure = null;
            _activeExecution = Task.CompletedTask;
            _isRunning = true;
        }
    }

    /// <summary>
    /// Richiede una singola esecuzione. Se una callback è già in corso, il tick viene scartato:
    /// non vengono create code arretrate e due esecuzioni non possono sovrapporsi.
    /// </summary>
    /// <returns><see langword="true"/> se la callback è stata avviata.</returns>
    public Task<bool> TryExecuteAsync()
    {
        Task execution;

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_disposeRequested ||
                !_isRunning ||
                _callback is null ||
                _executionCancellation is null ||
                _executionCancellation.IsCancellationRequested ||
                !_activeExecution.IsCompleted)
            {
                return Task.FromResult(false);
            }

            execution = ExecuteCallbackAsync(
                _callback,
                _executionCancellation.Token);
            _activeExecution = execution;
        }

        return AwaitExecutionAsync(execution);
    }

    public Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task activeExecution;
        TaskCompletionSource stopCompletion;

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return Task.CompletedTask;
            }

            if (!_stopTask.IsCompleted)
            {
                return _stopTask;
            }

            if (!_isRunning && _executionCancellation is null)
            {
                return Task.CompletedTask;
            }

            _isRunning = false;
            cancellation = _executionCancellation;
            activeExecution = _activeExecution;
            stopCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = stopCompletion.Task;
        }

        _ = CompleteStopAsync(cancellation, activeExecution, stopCompletion);
        return stopCompletion.Task;
    }

    private async Task CompleteStopAsync(
        CancellationTokenSource? cancellation,
        Task activeExecution,
        TaskCompletionSource stopCompletion)
    {
        try
        {
            try
            {
                cancellation?.Cancel();
            }
            catch (Exception ex)
            {
                StoreFailure(ex);
            }

            await activeExecution.ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                if (cancellation is not null &&
                    ReferenceEquals(_executionCancellation, cancellation))
                {
                    _executionCancellation = null;
                    _callback = null;
                    _activeExecution = Task.CompletedTask;
                }
            }

            cancellation?.Dispose();
            stopCompletion.TrySetResult();
        }
    }

    private async Task ExecuteCallbackAsync(
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        try
        {
            await callback(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arresto normale dello scheduler.
        }
        catch (Exception ex)
        {
            StoreFailure(ex);
        }
    }

    private void StoreFailure(Exception exception)
    {
        lock (_syncRoot)
        {
            _lastFailure = exception;
        }
    }

    private static async Task<bool> AwaitExecutionAsync(Task execution)
    {
        await execution.ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _disposeRequested = true;
        }

        await StopAsync().ConfigureAwait(false);

        lock (_syncRoot)
        {
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AsyncRecurringTaskCoordinator));
        }
    }
}
