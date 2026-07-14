namespace WeeklyPlanner.App.Services;

/// <summary>
/// Esegue il cleanup asincrono durante l'uscita del processo senza consentire
/// a una risorsa difettosa di bloccare indefinitamente il thread del lifetime desktop.
/// </summary>
public static class BoundedAsyncDisposer
{
    public static bool TryDispose(IAsyncDisposable disposable, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Task disposeTask;
        try
        {
            disposeTask = disposable.DisposeAsync().AsTask();
        }
        catch
        {
            return false;
        }

        try
        {
            if (!disposeTask.Wait(timeout))
            {
                ObserveLateFailure(disposeTask);
                return false;
            }

            disposeTask.GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ObserveLateFailure(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
