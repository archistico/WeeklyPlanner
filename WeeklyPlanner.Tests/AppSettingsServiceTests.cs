using System.Text.Json;
using WeeklyPlanner.Core.Configuration;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Load_returns_defaults_when_file_does_not_exist()
    {
        var service = CreateService();

        var settings = service.Load();

        Assert.False(settings.IsComplete());
        Assert.Equal(AppSettings.DefaultPollingIntervalSeconds, settings.PollingIntervalSeconds);
    }

    [Fact]
    public void Load_returns_defaults_when_json_is_corrupted()
    {
        var settingsPath = GetSettingsPath();
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(settingsPath, "{ not valid json");
        var service = new AppSettingsService(settingsPath);

        var settings = service.Load();

        Assert.False(settings.IsComplete());
    }


    [Fact]
    public void Load_normalizes_null_strings_from_json()
    {
        var settingsPath = GetSettingsPath();
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(
            settingsPath,
            """
            {
              "DatabasePath": null,
              "UserName": null,
              "PollingIntervalSeconds": 500
            }
            """);
        var service = new AppSettingsService(settingsPath);

        var settings = service.Load();

        Assert.Equal(string.Empty, settings.DatabasePath);
        Assert.Equal(string.Empty, settings.UserName);
        Assert.Equal(AppSettings.MaximumPollingIntervalSeconds, settings.PollingIntervalSeconds);
    }

    [Fact]
    public void Save_and_load_roundtrip_normalizes_values()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            DatabasePath = "  C:\\Data\\weeklyplanner.db  ",
            UserName = "  Emilie  ",
            PollingIntervalSeconds = 1,
        };

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal("C:\\Data\\weeklyplanner.db", reloaded.DatabasePath);
        Assert.Equal("Emilie", reloaded.UserName);
        Assert.Equal(AppSettings.MinimumPollingIntervalSeconds, reloaded.PollingIntervalSeconds);
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp"));
    }


    [Fact]
    public void IsComplete_accepts_a_fully_qualified_local_database_path()
    {
        var settings = new AppSettings
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "WeeklyPlanner", "weeklyplanner.db"),
            UserName = "Emilie",
        };

        Assert.True(settings.IsComplete());
    }

    [Fact]
    public void IsComplete_rejects_a_unc_database_path()
    {
        var settings = new AppSettings
        {
            DatabasePath = @"\\server\share\weeklyplanner.db",
            UserName = "Emilie",
        };

        Assert.False(settings.IsComplete());
    }


    [Fact]
    public void Normalize_converts_an_existing_directory_to_the_default_database_file()
    {
        var databaseDirectory = Path.Combine(_tempDirectory, "database-folder");
        Directory.CreateDirectory(databaseDirectory);
        var settings = new AppSettings
        {
            DatabasePath = databaseDirectory,
            UserName = "Emilie",
        };

        settings.Normalize();

        Assert.Equal(
            Path.Combine(databaseDirectory, AppSettings.DefaultDatabaseFileName),
            settings.DatabasePath);
        Assert.True(settings.IsComplete());
    }

    [Fact]
    public void Normalize_converts_a_path_ending_with_a_separator_to_the_default_database_file()
    {
        var databaseDirectory = Path.Combine(_tempDirectory, "new-database-folder") +
                                Path.DirectorySeparatorChar;
        var settings = new AppSettings
        {
            DatabasePath = databaseDirectory,
            UserName = "Emilie",
        };

        settings.Normalize();

        Assert.Equal(
            Path.Combine(databaseDirectory, AppSettings.DefaultDatabaseFileName),
            settings.DatabasePath);
        Assert.True(settings.IsComplete());
    }

    [Fact]
    public void Load_migrates_a_legacy_directory_path_in_memory()
    {
        var databaseDirectory = Path.Combine(_tempDirectory, "legacy-database-folder");
        Directory.CreateDirectory(databaseDirectory);
        var settingsPath = GetSettingsPath();
        File.WriteAllText(
            settingsPath,
            JsonSerializer.Serialize(new AppSettings
            {
                DatabasePath = databaseDirectory,
                UserName = "Emilie",
                PollingIntervalSeconds = 7,
            }));
        var service = new AppSettingsService(settingsPath);

        var settings = service.Load();

        Assert.Equal(
            Path.Combine(databaseDirectory, AppSettings.DefaultDatabaseFileName),
            settings.DatabasePath);
        Assert.True(settings.IsComplete());
    }

    [Fact]
    public void Normalize_expands_windows_environment_variables()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string variableName = "WEEKLYPLANNER_TEST_DATA";
        var variableValue = Path.Combine(_tempDirectory, "environment-data");
        Environment.SetEnvironmentVariable(variableName, variableValue);

        try
        {
            var settings = new AppSettings
            {
                DatabasePath = $"%{variableName}%\\weeklyplanner.db",
                UserName = "Emilie",
            };

            settings.Normalize();

            Assert.Equal(
                Path.Combine(variableValue, AppSettings.DefaultDatabaseFileName),
                settings.DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }


    [Fact]
    public void Save_and_load_roundtrip_preserves_theme_and_window_placement()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            DatabasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db"),
            UserName = "Emilie",
            ThemePreference = AppThemePreference.Dark,
            WindowWidth = 1280,
            WindowHeight = 820,
            WindowX = 120,
            WindowY = 80,
            WindowMaximized = true,
        };

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal(AppThemePreference.Dark, reloaded.ThemePreference);
        Assert.Equal(1280d, reloaded.WindowWidth);
        Assert.Equal(820d, reloaded.WindowHeight);
        Assert.Equal(120, reloaded.WindowX);
        Assert.Equal(80, reloaded.WindowY);
        Assert.True(reloaded.WindowMaximized);
    }

    [Fact]
    public void Normalize_repairs_invalid_theme_and_window_dimensions()
    {
        var settings = new AppSettings
        {
            ThemePreference = (AppThemePreference)999,
            WindowWidth = double.NaN,
            WindowHeight = 100,
            WindowX = int.MaxValue,
            WindowY = int.MinValue,
        };

        settings.Normalize();

        Assert.Equal(AppThemePreference.System, settings.ThemePreference);
        Assert.Equal(AppSettings.DefaultWindowWidth, settings.WindowWidth);
        Assert.Equal(AppSettings.MinimumWindowHeight, settings.WindowHeight);
        Assert.Null(settings.WindowX);
        Assert.Null(settings.WindowY);
    }

    [Fact]
    public void Clone_creates_an_independent_settings_instance()
    {
        var settings = new AppSettings
        {
            DatabasePath = Path.Combine(_tempDirectory, "data", "weeklyplanner.db"),
            UserName = "Emilie",
            PollingIntervalSeconds = 7,
        };

        var clone = settings.Clone();
        clone.UserName = "Altra persona";

        Assert.Equal("Emilie", settings.UserName);
        Assert.Equal("Altra persona", clone.UserName);
    }

    private AppSettingsService CreateService() => new(GetSettingsPath());

    private string GetSettingsPath() => Path.Combine(_tempDirectory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
