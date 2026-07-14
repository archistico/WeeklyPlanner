using WeeklyPlanner.Core.Models;

namespace WeeklyPlanner.Core.Repositories;

public interface ICardRepository
{
    Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una card in fondo alla colonna. Il SortOrder viene assegnato dal repository
    /// nella stessa transazione dell'inserimento.
    /// </summary>
    Task<Card> CreateAsync(Card card, CancellationToken cancellationToken = default);

    /// <summary>
    /// Salva titolo e note soltanto se la sessione possiede un lock attivo e la Version
    /// coincide con quella letta all'inizio dell'editing.
    /// </summary>
    Task<Card> UpdateAsync(
        Card card,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina la card e ricompatta i SortOrder della colonna nella stessa transazione.
    /// L'operazione viene rifiutata se la card ha un lock di editing attivo.
    /// </summary>
    Task DeleteAsync(
        long cardId,
        string updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sposta una card nella posizione indicata e ricompatta atomicamente tutte le card
    /// coinvolte. targetIndex è un indice di inserimento riferito alla collection di
    /// destinazione prima della rimozione della card trascinata. L'operazione viene
    /// rifiutata se la card ha un lock di editing attivo.
    /// </summary>
    Task MoveAsync(
        long cardId,
        long targetColumnId,
        int targetIndex,
        string updatedBy,
        CancellationToken cancellationToken = default);
}
