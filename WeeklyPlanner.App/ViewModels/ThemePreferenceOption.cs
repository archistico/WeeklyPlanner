using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.ViewModels;

public sealed record ThemePreferenceOption(
    AppThemePreference Value,
    string DisplayName);
