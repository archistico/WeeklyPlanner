using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Diagnostics;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.App.Diagnostics;

public sealed class ApplicationDiagnosticsProvider : IApplicationDiagnosticsProvider
{
    private readonly IAppLogger _logger;
    private readonly IAppSettingsService _settingsService;
    private readonly IApplicationSession _session;
    private readonly IDatabaseDiagnosticsReader _databaseDiagnosticsReader;
    private readonly IClock _clock;

    public ApplicationDiagnosticsProvider(
        IAppLogger logger,
        IAppSettingsService settingsService,
        IApplicationSession session,
        IDatabaseDiagnosticsReader databaseDiagnosticsReader,
        IClock clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _databaseDiagnosticsReader = databaseDiagnosticsReader ??
                                     throw new ArgumentNullException(nameof(databaseDiagnosticsReader));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ApplicationDiagnosticsSnapshot> CollectAsync(
        AppSettings settings,
        BoardRuntimeDiagnostics boardRuntime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(boardRuntime);

        var normalized = settings.Clone();
        normalized.Normalize();
        var activeDatabasePath = AppSettings.NormalizeDatabasePath(boardRuntime.ActiveDatabasePath);
        var database = await _databaseDiagnosticsReader.ReadAsync(
            activeDatabasePath,
            cancellationToken);

        var databaseStatus = database.FileExists
            ? database.ErrorMessage is null ? "Disponibile" : $"Errore: {database.ErrorMessage}"
            : database.ErrorMessage ?? "Non disponibile";
        var databaseSize = database.FileSizeBytes is long bytes
            ? FormatBytes(bytes)
            : "Non disponibile";
        var schemaVersion = database.SchemaVersion?.ToString(CultureInfo.InvariantCulture)
                            ?? "Non disponibile";
        var lastSync = boardRuntime.LastSuccessfulSyncAt is DateTimeOffset sync
            ? sync.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
            : "Mai";
        var logStatus = _logger.IsAvailable
            ? "Disponibile"
            : $"Non disponibile{FormatOptionalDetail(_logger.LastFailureMessage)}";
        var sessionId = _session.SessionId.Length <= 8
            ? _session.SessionId
            : _session.SessionId[..8];

        return new ApplicationDiagnosticsSnapshot(
            CollectedAt: _clock.Now,
            ProductVersion: ApplicationVersionInfo.ProductVersion,
            Milestone: ApplicationVersionInfo.Milestone,
            DotNetRuntime: RuntimeInformation.FrameworkDescription,
            AvaloniaVersion: typeof(Application).Assembly.GetName().Version?.ToString() ?? "sconosciuta",
            OperatingSystem: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            UserName: normalized.UserName,
            MachineName: _session.MachineName,
            SessionId: sessionId,
            ConnectionState: boardRuntime.ConnectionState,
            LastSuccessfulSync: lastSync,
            ColumnCount: boardRuntime.ColumnCount,
            CardCount: boardRuntime.CardCount,
            DatabasePath: activeDatabasePath,
            DatabaseStatus: databaseStatus,
            DatabaseSize: databaseSize,
            SchemaVersion: schemaVersion,
            ExpectedSchemaVersion: DatabaseInitializer.ExpectedSchemaVersion.ToString(CultureInfo.InvariantCulture),
            SettingsFilePath: _settingsService.SettingsFilePath,
            LogDirectoryPath: _logger.LogDirectoryPath,
            LogStatus: logStatus,
            LastLogFilePath: _logger.LastLogFilePath ?? "Nessun file scritto");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatOptionalDetail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";
}
