namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Rappresenta uno stato operativo del kanban. Dallo schema v5 le cinque colonne
/// sono voci di sistema identificate da una chiave stabile.
/// </summary>
public sealed class Column
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? SystemKey { get; set; }

    public bool IsSystem { get; set; }
}
