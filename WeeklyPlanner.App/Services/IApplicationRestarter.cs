namespace WeeklyPlanner.App.Services;

public interface IApplicationRestarter
{
    bool TryStartNewInstance(out string? errorMessage);
}
