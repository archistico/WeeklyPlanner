namespace WeeklyPlanner.Core.Models;

public sealed class CardTypeDefinition
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public bool IsDefault { get; set; }

    public int Version { get; set; } = 1;

    /// <summary>Chiave immutabile per le tipologie create dal sistema.</summary>
    public string? SystemKey { get; set; }

    public bool IsSystem { get; set; }

    /// <summary>Numero di card attualmente assegnate alla fascia.</summary>
    public int CardCount { get; set; }
}
