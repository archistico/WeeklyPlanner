namespace WeeklyPlanner.Core.Models;

/// <summary>
/// Chiavi stabili delle colonne operative del kanban. I nomi visualizzati possono essere
/// presentati dalla UI, mentre persistenza e logica applicativa usano queste chiavi immutabili.
/// </summary>
public static class WorkflowColumnKeys
{
    public const string Backlog = "backlog";
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Testing = "testing";
    public const string Done = "done";

    public static IReadOnlyList<string> Ordered { get; } =
    [
        Backlog,
        Todo,
        InProgress,
        Testing,
        Done,
    ];
}
