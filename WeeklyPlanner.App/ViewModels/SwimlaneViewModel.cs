using Avalonia.Media;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

/// <summary>
/// Proiezione visuale di una tipologia come fascia orizzontale della board.
/// Ogni fascia espone esattamente le cinque celle di sistema del workflow.
/// </summary>
public sealed class SwimlaneViewModel : ViewModelBase
{
    private readonly IReadOnlyDictionary<long, SwimlaneCellViewModel> _cellsByColumnId;

    public CardTypeDefinition Model { get; }

    public long Id => Model.Id;

    public string Name => Model.Name;

    public string ColorHex => Model.ColorHex;

    public IBrush ColorBrush { get; }

    public int SortOrder => Model.SortOrder;

    public bool IsActive => Model.IsActive;

    public bool IsInactive => !IsActive;

    public bool IsSystem => Model.IsSystem;

    public bool IsGeneric => string.Equals(
        Model.SystemKey,
        SystemCardTypeKeys.Generic,
        StringComparison.Ordinal);

    public SwimlaneCellViewModel Backlog { get; }

    public SwimlaneCellViewModel Todo { get; }

    public SwimlaneCellViewModel InProgress { get; }

    public SwimlaneCellViewModel Testing { get; }

    public SwimlaneCellViewModel Done { get; }

    public IReadOnlyList<SwimlaneCellViewModel> Cells { get; }

    public int CardCount => Cells.Sum(cell => cell.Cards.Count);

    public SwimlaneViewModel(
        CardTypeDefinition model,
        IEnumerable<ColumnViewModel> columns)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(columns);

        Model = model;
        ColorBrush = ColorHexParser.ToBrush(model.ColorHex);

        var columnsBySystemKey = columns
            .Where(column => !string.IsNullOrWhiteSpace(column.SystemKey))
            .ToDictionary(column => column.SystemKey!, StringComparer.Ordinal);

        Backlog = CreateCell(columnsBySystemKey, WorkflowColumnKeys.Backlog);
        Todo = CreateCell(columnsBySystemKey, WorkflowColumnKeys.Todo);
        InProgress = CreateCell(columnsBySystemKey, WorkflowColumnKeys.InProgress);
        Testing = CreateCell(columnsBySystemKey, WorkflowColumnKeys.Testing);
        Done = CreateCell(columnsBySystemKey, WorkflowColumnKeys.Done);

        Cells =
        [
            Backlog,
            Todo,
            InProgress,
            Testing,
            Done,
        ];
        _cellsByColumnId = Cells.ToDictionary(cell => cell.ColumnId);
    }

    public SwimlaneCellViewModel GetCell(long columnId)
    {
        if (_cellsByColumnId.TryGetValue(columnId, out var cell))
        {
            return cell;
        }

        throw new KeyNotFoundException(
            $"La colonna {columnId} non appartiene alla fascia {Name}.");
    }

    private SwimlaneCellViewModel CreateCell(
        IReadOnlyDictionary<string, ColumnViewModel> columnsBySystemKey,
        string systemKey)
    {
        if (!columnsBySystemKey.TryGetValue(systemKey, out var column))
        {
            throw new InvalidOperationException(
                $"La board non contiene la colonna di sistema '{systemKey}'.");
        }

        return new SwimlaneCellViewModel(Id, Name, IsGeneric, IsActive, column);
    }
}
