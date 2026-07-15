using System.Collections.ObjectModel;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class ColumnViewModel : ViewModelBase
{
    private bool _isDropAtEndVisible;

    public Column Model { get; }

    public long Id => Model.Id;

    public string Name => Model.Name;

    public string? SystemKey => Model.SystemKey;

    public bool IsSystem => Model.IsSystem;

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    public bool IsDropAtEndVisible
    {
        get => _isDropAtEndVisible;
        private set => SetProperty(ref _isDropAtEndVisible, value);
    }

    public void SetDropAtEnd(bool isVisible)
    {
        IsDropAtEndVisible = isVisible;
    }

    public void RefreshFromModel(Column model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var nameChanged = !string.Equals(Model.Name, model.Name, StringComparison.Ordinal);
        var systemKeyChanged = !string.Equals(Model.SystemKey, model.SystemKey, StringComparison.Ordinal);
        var isSystemChanged = Model.IsSystem != model.IsSystem;

        Model.Name = model.Name;
        Model.SortOrder = model.SortOrder;
        Model.SystemKey = model.SystemKey;
        Model.IsSystem = model.IsSystem;

        if (nameChanged)
        {
            OnPropertyChanged(nameof(Name));
        }

        if (systemKeyChanged)
        {
            OnPropertyChanged(nameof(SystemKey));
        }

        if (isSystemChanged)
        {
            OnPropertyChanged(nameof(IsSystem));
        }
    }

    public ColumnViewModel(Column model)
    {
        Model = model;
    }
}
