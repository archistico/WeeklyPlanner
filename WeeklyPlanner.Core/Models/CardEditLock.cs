namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Lease applicativo che riserva temporaneamente una card a una singola sessione di editing.
/// I timestamp sono UTC ISO 8601 e vengono prodotti dal repository con formato uniforme.
/// </summary>
public sealed class CardEditLock
{
    public long CardId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? MachineName { get; set; }

    public string AcquiredAtUtc { get; set; } = string.Empty;

    public string LastHeartbeatUtc { get; set; } = string.Empty;

    public string ExpiresAtUtc { get; set; } = string.Empty;
}
