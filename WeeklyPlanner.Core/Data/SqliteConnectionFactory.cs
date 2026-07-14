using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Crea connessioni SQLite configurate in modo coerente per tutta l'applicazione.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private const int BusyTimeoutMilliseconds = 5000;

    private readonly string _databasePath;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var normalizedPath = AppSettings.NormalizeDatabasePath(databasePath);
        if (!AppSettings.IsSupportedLocalDatabasePath(normalizedPath))
        {
            throw new ArgumentException(
                "Il percorso del database deve indicare un file locale con percorso assoluto.",
                nameof(databasePath));
        }

        _databasePath = normalizedPath;
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection Create()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                $"Impossibile determinare la cartella del database '{_databasePath}'.");
        }

        if (File.Exists(directory))
        {
            throw new InvalidOperationException(
                $"La cartella del database '{directory}' è in realtà un file.");
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Impossibile creare o accedere alla cartella del database '{directory}'.",
                ex);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);

        try
        {
            connection.Open();
            ExecutePragma(connection, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
            ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ExecutePragma(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
