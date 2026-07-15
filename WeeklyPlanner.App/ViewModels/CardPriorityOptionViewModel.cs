using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.App.ViewModels;

/// <summary>
/// Opzione compatta usata dall'editor della card. L'elemento con Id nullo
/// rappresenta esplicitamente l'assenza di priorità.
/// </summary>
public sealed class CardPriorityOptionViewModel
{
    public static CardPriorityOptionViewModel None { get; } = new();

    private CardPriorityOptionViewModel()
    {
        Id = null;
        Code = "—";
        Name = "Nessuna";
        Description = "La card non ha una priorità né una scadenza associata.";
        DisplayName = "Nessuna";
        BadgeText = "—";
        EffectiveDueHours = null;
        IsActive = true;
        IsNone = true;
    }

    private CardPriorityOptionViewModel(long id)
    {
        Id = id;
        Code = "?";
        Name = $"Priorità {id}";
        Description = "Priorità non disponibile nel catalogo corrente.";
        DisplayName = Name;
        BadgeText = "?";
        EffectiveDueHours = null;
        IsActive = false;
        IsNone = false;
    }

    public static CardPriorityOptionViewModel Unknown(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return new CardPriorityOptionViewModel(id);
    }

    public CardPriorityOptionViewModel(
        PriorityDefinition model,
        int effectiveDueHours)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectiveDueHours);

        Id = model.Id;
        Code = model.Code;
        Name = model.Name;
        Description = model.Description;
        DisplayName = model.IsActive
            ? $"{model.Code} — {model.Name}"
            : $"{model.Code} — {model.Name} (inattiva)";
        BadgeText = model.Code;
        EffectiveDueHours = effectiveDueHours;
        IsActive = model.IsActive;
        IsNone = false;
    }

    public long? Id { get; }

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }

    public string DisplayName { get; }

    public string BadgeText { get; }

    public int? EffectiveDueHours { get; }

    public bool IsActive { get; }

    public bool IsNone { get; }
}
