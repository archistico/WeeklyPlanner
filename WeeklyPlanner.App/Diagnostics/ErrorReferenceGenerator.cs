namespace WeeklyPlanner.App.Diagnostics;

public sealed class ErrorReferenceGenerator : IErrorReferenceGenerator
{
    public string Create() => $"WP-{Guid.NewGuid():N}"[..9].ToUpperInvariant();
}
