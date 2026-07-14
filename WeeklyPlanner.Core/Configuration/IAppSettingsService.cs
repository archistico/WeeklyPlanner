namespace WeeklyPlanner.Core.Configuration;

public interface IAppSettingsService
{
    string SettingsFilePath { get; }

    string SettingsDirectoryPath { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}
