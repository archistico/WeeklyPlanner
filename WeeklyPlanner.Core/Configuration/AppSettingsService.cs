using System.Text.Json;

namespace WeeklyPlanner.Core.Configuration;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsFilePath;

    public string SettingsFilePath => _settingsFilePath;

    public string SettingsDirectoryPath =>
        Path.GetDirectoryName(_settingsFilePath) ?? Environment.CurrentDirectory;

    public AppSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WeeklyPlanner",
            "settings.json"))
    {
    }

    /// <summary>
    /// Costruttore usato dai test per puntare a un percorso temporaneo.
    /// </summary>
    public AppSettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = settingsFilePath;
    }

    /// <summary>
    /// Carica la configurazione locale. Un file mancante, vuoto, corrotto o temporaneamente
    /// illeggibile non impedisce l'avvio: viene restituita una configurazione vuota e l'app
    /// ripropone l'onboarding.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Salva nello stesso filesystem tramite file temporaneo e sostituzione finale, evitando
    /// di lasciare un settings.json parzialmente scritto in caso di interruzione.
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Cleanup best effort: il file principale è già stato salvato o l'errore originale
                // deve restare quello rilevante per il chiamante.
            }
            catch (UnauthorizedAccessException)
            {
                // Come sopra: non mascherare l'esito dell'operazione principale.
            }
        }
    }
}
