namespace WeeklyPlanner.Core.Data;

/// <summary>
/// Rappresenta uno script di migrazione ordinato per versione dello schema.
/// </summary>
public sealed record DatabaseMigration(int Version, string ResourceName, string Sql);
