using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseInstanceRegistryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-instance-registry-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Register_exposes_the_live_instance_and_dispose_removes_it()
    {
        var registry = new DatabaseInstanceRegistry(Path.Combine(_tempDirectory, "sessions"));
        var databasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db");
        await using var lease = registry.Register(databasePath, "session-a");

        var active = registry.GetActiveInstances(databasePath);

        var instance = Assert.Single(active);
        Assert.Equal("session-a", instance.SessionId);
        Assert.Equal(Environment.ProcessId, instance.ProcessId);
        Assert.True(File.Exists(instance.MarkerPath));

        await lease.DisposeAsync();
        Assert.Empty(registry.GetActiveInstances(databasePath));
    }

    [Fact]
    public async Task GetActiveInstances_can_exclude_the_current_session()
    {
        var registry = new DatabaseInstanceRegistry(Path.Combine(_tempDirectory, "sessions"));
        var databasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db");
        await using var first = registry.Register(databasePath, "session-a");
        await using var second = registry.Register(databasePath, "session-b");

        var active = registry.GetActiveInstances(databasePath, "session-a");

        var instance = Assert.Single(active);
        Assert.Equal("session-b", instance.SessionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
