namespace WeeklyPlanner.App.Interaction;

public enum CardMoveDirection
{
    Up,
    Down,
    PreviousColumn,
    NextColumn,
}

public readonly record struct CardMovePlan(int TargetColumnIndex, int TargetIndex);

/// <summary>
/// Calcola gli indici da passare al repository per gli spostamenti da tastiera.
/// L'indice nella stessa colonna segue la semantica del drag&drop: rappresenta
/// la posizione di inserimento prima della rimozione della card sorgente.
/// </summary>
public static class CardMovePlanner
{
    public static bool WouldChangePosition(
        int sourceColumnIndex,
        int sourceCardIndex,
        int sourceColumnCount,
        int targetColumnIndex,
        int targetIndex)
    {
        if (sourceColumnCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceColumnCount));
        }

        if (sourceCardIndex < 0 || sourceCardIndex >= sourceColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceCardIndex));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        if (sourceColumnIndex != targetColumnIndex)
        {
            return true;
        }

        var adjustedIndex = sourceCardIndex < targetIndex
            ? targetIndex - 1
            : targetIndex;
        adjustedIndex = Math.Clamp(adjustedIndex, 0, sourceColumnCount - 1);
        return adjustedIndex != sourceCardIndex;
    }

    public static bool TryCreate(
        int sourceColumnIndex,
        int sourceCardIndex,
        IReadOnlyList<int> cardCountsByColumn,
        CardMoveDirection direction,
        out CardMovePlan plan)
    {
        ArgumentNullException.ThrowIfNull(cardCountsByColumn);

        if (sourceColumnIndex < 0 || sourceColumnIndex >= cardCountsByColumn.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceColumnIndex));
        }

        var sourceColumnCount = cardCountsByColumn[sourceColumnIndex];
        if (sourceCardIndex < 0 || sourceCardIndex >= sourceColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceCardIndex));
        }

        switch (direction)
        {
            case CardMoveDirection.Up when sourceCardIndex > 0:
                plan = new CardMovePlan(sourceColumnIndex, sourceCardIndex - 1);
                return true;

            case CardMoveDirection.Down when sourceCardIndex < sourceColumnCount - 1:
                // Il repository rimuove prima la card e, se la sorgente precede la destinazione,
                // sottrae uno all'indice. +2 produce quindi uno spostamento finale di +1.
                plan = new CardMovePlan(sourceColumnIndex, sourceCardIndex + 2);
                return true;

            case CardMoveDirection.PreviousColumn when sourceColumnIndex > 0:
            {
                var targetColumnIndex = sourceColumnIndex - 1;
                plan = new CardMovePlan(
                    targetColumnIndex,
                    Math.Min(sourceCardIndex, cardCountsByColumn[targetColumnIndex]));
                return true;
            }

            case CardMoveDirection.NextColumn when sourceColumnIndex < cardCountsByColumn.Count - 1:
            {
                var targetColumnIndex = sourceColumnIndex + 1;
                plan = new CardMovePlan(
                    targetColumnIndex,
                    Math.Min(sourceCardIndex, cardCountsByColumn[targetColumnIndex]));
                return true;
            }

            default:
                plan = default;
                return false;
        }
    }
}
