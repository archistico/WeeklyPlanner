namespace WeeklyPlanner.Core.Models;

public sealed class CardEditLockAcquisitionResult
{
    public bool Acquired { get; }

    public CardEditLock CurrentLock { get; }

    public CardEditLockAcquisitionResult(bool acquired, CardEditLock currentLock)
    {
        ArgumentNullException.ThrowIfNull(currentLock);

        Acquired = acquired;
        CurrentLock = currentLock;
    }
}
