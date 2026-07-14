namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Rappresenta una card della board. Corrisponde 1:1 alla riga della tabella Cards
/// (vedi le migrazioni in Data/Sql).
/// </summary>
public sealed class Card
{
    public const int MaxTitleLength = 160;

    public long Id { get; set; }

    public long ColumnId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Timestamp UTC in formato ISO 8601 usato come metadato dell'ultima modifica.
    /// Il polling usa la revisione monotona di BoardState e non dipende da questo valore.
    /// </summary>
    public string UpdatedAtUtc { get; set; } = string.Empty;

    /// <summary>
    /// Revisione ottimistica della singola card. Viene incrementata a ogni salvataggio
    /// del contenuto e confrontata con la versione letta all'inizio dell'editing.
    /// </summary>
    public int Version { get; set; } = 1;
}
