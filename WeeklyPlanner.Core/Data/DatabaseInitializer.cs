using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Inizializza il database e applica in ordine tutte le migrazioni embedded mancanti.
/// </summary>
public sealed class DatabaseInitializer
{
    public const int ExpectedSchemaVersion = 3;

    private static readonly Regex MigrationNameRegex = new(
        @"\.(?<version>\d{4})_[^.]+\.sql$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void EnsureInitialized()
    {
        using var connection = _connectionFactory.Create();

        ConfigurePersistentJournalMode(connection);
        EnsureVersionTable(connection);

        var currentVersion = GetCurrentVersion(connection);
        if (currentVersion > ExpectedSchemaVersion)
        {
            throw new SchemaVersionMismatchException(currentVersion, ExpectedSchemaVersion);
        }

        if (currentVersion == ExpectedSchemaVersion)
        {
            return;
        }

        var migrations = ReadEmbeddedMigrations()
            .Where(migration => migration.Version > currentVersion &&
                                migration.Version <= ExpectedSchemaVersion)
            .OrderBy(migration => migration.Version)
            .ToList();

        for (var version = currentVersion + 1; version <= ExpectedSchemaVersion; version++)
        {
            if (migrations.All(migration => migration.Version != version))
            {
                throw new MissingMigrationException(version);
            }
        }

        foreach (var migration in migrations)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: transaction);
            connection.Execute("DELETE FROM SchemaVersion;", transaction: transaction);
            connection.Execute(
                "INSERT INTO SchemaVersion (Version) VALUES (@Version);",
                new { migration.Version },
                transaction);
            transaction.Commit();
        }
    }

    private static void ConfigurePersistentJournalMode(System.Data.IDbConnection connection)
    {
        var journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode = DELETE;");
        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite non ha applicato journal_mode=DELETE (modalità restituita: '{journalMode}').");
        }
    }

    private static void EnsureVersionTable(System.Data.IDbConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL
            );

            INSERT INTO SchemaVersion (Version)
            SELECT 0
            WHERE NOT EXISTS (SELECT 1 FROM SchemaVersion);
            """);
    }

    private static int GetCurrentVersion(System.Data.IDbConnection connection) =>
        connection.ExecuteScalar<int>("SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;");

    private static IReadOnlyList<MigrationScript> ReadEmbeddedMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<MigrationScript>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            var match = MigrationNameRegex.Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            var version = int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture);
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"La risorsa embedded della migrazione '{resourceName}' non è disponibile.");
            using var reader = new StreamReader(stream);
            migrations.Add(new MigrationScript(version, resourceName, reader.ReadToEnd()));
        }

        var duplicateVersion = migrations
            .GroupBy(migration => migration.Version)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException(
                $"Sono presenti più migrazioni per la versione {duplicateVersion.Key}.");
        }

        return migrations;
    }

    private sealed record MigrationScript(int Version, string ResourceName, string Sql);
}

public sealed class SchemaVersionMismatchException : Exception
{
    public int FoundVersion { get; }
    public int ExpectedVersion { get; }

    public SchemaVersionMismatchException(int foundVersion, int expectedVersion)
        : base($"Lo schema del database (v{foundVersion}) è più recente di quello atteso da " +
               $"questa versione dell'app (v{expectedVersion}). Aggiorna l'applicazione prima di continuare.")
    {
        FoundVersion = foundVersion;
        ExpectedVersion = expectedVersion;
    }
}

public sealed class MissingMigrationException : Exception
{
    public int MissingVersion { get; }

    public MissingMigrationException(int missingVersion)
        : base($"Manca lo script embedded per la migrazione dello schema v{missingVersion}.")
    {
        MissingVersion = missingVersion;
    }
}
