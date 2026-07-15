using Avalonia.Media;

namespace WeeklyPlanner.App.ViewModels;

public sealed class PriorityDeadlineRuleViewModel : ViewModelBase
{
    private bool _useOverride;
    private int _dueValue;
    private DeadlineUnitOption _selectedUnit;

    public long CardTypeId { get; }

    public string CardTypeName { get; }

    public string ColorHex { get; }

    public IBrush ColorBrush { get; }

    public bool IsCardTypeActive { get; }

    public bool IsCardTypeInactive => !IsCardTypeActive;

    public IReadOnlyList<DeadlineUnitOption> DeadlineUnits { get; }

    public bool UseOverride
    {
        get => _useOverride;
        set => SetProperty(ref _useOverride, value);
    }

    public int DueValue
    {
        get => _dueValue;
        set => SetProperty(ref _dueValue, value);
    }

    public DeadlineUnitOption SelectedUnit
    {
        get => _selectedUnit;
        set => SetProperty(ref _selectedUnit, value);
    }

    public PriorityDeadlineRuleViewModel(
        long cardTypeId,
        string cardTypeName,
        string colorHex,
        bool isCardTypeActive,
        IReadOnlyList<DeadlineUnitOption> deadlineUnits,
        bool useOverride,
        int dueValue,
        DeadlineUnitOption selectedUnit)
    {
        CardTypeId = cardTypeId;
        CardTypeName = cardTypeName;
        ColorHex = colorHex;
        IsCardTypeActive = isCardTypeActive;
        DeadlineUnits = deadlineUnits;
        _useOverride = useOverride;
        _dueValue = dueValue;
        _selectedUnit = selectedUnit;
        ColorBrush = ColorHexParser.ToBrush(colorHex);
    }

    public int GetDueHours()
    {
        try
        {
            return checked(DueValue * SelectedUnit.HoursMultiplier);
        }
        catch (OverflowException)
        {
            return int.MaxValue;
        }
    }
}
