using System.Text;

namespace WeeklyPlanner.App.Diagnostics;

public sealed record ApplicationDiagnosticsSnapshot(
    DateTimeOffset CollectedAt,
    string ProductVersion,
    string Milestone,
    string DotNetRuntime,
    string AvaloniaVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string UserName,
    string MachineName,
    string SessionId,
    string ConnectionState,
    string LastSuccessfulSync,
    int ColumnCount,
    int CardCount,
    string DatabasePath,
    string DatabaseStatus,
    string DatabaseSize,
    string SchemaVersion,
    string ExpectedSchemaVersion,
    string SettingsFilePath,
    string LogDirectoryPath,
    string LogStatus,
    string LastLogFilePath)
{
    public string ContentSummary => $"{ColumnCount} colonne, {CardCount} card";

    public string ToPlainText()
    {
        var builder = new StringBuilder();
        Append(builder, "Raccolta", CollectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Append(builder, "Versione", ProductVersion);
        Append(builder, "Milestone", Milestone);
        Append(builder, ".NET", DotNetRuntime);
        Append(builder, "Avalonia", AvaloniaVersion);
        Append(builder, "Sistema operativo", OperatingSystem);
        Append(builder, "Architettura processo", ProcessArchitecture);
        Append(builder, "Utente", UserName);
        Append(builder, "Computer", MachineName);
        Append(builder, "Sessione", SessionId);
        Append(builder, "Stato board", ConnectionState);
        Append(builder, "Ultimo aggiornamento", LastSuccessfulSync);
        Append(builder, "Colonne", ColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, "Card", CardCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, "Database", DatabasePath);
        Append(builder, "Stato database", DatabaseStatus);
        Append(builder, "Dimensione database", DatabaseSize);
        Append(builder, "Schema database", SchemaVersion);
        Append(builder, "Schema atteso", ExpectedSchemaVersion);
        Append(builder, "Impostazioni", SettingsFilePath);
        Append(builder, "Cartella log", LogDirectoryPath);
        Append(builder, "Stato log", LogStatus);
        Append(builder, "Ultimo file log", LastLogFilePath);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, string value) =>
        builder.Append(label).Append(": ").AppendLine(value);
}
