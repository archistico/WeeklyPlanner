using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.App.ViewModels;

public sealed class DatabaseBackupItemViewModel
{
    public DatabaseBackupInfo Model { get; }

    public string FilePath => Model.FilePath;

    public string FileName => Model.FileName;

    public string KindText => Model.Kind == DatabaseBackupKind.PreRestore
        ? "Preventivo restore"
        : "Manuale";

    public string CreatedAtText => Model.CreatedAtUtc == DateTimeOffset.MinValue
        ? "Data non disponibile"
        : Model.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    public string SizeText => FormatSize(Model.SizeBytes);

    public string SchemaVersionText => Model.SchemaVersion is null
        ? "Schema non rilevato"
        : $"Schema v{Model.SchemaVersion}";

    public string IntegrityText => Model.IntegrityStatus switch
    {
        DatabaseBackupIntegrityStatus.Valid => "Valido",
        DatabaseBackupIntegrityStatus.Corrupt => "Corrotto",
        DatabaseBackupIntegrityStatus.Incompatible => "Incompatibile",
        DatabaseBackupIntegrityStatus.Missing => "Mancante",
        _ => "Errore",
    };

    public string IntegrityDetails => Model.IntegrityMessage;

    public bool CanRestore => Model.CanRestore;

    public bool IsValid => Model.IntegrityStatus == DatabaseBackupIntegrityStatus.Valid;

    public bool HasProblem => !IsValid;

    public DatabaseBackupItemViewModel(DatabaseBackupInfo model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var value = bytes / 1024d;
        if (value < 1024)
        {
            return $"{value:0.0} KB";
        }

        value /= 1024d;
        return value < 1024
            ? $"{value:0.0} MB"
            : $"{value / 1024d:0.0} GB";
    }
}
