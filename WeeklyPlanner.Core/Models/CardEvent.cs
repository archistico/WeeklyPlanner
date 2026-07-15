namespace WeeklyPlanner.Core.Models;

public sealed class CardEvent
{
    public long Id { get; set; }

    public string CardStableId { get; set; } = string.Empty;

    public long? CardId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string OccurredAtUtc { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    public string? MachineName { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string DataJson { get; set; } = "{}";

    public int FormatVersion { get; set; } = 1;
}
