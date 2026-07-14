using System.Collections.ObjectModel;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

public sealed class ColumnViewModel : ViewModelBase
{
    private bool _isDropAtEndVisible;

    public Column Model { get; }

    public long Id => Model.Id;

    public string Name => Model.Name;

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

    public ColumnViewModel(Column model)
    {
        Model = model;
    }
}
