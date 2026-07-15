namespace WeeklyPlanner.Core.Auditing;

public interface ICardAuditContextProvider
{
    CardAuditContext Current { get; }
}
