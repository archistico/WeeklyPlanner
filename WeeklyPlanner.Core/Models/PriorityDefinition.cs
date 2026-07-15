namespace WeeklyPlanner.Core.Models;

public sealed class PriorityDefinition
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int DefaultDueHours { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public bool IsDefault { get; set; }

    public int Version { get; set; } = 1;
}
