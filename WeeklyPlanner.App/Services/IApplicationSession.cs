namespace WeeklyPlanner.App.Services;

public interface IApplicationSession
{
    string SessionId { get; }

    string MachineName { get; }
}
