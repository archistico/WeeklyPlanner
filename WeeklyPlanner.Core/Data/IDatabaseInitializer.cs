namespace WeeklyPlanner.Core.Data;

public interface IDatabaseInitializer
{
    void EnsureInitialized(bool allowCreate = true);
}
