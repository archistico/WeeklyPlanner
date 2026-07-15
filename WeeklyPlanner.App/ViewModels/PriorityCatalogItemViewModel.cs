using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class PriorityCatalogItemViewModel(PriorityDefinition model) : ViewModelBase
{
    public long Id { get; } = model.Id;

    public string Code { get; } = model.Code;

    public string Name { get; } = model.Name;

    public string DisplayName => $"{Code} — {Name}";

    public int DefaultDueHours { get; } = model.DefaultDueHours;

    public string DeadlineText => DefaultDueHours % 24 == 0
        ? $"{DefaultDueHours / 24} giorni"
        : $"{DefaultDueHours} ore";

    public int SortOrder { get; } = model.SortOrder;

    public bool IsActive { get; } = model.IsActive;

    public bool IsInactive => !IsActive;

    public bool IsDefault { get; } = model.IsDefault;

    public int Version { get; } = model.Version;

    public PriorityDefinition Model { get; } = model;
}
