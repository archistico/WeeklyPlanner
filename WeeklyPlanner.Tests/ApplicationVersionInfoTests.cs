using WeeklyPlanner.App;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ApplicationVersionInfoTests
{
    [Fact]
    public void Milestone_is_read_from_centralized_assembly_metadata()
    {
        Assert.Equal("M2.3", ApplicationVersionInfo.Milestone);
        Assert.Contains("M2.3", ApplicationVersionInfo.WindowTitle);
    }
}
