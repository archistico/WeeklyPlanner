namespace WeeklyPlanner.Core.Configuration;

/// <summary>
/// Configurazione locale salvata in %APPDATA%/WeeklyPlanner/settings.json.
/// Contiene soltanto preferenze locali e il percorso del database SQLite locale.
/// </summary>
public sealed class AppSettings
{
    public const string DefaultDatabaseFileName = "weeklyplanner.db";
    public const int DefaultPollingIntervalSeconds = 7;
    public const int MinimumPollingIntervalSeconds = 3;
    public const int MaximumPollingIntervalSeconds = 60;

    public string DatabasePath { get; set; } = string.Empty;

    public static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeeklyPlanner",
            "Data",
            DefaultDatabaseFileName);

    public string UserName { get; set; } = string.Empty;

    public int PollingIntervalSeconds { get; set; } = DefaultPollingIntervalSeconds;

    public bool IsComplete() =>
        IsSupportedLocalDatabasePath(DatabasePath) && !string.IsNullOrWhiteSpace(UserName);

    public static bool IsSupportedLocalDatabasePath(string? databasePath)
    {
        var normalizedPath = NormalizeDatabasePath(databasePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || IsUncPath(normalizedPath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(normalizedPath) || Directory.Exists(normalizedPath))
            {
                return false;
            }

            var fileName = Path.GetFileName(normalizedPath);
            return !string.IsNullOrWhiteSpace(fileName);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizza anche le configurazioni create dalle versioni precedenti:
    /// - espande variabili d'ambiente come %LOCALAPPDATA%;
    /// - rimuove eventuali virgolette esterne;
    /// - se il valore indica una cartella esistente o termina con un separatore,
    ///   aggiunge automaticamente il nome predefinito del database.
    /// </summary>
    public static string NormalizeDatabasePath(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return string.Empty;
        }

        var normalizedPath = Environment
            .ExpandEnvironmentVariables(databasePath.Trim().Trim('"'));

        try
        {
            if (!IsUncPath(normalizedPath) &&
                (EndsWithDirectorySeparator(normalizedPath) || Directory.Exists(normalizedPath)))
            {
                normalizedPath = Path.Combine(normalizedPath, DefaultDatabaseFileName);
            }

            if (Path.IsPathFullyQualified(normalizedPath))
            {
                normalizedPath = Path.GetFullPath(normalizedPath);
            }
        }
        catch (ArgumentException)
        {
            // Il percorso resterà non valido e verrà rifiutato da IsSupportedLocalDatabasePath.
        }
        catch (NotSupportedException)
        {
            // Come sopra: non impedire il caricamento dei settings corrotti o legacy.
        }
        catch (PathTooLongException)
        {
            // Come sopra: la validazione successiva impedirà l'utilizzo del percorso.
        }

        return normalizedPath;
    }

    public void Normalize()
    {
        DatabasePath = NormalizeDatabasePath(DatabasePath);
        UserName = UserName?.Trim() ?? string.Empty;
        PollingIntervalSeconds = Math.Clamp(
            PollingIntervalSeconds,
            MinimumPollingIntervalSeconds,
            MaximumPollingIntervalSeconds);
    }

    private static bool EndsWithDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ||
        path.EndsWith(Path.AltDirectorySeparatorChar);

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);
}
