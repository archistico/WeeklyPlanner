using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Polly;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class CardRepository : ICardRepository
{
    private const string CardSelect =
        "SELECT Id, ColumnId, Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version FROM Cards";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _writePipeline;
    private readonly ResiliencePipeline _readPipeline;
    private readonly TimeProvider _timeProvider;

    public CardRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline writePipeline,
        TimeProvider? timeProvider = null,
        ResiliencePipeline? readPipeline = null)
    {
        _connectionFactory = connectionFactory;
        _writePipeline = writePipeline;
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"{CardSelect} ORDER BY ColumnId, SortOrder, Id;";

        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            var command = new CommandDefinition(sql, cancellationToken: token);
            var cards = await connection.QueryAsync<Card>(command);
            return (IReadOnlyList<Card>)cards.AsList();
        }, cancellationToken);
    }

    public async Task<Card> CreateAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        NormalizeAndValidateContent(card);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var updatedBy = string.IsNullOrWhiteSpace(card.UpdatedBy)
                ? card.CreatedBy ?? string.Empty
                : card.UpdatedBy!;
            var updatedAtUtc = GetUtcTimestamp();
            var existingCards = await GetColumnOrderAsync(connection, transaction, card.ColumnId, token);
            await PersistOrderAsync(
                connection,
                transaction,
                existingCards,
                updatedBy,
                updatedAtUtc,
                token);

            card.SortOrder = existingCards.Count;
            card.UpdatedAtUtc = updatedAtUtc;
            card.UpdatedBy = updatedBy;
            card.Version = 1;

            var command = new CommandDefinition(
                """
                INSERT INTO Cards
                    (ColumnId, Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
                VALUES
                    (@ColumnId, @Title, @Notes, @SortOrder, @CreatedBy, @UpdatedBy, @UpdatedAtUtc, @Version);
                SELECT last_insert_rowid();
                """,
                card,
                transaction,
                cancellationToken: token);

            card.Id = await connection.ExecuteScalarAsync<long>(command);
            transaction.Commit();
            return card;
        }, cancellationToken);
    }

    public async Task<Card> UpdateAsync(
        Card card,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        NormalizeAndValidateContent(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var updatedAtUtc = GetUtcTimestamp();
            var nowUtc = GetUtcTimestamp();
            var parameters = new
            {
                card.Id,
                card.Title,
                card.Notes,
                card.UpdatedBy,
                UpdatedAtUtc = updatedAtUtc,
                card.Version,
                SessionId = sessionId,
                NowUtc = nowUtc,
            };

            var command = new CommandDefinition(
                """
                UPDATE Cards
                SET Title = @Title,
                    Notes = @Notes,
                    UpdatedBy = @UpdatedBy,
                    UpdatedAtUtc = @UpdatedAtUtc,
                    Version = Version + 1
                WHERE Id = @Id
                  AND Version = @Version
                  AND EXISTS
                  (
                      SELECT 1
                      FROM CardEditLocks
                      WHERE CardId = @Id
                        AND SessionId = @SessionId
                        AND ExpiresAtUtc > @NowUtc
                  );
                """,
                parameters,
                transaction,
                cancellationToken: token);

            var affectedRows = await connection.ExecuteAsync(command);
            if (affectedRows != 1)
            {
                await ThrowUpdateFailureAsync(
                    connection,
                    transaction,
                    card.Id,
                    card.Version,
                    sessionId,
                    nowUtc,
                    token);
            }

            var updatedCard = await GetByIdAsync(connection, transaction, card.Id, token)
                ?? throw new KeyNotFoundException($"La card {card.Id} non esiste più.");

            transaction.Commit();
            return updatedCard;
        }, cancellationToken);
    }

    public async Task DeleteAsync(
        long cardId,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var sourceColumnId = await GetCardColumnIdAsync(connection, transaction, cardId, token);
            await EnsureCardIsNotLockedAsync(connection, transaction, cardId, token);

            var deleteCommand = new CommandDefinition(
                "DELETE FROM Cards WHERE Id = @CardId;",
                new { CardId = cardId },
                transaction,
                cancellationToken: token);

            var affectedRows = await connection.ExecuteAsync(deleteCommand);
            EnsureSingleRowAffected(affectedRows, cardId, "eliminare");

            var updatedAtUtc = GetUtcTimestamp();
            var remainingCards = await GetColumnOrderAsync(connection, transaction, sourceColumnId, token);
            await PersistOrderAsync(
                connection,
                transaction,
                remainingCards,
                updatedBy,
                updatedAtUtc,
                token);

            transaction.Commit();
        }, cancellationToken);
    }

    public async Task MoveAsync(
        long cardId,
        long targetColumnId,
        int targetIndex,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var sourceColumnId = await GetCardColumnIdAsync(connection, transaction, cardId, token);
            await EnsureCardIsNotLockedAsync(connection, transaction, cardId, token);
            await EnsureColumnExistsAsync(connection, transaction, targetColumnId, token);

            var updatedAtUtc = GetUtcTimestamp();
            var sourceCards = await GetColumnOrderAsync(connection, transaction, sourceColumnId, token);
            var draggedCard = sourceCards.Single(card => card.Id == cardId);
            var sourceIndex = sourceCards.IndexOf(draggedCard);

            if (sourceColumnId == targetColumnId)
            {
                sourceCards.RemoveAt(sourceIndex);

                var adjustedIndex = sourceIndex < targetIndex
                    ? targetIndex - 1
                    : targetIndex;
                adjustedIndex = Math.Clamp(adjustedIndex, 0, sourceCards.Count);
                sourceCards.Insert(adjustedIndex, draggedCard);

                await PersistOrderAsync(
                    connection,
                    transaction,
                    sourceCards,
                    updatedBy,
                    updatedAtUtc,
                    token);
            }
            else
            {
                var targetCards = await GetColumnOrderAsync(connection, transaction, targetColumnId, token);
                sourceCards.RemoveAt(sourceIndex);

                var insertIndex = Math.Clamp(targetIndex, 0, targetCards.Count);
                draggedCard.ColumnId = targetColumnId;
                targetCards.Insert(insertIndex, draggedCard);

                await PersistOrderAsync(
                    connection,
                    transaction,
                    sourceCards,
                    updatedBy,
                    updatedAtUtc,
                    token);
                await PersistOrderAsync(
                    connection,
                    transaction,
                    targetCards,
                    updatedBy,
                    updatedAtUtc,
                    token);
            }

            transaction.Commit();
        }, cancellationToken);
    }

    private static async Task ThrowUpdateFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        int expectedVersion,
        string sessionId,
        string nowUtc,
        CancellationToken cancellationToken)
    {
        var currentCard = await GetByIdAsync(connection, transaction, cardId, cancellationToken);
        if (currentCard is null)
        {
            throw new KeyNotFoundException($"Impossibile aggiornare la card {cardId}: la card non esiste più.");
        }

        var lockCommand = new CommandDefinition(
            """
            SELECT UserName
            FROM CardEditLocks
            WHERE CardId = @CardId
              AND SessionId = @SessionId
              AND ExpiresAtUtc > @NowUtc;
            """,
            new { CardId = cardId, SessionId = sessionId, NowUtc = nowUtc },
            transaction,
            cancellationToken: cancellationToken);
        var ownsActiveLock = await connection.QuerySingleOrDefaultAsync<string?>(lockCommand);

        if (ownsActiveLock is null)
        {
            var ownerCommand = new CommandDefinition(
                """
                SELECT UserName
                FROM CardEditLocks
                WHERE CardId = @CardId
                  AND ExpiresAtUtc > @NowUtc;
                """,
                new { CardId = cardId, NowUtc = nowUtc },
                transaction,
                cancellationToken: cancellationToken);
            var owner = await connection.QuerySingleOrDefaultAsync<string?>(ownerCommand);
            var message = owner is null
                ? $"Il lock di modifica della card {cardId} è scaduto o non è più disponibile."
                : $"La card {cardId} è ora bloccata da {owner}.";
            throw new CardEditLockException(cardId, message, owner);
        }

        throw new CardConcurrencyException(cardId, expectedVersion, currentCard.Version);
    }

    private async Task EnsureCardIsNotLockedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        CancellationToken cancellationToken)
    {
        var nowUtc = GetUtcTimestamp();
        var command = new CommandDefinition(
            """
            SELECT UserName
            FROM CardEditLocks
            WHERE CardId = @CardId
              AND ExpiresAtUtc > @NowUtc;
            """,
            new { CardId = cardId, NowUtc = nowUtc },
            transaction,
            cancellationToken: cancellationToken);
        var owner = await connection.QuerySingleOrDefaultAsync<string?>(command);

        if (owner is not null)
        {
            throw new CardEditLockException(
                cardId,
                $"La card {cardId} è in modifica da {owner}.",
                owner);
        }
    }

    private static async Task<Card?> GetByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            $"{CardSelect} WHERE Id = @CardId;",
            new { CardId = cardId },
            transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Card>(command);
    }

    private static async Task<long> GetCardColumnIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT ColumnId FROM Cards WHERE Id = @CardId;";
        var command = new CommandDefinition(
            sql,
            new { CardId = cardId },
            transaction,
            cancellationToken: cancellationToken);
        var columnId = await connection.QuerySingleOrDefaultAsync<long?>(command);

        return columnId ?? throw new KeyNotFoundException($"La card {cardId} non esiste.");
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long columnId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM Columns WHERE Id = @ColumnId;";
        var command = new CommandDefinition(
            sql,
            new { ColumnId = columnId },
            transaction,
            cancellationToken: cancellationToken);

        if (await connection.QuerySingleAsync<int>(command) == 0)
        {
            throw new KeyNotFoundException($"La colonna {columnId} non esiste.");
        }
    }

    private static async Task<List<CardOrderRow>> GetColumnOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long columnId,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT Id, ColumnId, SortOrder FROM Cards " +
            "WHERE ColumnId = @ColumnId ORDER BY SortOrder, Id;";
        var command = new CommandDefinition(
            sql,
            new { ColumnId = columnId },
            transaction,
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<CardOrderRow>(command);
        return rows.AsList();
    }

    private static async Task PersistOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CardOrderRow> orderedCards,
        string updatedBy,
        string updatedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE Cards
            SET ColumnId = @ColumnId,
                SortOrder = @SortOrder,
                UpdatedBy = @UpdatedBy,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id
              AND (ColumnId <> @ColumnId OR SortOrder <> @SortOrder);
            """;
        for (var index = 0; index < orderedCards.Count; index++)
        {
            var card = orderedCards[index];
            var command = new CommandDefinition(
                sql,
                new
                {
                    card.Id,
                    card.ColumnId,
                    SortOrder = index,
                    UpdatedBy = updatedBy,
                    UpdatedAtUtc = updatedAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            card.SortOrder = index;
        }
    }


    private static void NormalizeAndValidateContent(Card card)
    {
        var normalizedTitle = card.Title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length == 0)
        {
            throw new CardValidationException("Il titolo della card è obbligatorio.");
        }

        if (normalizedTitle.Length > Card.MaxTitleLength)
        {
            throw new CardValidationException(
                $"Il titolo della card non può superare {Card.MaxTitleLength} caratteri.");
        }

        card.Title = normalizedTitle;
    }

    private string GetUtcTimestamp() =>
        _timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void EnsureSingleRowAffected(int affectedRows, long cardId, string operation)
    {
        if (affectedRows != 1)
        {
            throw new KeyNotFoundException(
                $"Impossibile {operation} la card {cardId}: la card non esiste più.");
        }
    }

    private sealed class CardOrderRow
    {
        public long Id { get; set; }

        public long ColumnId { get; set; }

        public int SortOrder { get; set; }
    }
}
