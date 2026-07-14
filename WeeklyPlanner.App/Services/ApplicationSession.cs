namespace WeeklyPlanner.App.Services;

public sealed class ApplicationSession : IApplicationSession
{
    public string SessionId { get; }

    public string MachineName { get; }

    public ApplicationSession(string sessionId, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        SessionId = sessionId;
        MachineName = machineName;
    }

    public static ApplicationSession CreateDefault() => new(
        Guid.NewGuid().ToString("N"),
        Environment.MachineName);
}
