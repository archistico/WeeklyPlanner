using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

/// <summary>
/// Rappresenta una singola cella della matrice kanban: una fascia e uno stato operativo.
/// Le card sono gli stessi ViewModel posseduti dalle collection tecniche delle colonne,
/// così editing, lock e feedback di persistenza restano univoci.
/// </summary>
public sealed class SwimlaneCellViewModel : ViewModelBase
{
    private bool _isDropAtEndVisible;

    public ColumnViewModel Column { get; }

    public long CardTypeId { get; }

    public string CardTypeName { get; }

    public bool IsGeneric { get; }

    public bool IsCardTypeActive { get; }

    public bool CanCreateCard => IsCardTypeActive && IsGeneric;

    public long ColumnId => Column.Id;

    public string ColumnName => Column.Name;

    public string AutomationName => $"{CardTypeName}, {ColumnName}";

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    public bool HasCards => Cards.Count > 0;

    public bool IsEmpty => !HasCards;

    public bool IsDropAtEndVisible
    {
        get => _isDropAtEndVisible;
        private set => SetProperty(ref _isDropAtEndVisible, value);
    }

    public void SetDropAtEnd(bool isVisible)
    {
        IsDropAtEndVisible = isVisible;
    }

    public SwimlaneCellViewModel(
        long cardTypeId,
        string cardTypeName,
        bool isGeneric,
        bool isCardTypeActive,
        ColumnViewModel column)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cardTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardTypeName);
        ArgumentNullException.ThrowIfNull(column);

        CardTypeId = cardTypeId;
        CardTypeName = cardTypeName;
        IsGeneric = isGeneric;
        IsCardTypeActive = isCardTypeActive;
        Column = column;
        Cards.CollectionChanged += OnCardsCollectionChanged;
    }

    private void OnCardsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
