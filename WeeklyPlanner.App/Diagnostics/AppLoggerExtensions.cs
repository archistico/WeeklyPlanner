namespace WeeklyPlanner.App.Diagnostics;

public static class AppLoggerExtensions
{
    public static void Information(
        this IAppLogger logger,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        logger.Log(AppLogLevel.Information, eventName, message, properties: properties);

    public static void Warning(
        this IAppLogger logger,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        logger.Log(AppLogLevel.Warning, eventName, message, exception, properties: properties);

    public static void Error(
        this IAppLogger logger,
        string eventName,
        string message,
        Exception exception,
        string errorReference,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        logger.Log(AppLogLevel.Error, eventName, message, exception, errorReference, properties);

    public static void Critical(
        this IAppLogger logger,
        string eventName,
        string message,
        Exception exception,
        string errorReference,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        logger.Log(AppLogLevel.Critical, eventName, message, exception, errorReference, properties);
}
