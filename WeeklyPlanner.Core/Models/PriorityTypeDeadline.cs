namespace WeeklyPlanner.Core.Models;

public sealed class PriorityTypeDeadline
{
    public long PriorityId { get; set; }

    public long CardTypeId { get; set; }

    public int DueHours { get; set; }

    public int Version { get; set; } = 1;
}
