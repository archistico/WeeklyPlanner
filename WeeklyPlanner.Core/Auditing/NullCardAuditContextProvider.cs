namespace WeeklyPlanner.Core.Auditing;

public sealed class NullCardAuditContextProvider : ICardAuditContextProvider
{
    public static NullCardAuditContextProvider Instance { get; } = new();

    private NullCardAuditContextProvider()
    {
    }

    public CardAuditContext Current => CardAuditContext.Empty;
}
