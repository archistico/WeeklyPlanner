using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.Diagnostics;

public interface IApplicationDiagnosticsProvider
{
    Task<ApplicationDiagnosticsSnapshot> CollectAsync(
        AppSettings settings,
        BoardRuntimeDiagnostics boardRuntime,
        CancellationToken cancellationToken = default);
}
