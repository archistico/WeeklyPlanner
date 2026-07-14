using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-connection-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Create_creates_the_parent_directory_for_a_local_database()
    {
        var databasePath = Path.Combine(_tempDirectory, "nested", "weeklyplanner.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        using var connection = connectionFactory.Create();

        Assert.True(Directory.Exists(Path.GetDirectoryName(databasePath)));
        Assert.True(File.Exists(databasePath));
    }

    [Fact]
    public void Create_accepts_an_existing_directory_and_uses_the_default_database_file_name()
    {
        var databaseDirectory = Path.Combine(_tempDirectory, "database-folder");
        Directory.CreateDirectory(databaseDirectory);
        var connectionFactory = new SqliteConnectionFactory(databaseDirectory);

        using var connection = connectionFactory.Create();

        var expectedPath = Path.Combine(databaseDirectory, AppSettings.DefaultDatabaseFileName);
        Assert.Equal(Path.GetFullPath(expectedPath), connectionFactory.DatabasePath);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void Constructor_rejects_a_relative_database_path()
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteConnectionFactory(Path.Combine("relative", "weeklyplanner.db")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
