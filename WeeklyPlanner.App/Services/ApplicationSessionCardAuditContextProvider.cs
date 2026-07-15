using WeeklyPlanner.Core.Auditing;

namespace WeeklyPlanner.App.Services;

public sealed class ApplicationSessionCardAuditContextProvider : ICardAuditContextProvider
{
    private readonly IApplicationSession _session;

    public ApplicationSessionCardAuditContextProvider(IApplicationSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public CardAuditContext Current => new(_session.SessionId, _session.MachineName);
}
