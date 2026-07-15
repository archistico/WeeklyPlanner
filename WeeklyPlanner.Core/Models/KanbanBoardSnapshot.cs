namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Snapshot coerente della board letto all'interno di una singola transazione SQLite.
/// Contiene tutti i dati necessari a rappresentare il kanban senza combinare letture
/// appartenenti a revisioni differenti.
/// </summary>
public sealed record KanbanBoardSnapshot(
    long Revision,
    IReadOnlyList<Column> Columns,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<PriorityDefinition> Priorities,
    IReadOnlyList<CardTypeDefinition> CardTypes,
    IReadOnlyList<PriorityTypeDeadline> DeadlineRules,
    IReadOnlyList<CardEditLock> ActiveLocks);
