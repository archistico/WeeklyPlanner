namespace WeeklyPlanner.Core.Polling;

/// <summary>
/// Astrae il controllo "è cambiato qualcosa da quando ho controllato l'ultima volta?" usato dal
/// timer di polling lato App.
/// </summary>
public interface IBoardChangeDetector
{
    /// <returns>
    /// <see langword="true"/> se la revisione monotona della board è diversa dall'ultima osservata.
    /// La prima chiamata acquisisce la baseline e restituisce <see langword="true"/>.
    /// </returns>
    Task<bool> HasChangedSinceLastCheckAsync(CancellationToken cancellationToken = default);
}
