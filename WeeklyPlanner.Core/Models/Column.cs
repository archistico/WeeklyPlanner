namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Rappresenta una colonna della board (nell'MVP: colonne fisse, es. Lun-Dom + Backlog).
/// La personalizzazione delle colonne (rinomina/aggiunta/rimozione) è backlog aperto,
/// non ancora implementata: vedi §12 del documento di progetto.
/// </summary>
public sealed class Column
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
