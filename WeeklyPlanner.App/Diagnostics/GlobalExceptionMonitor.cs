using Avalonia.Threading;

namespace WeeklyPlanner.App.Diagnostics;

public sealed class GlobalExceptionMonitor : IDisposable
{
    private static readonly TimeSpan FatalFlushTimeout = TimeSpan.FromSeconds(1);

    private readonly IAppLogger _logger;
    private readonly IErrorReferenceGenerator _errorReferences;
    private bool _registered;

    public GlobalExceptionMonitor(
        IAppLogger logger,
        IErrorReferenceGenerator errorReferences)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorReferences = errorReferences ?? throw new ArgumentNullException(nameof(errorReferences));
    }

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal("exception.ui_unhandled", "Eccezione non gestita sul thread UI.", e.Exception);
        // Non marcare come gestita: dopo un errore UI sconosciuto non è garantito che lo stato
        // dell'applicazione sia ancora coerente. Il log viene forzato su disco prima del crash.
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException("Eccezione AppDomain non rappresentata da System.Exception.");
        LogFatal("exception.process_unhandled", "Eccezione non gestita nel processo.", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var reference = _errorReferences.Create();
        _logger.Error(
            "exception.task_unobserved",
            "Eccezione non osservata in una Task.",
            e.Exception,
            reference);
        e.SetObserved();
    }

    private void LogFatal(string eventName, string message, Exception exception)
    {
        try
        {
            var reference = _errorReferences.Create();
            _logger.Critical(eventName, message, exception, reference);
            using var cancellation = new CancellationTokenSource(FatalFlushTimeout);
            _logger.FlushAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // Un handler globale non deve generare una seconda eccezione.
        }
    }
}
