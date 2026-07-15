using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Legge gli script SQL embedded nel progetto Core e ne valida l'univocità per versione.
/// </summary>
public sealed class EmbeddedDatabaseMigrationCatalog : IDatabaseMigrationCatalog
{
    private static readonly Regex MigrationNameRegex = new(
        @"\.(?<version>\d{4})_[^.]+\.sql$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Assembly _assembly;

    public EmbeddedDatabaseMigrationCatalog()
        : this(typeof(EmbeddedDatabaseMigrationCatalog).Assembly)
    {
    }

    public EmbeddedDatabaseMigrationCatalog(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public IReadOnlyList<DatabaseMigration> ReadMigrations()
    {
        var migrations = new List<DatabaseMigration>();

        foreach (var resourceName in _assembly.GetManifestResourceNames())
        {
            var match = MigrationNameRegex.Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            var version = int.Parse(
                match.Groups["version"].Value,
                CultureInfo.InvariantCulture);
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"La risorsa embedded della migrazione '{resourceName}' non è disponibile.");
            using var reader = new StreamReader(stream);
            migrations.Add(new DatabaseMigration(version, resourceName, reader.ReadToEnd()));
        }

        var duplicateVersion = migrations
            .GroupBy(migration => migration.Version)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException(
                $"Sono presenti più migrazioni per la versione {duplicateVersion.Key}.");
        }

        return migrations
            .OrderBy(migration => migration.Version)
            .ToList();
    }
}
