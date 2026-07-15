namespace WeeklyPlanner.Core.Repositories;

public sealed class CardCatalogValidationException : Exception
{
    public CardCatalogValidationException(string message)
        : base(message)
    {
    }
}

public sealed class CardCatalogConcurrencyException : Exception
{
    public string CatalogName { get; }

    public long? ItemId { get; }

    public int? ExpectedVersion { get; }

    public int? ActualVersion { get; }

    public CardCatalogConcurrencyException(
        string catalogName,
        long itemId,
        int expectedVersion,
        int? actualVersion)
        : base(actualVersion is null
            ? $"La voce {itemId} del catalogo {catalogName} non esiste più."
            : $"La voce {itemId} del catalogo {catalogName} è stata modificata da un'altra istanza " +
              $"(versione attesa {expectedVersion}, versione corrente {actualVersion}).")
    {
        CatalogName = catalogName;
        ItemId = itemId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public CardCatalogConcurrencyException(string catalogName)
        : base($"Il catalogo {catalogName} è cambiato in un'altra istanza. I dati sono stati ricaricati.")
    {
        CatalogName = catalogName;
    }
}

public sealed class CardCatalogItemInUseException : Exception
{
    public string CatalogName { get; }

    public long ItemId { get; }

    public int UsageCount { get; }

    public CardCatalogItemInUseException(
        string catalogName,
        long itemId,
        int usageCount,
        string displayName)
        : base(
            $"Non è possibile eliminare '{displayName}': è assegnato a {usageCount} " +
            "card. Disattivalo invece di eliminarlo.")
    {
        CatalogName = catalogName;
        ItemId = itemId;
        UsageCount = usageCount;
    }
}
