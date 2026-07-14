using System.Reflection;

namespace WeeklyPlanner.App;

public static class ApplicationVersionInfo
{
    private static readonly Assembly AppAssembly = typeof(ApplicationVersionInfo).Assembly;

    public static string ProductVersion =>
        AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? AppAssembly.GetName().Version?.ToString()
        ?? "sconosciuta";

    public static string Milestone => AppAssembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute =>
            string.Equals(attribute.Key, "WeeklyPlannerMilestone", StringComparison.Ordinal))
        ?.Value
        ?? ProductVersion;

    public static string WindowTitle => $"WeeklyPlanner — {Milestone}";
}
