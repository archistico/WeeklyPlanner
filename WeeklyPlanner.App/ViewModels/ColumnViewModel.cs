using System.Collections.ObjectModel;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class ColumnViewModel : ViewModelBase
{
    public Column Model { get; }

    public long Id => Model.Id;

    public string Name => Model.Name;

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    public ColumnViewModel(Column model)
    {
        Model = model;
    }
}
