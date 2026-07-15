namespace WeeklyPlanner.App.ViewModels;

public sealed record DeadlineUnitOption(string DisplayName, int HoursMultiplier)
{
    public override string ToString() => DisplayName;
}
