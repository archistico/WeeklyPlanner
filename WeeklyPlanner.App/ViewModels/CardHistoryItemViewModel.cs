using System.Globalization;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class CardHistoryItemViewModel
{
    public long Id { get; }

    public string EventType { get; }

    public string EventTypeText { get; }

    public string EventSymbol { get; }

    public string OccurredAtText { get; }

    public string ActorText { get; }

    public string Summary { get; }

    public bool IsMovement { get; }

    public string MovementContextText => IsMovement
        ? "Movimento bidimensionale: fascia, stato e posizione nella cella"
        : string.Empty;

    public CardHistoryItemViewModel(CardEvent cardEvent)
    {
        ArgumentNullException.ThrowIfNull(cardEvent);

        Id = cardEvent.Id;
        EventType = cardEvent.EventType;
        EventTypeText = ResolveEventTypeText(cardEvent.EventType);
        EventSymbol = ResolveEventSymbol(cardEvent.EventType);
        OccurredAtText = FormatLocalDateTime(cardEvent.OccurredAtUtc);
        ActorText = BuildActorText(cardEvent.UserName, cardEvent.MachineName);
        Summary = string.IsNullOrWhiteSpace(cardEvent.Summary)
            ? ResolveFallbackSummary(cardEvent.EventType)
            : cardEvent.Summary.Trim();
        IsMovement = string.Equals(cardEvent.EventType, CardEventTypes.Moved, StringComparison.Ordinal) ||
                     string.Equals(cardEvent.EventType, CardEventTypes.Reordered, StringComparison.Ordinal);
    }

    private static string ResolveEventTypeText(string eventType) => eventType switch
    {
        CardEventTypes.Imported => "Importazione",
        CardEventTypes.WorkflowMigrated => "Migrazione stato",
        CardEventTypes.TypeMigrated => "Migrazione fascia",
        CardEventTypes.Created => "Creazione",
        CardEventTypes.Updated => "Modifica",
        CardEventTypes.PriorityChanged => "Priorità",
        CardEventTypes.TypeChanged => "Fascia",
        CardEventTypes.Moved => "Movimento",
        CardEventTypes.Reordered => "Riordino",
        CardEventTypes.Deleted => "Eliminazione",
        _ => "Evento",
    };

    private static string ResolveEventSymbol(string eventType) => eventType switch
    {
        CardEventTypes.Created => "+",
        CardEventTypes.Updated => "✎",
        CardEventTypes.PriorityChanged => "!",
        CardEventTypes.TypeChanged => "≡",
        CardEventTypes.Moved => "↔",
        CardEventTypes.Reordered => "↕",
        CardEventTypes.Deleted => "×",
        CardEventTypes.Imported or CardEventTypes.WorkflowMigrated or CardEventTypes.TypeMigrated => "⇢",
        _ => "•",
    };

    private static string ResolveFallbackSummary(string eventType) => eventType switch
    {
        CardEventTypes.Created => "Card creata.",
        CardEventTypes.Updated => "Card modificata.",
        CardEventTypes.PriorityChanged => "Priorità della card modificata.",
        CardEventTypes.TypeChanged => "Fascia della card modificata.",
        CardEventTypes.Moved => "Card spostata in un'altra cella.",
        CardEventTypes.Reordered => "Ordine della card modificato nella cella.",
        CardEventTypes.Deleted => "Card eliminata.",
        _ => "Evento registrato.",
    };

    private static string BuildActorText(string userName, string? machineName)
    {
        var normalizedUserName = string.IsNullOrWhiteSpace(userName)
            ? "Utente sconosciuto"
            : userName.Trim();
        return string.IsNullOrWhiteSpace(machineName)
            ? normalizedUserName
            : $"{normalizedUserName} · {machineName.Trim()}";
    }

    private static string FormatLocalDateTime(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return "Data non disponibile";
        }

        return parsed.ToLocalTime().ToString(
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.CurrentCulture);
    }
}
