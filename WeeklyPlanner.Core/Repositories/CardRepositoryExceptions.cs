namespace WeeklyPlanner.Core.Repositories;

public sealed class CardConcurrencyException : Exception
{
    public long CardId { get; }

    public int ExpectedVersion { get; }

    public int ActualVersion { get; }

    public CardConcurrencyException(long cardId, int expectedVersion, int actualVersion)
        : base(
            $"La card {cardId} è stata modificata dopo l'inizio dell'editing " +
            $"(versione attesa {expectedVersion}, versione corrente {actualVersion}).")
    {
        CardId = cardId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

public sealed class CardEditLockException : Exception
{
    public long CardId { get; }

    public string? LockOwnerName { get; }

    public CardEditLockException(long cardId, string message, string? lockOwnerName = null)
        : base(message)
    {
        CardId = cardId;
        LockOwnerName = lockOwnerName;
    }
}

public sealed class CardValidationException : Exception
{
    public CardValidationException(string message)
        : base(message)
    {
    }
}
