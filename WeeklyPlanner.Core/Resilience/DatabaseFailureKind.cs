namespace WeeklyPlanner.Core.Resilience;

/// <summary>
/// Classificazione stabile degli errori di persistenza mostrabili dalla UI.
/// I codici SQLite restano confinati nel layer Core.
/// </summary>
public enum DatabaseFailureKind
{
    Contention,
    Unavailable,
    PermissionDenied,
    StorageFull,
    Corrupt,
    Schema,
    Unknown,
}
