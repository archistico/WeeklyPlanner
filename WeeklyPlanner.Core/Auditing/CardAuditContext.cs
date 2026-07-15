namespace WeeklyPlanner.Core.Auditing;

public sealed record CardAuditContext(string? SessionId, string? MachineName)
{
    public static CardAuditContext Empty { get; } = new(null, null);
}
