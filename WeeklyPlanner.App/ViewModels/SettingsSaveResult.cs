using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.ViewModels;

public sealed record SettingsSaveResult(
    AppSettings Settings,
    bool UserNameChanged,
    bool DatabasePathChanged)
{
    public bool RequiresRestart => DatabasePathChanged;
}
