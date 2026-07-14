using System.Reflection;
using WeeklyPlanner.App;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ApplicationVersionInfoTests
{
    [Fact]
    public void Milestone_is_read_from_centralized_assembly_metadata()
    {
        AssemblyMetadataAttribute metadata = Assert.Single(
            typeof(ApplicationVersionInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
            attribute => string.Equals(
                attribute.Key,
                "WeeklyPlannerMilestone",
                StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(metadata.Value));
        Assert.Equal(metadata.Value, ApplicationVersionInfo.Milestone);
        Assert.Contains(ApplicationVersionInfo.Milestone, ApplicationVersionInfo.WindowTitle);
    }
}
