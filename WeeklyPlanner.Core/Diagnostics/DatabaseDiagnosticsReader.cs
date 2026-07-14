using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.Core.Diagnostics;

public sealed class DatabaseDiagnosticsReader : IDatabaseDiagnosticsReader
{
    public async Task<DatabaseDiagnosticsInfo> ReadAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var normalizedPath = AppSettings.NormalizeDatabasePath(databasePath);
        if (!AppSettings.IsSupportedLocalDatabasePath(normalizedPath))
        {
            return new DatabaseDiagnosticsInfo(
                FileExists: false,
                FileSizeBytes: null,
                SchemaVersion: null,
                ErrorMessage: "Il percorso del database non è un file locale assoluto valido.");
        }

        if (!File.Exists(normalizedPath))
        {
            return new DatabaseDiagnosticsInfo(
                FileExists: false,
                FileSizeBytes: null,
                SchemaVersion: null,
                ErrorMessage: "Il file del database non esiste.");
        }

        var fileSize = new FileInfo(normalizedPath).Length;
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = normalizedPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Default,
                Pooling = false,
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";
            var versionTableExists = Convert.ToInt64(
                await tableCommand.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture) > 0;

            if (!versionTableExists)
            {
                return new DatabaseDiagnosticsInfo(
                    FileExists: true,
                    FileSizeBytes: fileSize,
                    SchemaVersion: 0,
                    ErrorMessage: null);
            }

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
            var version = Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);

            return new DatabaseDiagnosticsInfo(
                FileExists: true,
                FileSizeBytes: fileSize,
                SchemaVersion: version,
                ErrorMessage: null);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new DatabaseDiagnosticsInfo(
                FileExists: true,
                FileSizeBytes: fileSize,
                SchemaVersion: null,
                ErrorMessage: ex.Message);
        }
    }
}
