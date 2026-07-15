using Avalonia.Media;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class CardTypeCatalogItemViewModel : ViewModelBase
{
    public long Id { get; }

    public string Name { get; }

    public string ColorHex { get; }

    public IBrush ColorBrush { get; }

    public int SortOrder { get; }

    public bool IsActive { get; }

    public bool IsInactive => !IsActive;

    public bool IsDefault { get; }

    public int Version { get; }

    public string? SystemKey { get; }

    public bool IsSystem { get; }

    public int CardCount { get; }

    public string CardCountText => CardCount == 1 ? "1 card" : $"{CardCount} card";

    public CardTypeDefinition Model { get; }

    public CardTypeCatalogItemViewModel(CardTypeDefinition model)
    {
        Model = model;
        Id = model.Id;
        Name = model.Name;
        ColorHex = model.ColorHex;
        ColorBrush = ColorHexParser.ToBrush(model.ColorHex);
        SortOrder = model.SortOrder;
        IsActive = model.IsActive;
        IsDefault = model.IsDefault;
        Version = model.Version;
        SystemKey = model.SystemKey;
        IsSystem = model.IsSystem;
        CardCount = model.CardCount;
    }
}
