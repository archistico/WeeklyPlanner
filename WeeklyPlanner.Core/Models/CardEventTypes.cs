namespace WeeklyPlanner.Core.Models;

public static class CardEventTypes
{
    public const string Imported = "Imported";
    public const string WorkflowMigrated = "WorkflowMigrated";
    public const string TypeMigrated = "TypeMigrated";
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string PriorityChanged = "PriorityChanged";
    public const string TypeChanged = "TypeChanged";
    public const string Moved = "Moved";
    public const string Reordered = "Reordered";
    public const string Deleted = "Deleted";
}
