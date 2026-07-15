using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Polly;
using WeeklyPlanner.Core.Auditing;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed class CardRepository : ICardRepository
{
    private const string CardSelect =
        "SELECT Id, ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, " +
        "PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc, " +
        "Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version FROM Cards";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _writePipeline;
    private readonly ResiliencePipeline _readPipeline;
    private readonly TimeProvider _timeProvider;
    private readonly ICardAuditContextProvider _auditContextProvider;

    public CardRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline writePipeline,
        TimeProvider? timeProvider = null,
        ResiliencePipeline? readPipeline = null,
        ICardAuditContextProvider? auditContextProvider = null)
    {
        _connectionFactory = connectionFactory;
        _writePipeline = writePipeline;
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _auditContextProvider = auditContextProvider ?? NullCardAuditContextProvider.Instance;
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

            var resolvedCardTypeId = await ResolveCardTypeIdAsync(
                connection,
                transaction,
                card.CardTypeId,
                token);
            card.CardTypeId = resolvedCardTypeId;
            await EnsureCardTypeExistsAsync(connection, transaction, resolvedCardTypeId, token);
            var creationCardType = await GetCardTypeForMovementAsync(
                connection,
                transaction,
                resolvedCardTypeId,
                token);
            if (!creationCardType.IsActive)
            {
                throw new InvalidOperationException(
                    $"La fascia {creationCardType.Name} è inattiva e non può ricevere nuove card.");
            }
            await EnsureColumnExistsAsync(connection, transaction, card.ColumnId, token);
            if (card.PriorityId is long creationPriorityId)
            {
                await EnsurePriorityCanBeAssignedAsync(
                    connection,
                    transaction,
                    creationPriorityId,
                    token);
            }

            var updatedBy = NormalizeUserName(card.UpdatedBy, card.CreatedBy);
            var now = GetUtcNow();
            var nowText = FormatUtc(now);
            var existingCards = await GetColumnOrderAsync(connection, transaction, card.ColumnId, token);
            await PersistOrderAsync(
                connection,
                transaction,
                existingCards,
                token);

            card.StableId = string.IsNullOrWhiteSpace(card.StableId)
                ? Guid.NewGuid().ToString("N")
                : card.StableId.Trim();
            card.CreatedAtUtc = nowText;
            card.CreatedAtIsEstimated = false;
            card.PriorityAssignedAtUtc = card.PriorityId is null ? null : nowText;
            card.DueAtUtc = await CalculateDueAtAsync(
                connection,
                transaction,
                card.PriorityId,
                card.CardTypeId,
                card.PriorityAssignedAtUtc,
                token);
            card.SortOrder = existingCards.Count;
            card.UpdatedAtUtc = nowText;
            card.UpdatedBy = updatedBy;
            var createdBy = card.CreatedBy;
            card.CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? updatedBy : createdBy.Trim();
            card.Version = 1;

            var command = new CommandDefinition(
                """
                INSERT INTO Cards
                    (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated,
                     PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc,
                     Title, Notes, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
                VALUES
                    (@ColumnId, @StableId, @CreatedAtUtc, @CreatedAtIsEstimated,
                     @PriorityId, @CardTypeId, @PriorityAssignedAtUtc, @DueAtUtc,
                     @Title, @Notes, @SortOrder, @CreatedBy, @UpdatedBy, @UpdatedAtUtc, @Version);
                SELECT last_insert_rowid();
                """,
                card,
                transaction,
                cancellationToken: token);

            card.Id = await connection.ExecuteScalarAsync<long>(command);

            var createdCardTypeId = resolvedCardTypeId;
            var createdOrderRow = new CardOrderRow
            {
                Id = card.Id,
                ColumnId = card.ColumnId,
                CardTypeId = card.CardTypeId,
                SortOrder = card.SortOrder,
            };
            existingCards.Add(createdOrderRow);
            var createdCellCards = existingCards
                .Where(existing => GetRequiredCardTypeId(existing) == createdCardTypeId)
                .ToList();
            var cardTypeRanks = await GetCardTypeRanksAsync(
                connection,
                transaction,
                token);
            var canonicalOrder = BuildCanonicalColumnOrder(
                existingCards,
                cardTypeRanks,
                createdCardTypeId,
                createdCellCards);
            await PersistOrderAsync(
                connection,
                transaction,
                canonicalOrder,
                token);
            card.SortOrder = canonicalOrder.FindIndex(existing => existing.Id == card.Id);

            await InsertEventAsync(
                connection,
                transaction,
                card,
                CardEventTypes.Created,
                nowText,
                updatedBy,
                "Card creata.",
                new
                {
                    card.ColumnId,
                    card.SortOrder,
                    card.PriorityId,
                    card.CardTypeId,
                    card.PriorityAssignedAtUtc,
                    card.DueAtUtc,
                },
                token);

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

            var currentCard = await GetByIdAsync(connection, transaction, card.Id, token)
                ?? throw new KeyNotFoundException($"Impossibile aggiornare la card {card.Id}: la card non esiste più.");
            card.CardTypeId = await ResolveCardTypeIdAsync(
                connection,
                transaction,
                card.CardTypeId,
                token);
            await EnsureCardTypeExistsAsync(connection, transaction, card.CardTypeId, token);

            var now = GetUtcNow();
            var nowText = FormatUtc(now);
            var priorityChanged = currentCard.PriorityId != card.PriorityId;
            var typeChanged = currentCard.CardTypeId != card.CardTypeId;
            if (priorityChanged && card.PriorityId is long assignedPriorityId)
            {
                await EnsurePriorityCanBeAssignedAsync(
                    connection,
                    transaction,
                    assignedPriorityId,
                    token);
            }

            var titleChanged = !string.Equals(currentCard.Title, card.Title, StringComparison.Ordinal);
            var notesChanged = !string.Equals(currentCard.Notes, card.Notes, StringComparison.Ordinal);
            var contentChanged = titleChanged || notesChanged;

            var priorityAssignedAtUtc = priorityChanged
                ? card.PriorityId is null ? null : nowText
                : currentCard.PriorityAssignedAtUtc;
            if (card.PriorityId is not null && string.IsNullOrWhiteSpace(priorityAssignedAtUtc))
            {
                priorityAssignedAtUtc = nowText;
            }

            var dueAtUtc = await CalculateDueAtAsync(
                connection,
                transaction,
                card.PriorityId,
                card.CardTypeId,
                priorityAssignedAtUtc,
                token);
            var updatedBy = NormalizeUserName(card.UpdatedBy, currentCard.UpdatedBy, currentCard.CreatedBy);

            var parameters = new
            {
                card.Id,
                card.Title,
                card.Notes,
                card.PriorityId,
                card.CardTypeId,
                PriorityAssignedAtUtc = priorityAssignedAtUtc,
                DueAtUtc = dueAtUtc,
                UpdatedBy = updatedBy,
                UpdatedAtUtc = nowText,
                card.Version,
                SessionId = sessionId,
                NowUtc = nowText,
            };

            var command = new CommandDefinition(
                """
                UPDATE Cards
                SET Title = @Title,
                    Notes = @Notes,
                    PriorityId = @PriorityId,
                    CardTypeId = @CardTypeId,
                    PriorityAssignedAtUtc = @PriorityAssignedAtUtc,
                    DueAtUtc = @DueAtUtc,
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
                    nowText,
                    token);
            }

            var updatedCard = await GetByIdAsync(connection, transaction, card.Id, token)
                ?? throw new KeyNotFoundException($"La card {card.Id} non esiste più.");

            if (contentChanged)
            {
                await InsertEventAsync(
                    connection,
                    transaction,
                    updatedCard,
                    CardEventTypes.Updated,
                    nowText,
                    updatedBy,
                    BuildContentSummary(titleChanged, notesChanged),
                    new
                    {
                        titleChanged,
                        notesChanged,
                    },
                    token);
            }

            if (priorityChanged)
            {
                await InsertEventAsync(
                    connection,
                    transaction,
                    updatedCard,
                    CardEventTypes.PriorityChanged,
                    nowText,
                    updatedBy,
                    "Priorità della card modificata.",
                    new
                    {
                        previousPriorityId = currentCard.PriorityId,
                        priorityId = updatedCard.PriorityId,
                        updatedCard.PriorityAssignedAtUtc,
                        updatedCard.DueAtUtc,
                    },
                    token);
            }

            if (typeChanged)
            {
                await InsertEventAsync(
                    connection,
                    transaction,
                    updatedCard,
                    CardEventTypes.TypeChanged,
                    nowText,
                    updatedBy,
                    "Tipologia della card modificata.",
                    new
                    {
                        previousCardTypeId = currentCard.CardTypeId,
                        cardTypeId = updatedCard.CardTypeId,
                        updatedCard.DueAtUtc,
                    },
                    token);
            }

            if (!contentChanged && !priorityChanged && !typeChanged)
            {
                await InsertEventAsync(
                    connection,
                    transaction,
                    updatedCard,
                    CardEventTypes.Updated,
                    nowText,
                    updatedBy,
                    "Card salvata senza variazioni di contenuto.",
                    new { changedFields = Array.Empty<string>() },
                    token);
            }

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

            var currentCard = await GetByIdAsync(connection, transaction, cardId, token)
                ?? throw new KeyNotFoundException($"La card {cardId} non esiste.");
            await EnsureCardIsNotLockedAsync(connection, transaction, cardId, token);

            var nowText = FormatUtc(GetUtcNow());
            await InsertEventAsync(
                connection,
                transaction,
                currentCard,
                CardEventTypes.Deleted,
                nowText,
                updatedBy,
                "Card eliminata.",
                new
                {
                    currentCard.ColumnId,
                    currentCard.SortOrder,
                    currentCard.PriorityId,
                    currentCard.CardTypeId,
                },
                token);

            var deleteCommand = new CommandDefinition(
                "DELETE FROM Cards WHERE Id = @CardId;",
                new { CardId = cardId },
                transaction,
                cancellationToken: token);

            var affectedRows = await connection.ExecuteAsync(deleteCommand);
            EnsureSingleRowAffected(affectedRows, cardId, "eliminare");

            var remainingCards = await GetColumnOrderAsync(connection, transaction, currentCard.ColumnId, token);
            await PersistOrderAsync(
                connection,
                transaction,
                remainingCards,
                token);

            transaction.Commit();
        }, cancellationToken);
    }

    public async Task MoveToCellAsync(
        long cardId,
        long targetColumnId,
        long targetCardTypeId,
        int targetCellIndex,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCardTypeId);
        ArgumentOutOfRangeException.ThrowIfNegative(targetCellIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var currentCard = await GetByIdAsync(connection, transaction, cardId, token)
                ?? throw new KeyNotFoundException($"La card {cardId} non esiste.");
            await EnsureCardIsNotLockedAsync(connection, transaction, cardId, token);
            await EnsureColumnExistsAsync(connection, transaction, targetColumnId, token);

            var sourceColumnId = currentCard.ColumnId;
            var sourceCardTypeId = await ResolveCardTypeIdAsync(
                connection,
                transaction,
                currentCard.CardTypeId,
                token);
            var sourceCardType = await GetCardTypeForMovementAsync(
                connection,
                transaction,
                sourceCardTypeId,
                token);
            var targetCardType = await GetCardTypeForMovementAsync(
                connection,
                transaction,
                targetCardTypeId,
                token);
            if (!targetCardType.IsActive && targetCardType.Id != sourceCardTypeId)
            {
                throw new InvalidOperationException(
                    $"La fascia {targetCardType.Name} è inattiva e non può ricevere nuove card.");
            }

            var sameColumn = sourceColumnId == targetColumnId;
            var sameCardType = sourceCardTypeId == targetCardTypeId;
            var sameCell = sameColumn && sameCardType;
            var sourceCards = await GetColumnOrderAsync(
                connection,
                transaction,
                sourceColumnId,
                token);
            var draggedCard = sourceCards.Single(card => card.Id == cardId);
            draggedCard.CardTypeId = sourceCardTypeId;
            var sourceGlobalIndex = sourceCards.IndexOf(draggedCard);
            var sourceCellCards = sourceCards
                .Where(card => GetRequiredCardTypeId(card) == sourceCardTypeId)
                .ToList();
            var sourceCellIndex = sourceCellCards.IndexOf(draggedCard);

            var finalCellIndex = targetCellIndex;
            if (sameCell)
            {
                var adjustedIndex = sourceCellIndex < targetCellIndex
                    ? targetCellIndex - 1
                    : targetCellIndex;
                finalCellIndex = Math.Clamp(adjustedIndex, 0, sourceCellCards.Count - 1);
                if (finalCellIndex == sourceCellIndex)
                {
                    transaction.Commit();
                    return;
                }
            }

            sourceCards.RemoveAt(sourceGlobalIndex);
            var targetCards = sameColumn
                ? sourceCards
                : await GetColumnOrderAsync(connection, transaction, targetColumnId, token);
            var targetCellCards = targetCards
                .Where(card => GetRequiredCardTypeId(card) == targetCardTypeId)
                .ToList();
            if (!sameCell)
            {
                finalCellIndex = Math.Clamp(targetCellIndex, 0, targetCellCards.Count);
            }

            draggedCard.ColumnId = targetColumnId;
            draggedCard.CardTypeId = targetCardTypeId;
            targetCellCards.Insert(finalCellIndex, draggedCard);

            var cardTypeRanks = await GetCardTypeRanksAsync(
                connection,
                transaction,
                token);
            var orderedTargetCards = BuildCanonicalColumnOrder(
                targetCards,
                cardTypeRanks,
                targetCardTypeId,
                targetCellCards);
            if (!sameColumn)
            {
                var orderedSourceCards = BuildCanonicalColumnOrder(
                    sourceCards,
                    cardTypeRanks);
                await PersistOrderAsync(
                    connection,
                    transaction,
                    orderedSourceCards,
                    token);
            }

            await PersistOrderAsync(
                connection,
                transaction,
                orderedTargetCards,
                token);

            var previousDueAtUtc = currentCard.DueAtUtc;
            var dueAtUtc = sameCardType
                ? currentCard.DueAtUtc
                : await CalculateDueAtAsync(
                    connection,
                    transaction,
                    currentCard.PriorityId,
                    targetCardTypeId,
                    currentCard.PriorityAssignedAtUtc,
                    token);
            var nowText = FormatUtc(GetUtcNow());
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE Cards
                SET CardTypeId = @CardTypeId,
                    DueAtUtc = @DueAtUtc,
                    UpdatedBy = @UpdatedBy,
                    UpdatedAtUtc = @UpdatedAtUtc,
                    Version = Version + 1
                WHERE Id = @CardId;
                """,
                new
                {
                    CardId = cardId,
                    CardTypeId = targetCardTypeId,
                    DueAtUtc = dueAtUtc,
                    UpdatedBy = NormalizeUserName(updatedBy),
                    UpdatedAtUtc = nowText,
                },
                transaction,
                cancellationToken: token));
            EnsureSingleRowAffected(affectedRows, cardId, "spostare");

            var sourceColumnName = await GetColumnNameAsync(
                connection,
                transaction,
                sourceColumnId,
                token);
            var targetColumnName = sameColumn
                ? sourceColumnName
                : await GetColumnNameAsync(
                    connection,
                    transaction,
                    targetColumnId,
                    token);
            var finalGlobalIndex = orderedTargetCards.FindIndex(card => card.Id == cardId);
            currentCard.ColumnId = targetColumnId;
            currentCard.CardTypeId = targetCardTypeId;
            currentCard.SortOrder = finalGlobalIndex;
            currentCard.DueAtUtc = dueAtUtc;
            currentCard.UpdatedBy = NormalizeUserName(updatedBy);
            currentCard.UpdatedAtUtc = nowText;
            currentCard.Version++;

            var eventType = sameCell
                ? CardEventTypes.Reordered
                : CardEventTypes.Moved;
            var summary = sameCell
                ? $"Ordine aggiornato in {targetCardType.Name} / {targetColumnName}."
                : $"Card spostata da {sourceCardType.Name} / {sourceColumnName} " +
                  $"a {targetCardType.Name} / {targetColumnName}.";
            await InsertEventAsync(
                connection,
                transaction,
                currentCard,
                eventType,
                nowText,
                updatedBy,
                summary,
                new
                {
                    previousColumnId = sourceColumnId,
                    previousColumnName = sourceColumnName,
                    columnId = targetColumnId,
                    columnName = targetColumnName,
                    previousCardTypeId = sourceCardTypeId,
                    previousCardTypeName = sourceCardType.Name,
                    cardTypeId = targetCardTypeId,
                    cardTypeName = targetCardType.Name,
                    previousCellIndex = sourceCellIndex,
                    cellIndex = finalCellIndex,
                    previousSortOrder = sourceGlobalIndex,
                    sortOrder = finalGlobalIndex,
                    previousDueAtUtc,
                    dueAtUtc,
                },
                token);

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
        var nowUtc = FormatUtc(GetUtcNow());
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

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long columnId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM Columns WHERE Id = @ColumnId AND IsSystem = 1;";
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

    private static async Task<long> ResolveCardTypeIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? cardTypeId,
        CancellationToken cancellationToken)
    {
        if (cardTypeId is not null)
        {
            return cardTypeId.Value;
        }

        var command = new CommandDefinition(
            "SELECT Id FROM CardTypes WHERE SystemKey = @SystemKey;",
            new { SystemKey = SystemCardTypeKeys.Generic },
            transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<long?>(command)
            ?? throw new InvalidOperationException(
                "La tipologia di sistema Generica non è disponibile.");
    }

    private static async Task EnsureCardTypeExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? cardTypeId,
        CancellationToken cancellationToken)
    {
        if (cardTypeId is null)
        {
            return;
        }

        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM CardTypes WHERE Id = @CardTypeId;",
            new { CardTypeId = cardTypeId.Value },
            transaction,
            cancellationToken: cancellationToken);
        if (await connection.QuerySingleAsync<int>(command) == 0)
        {
            throw new KeyNotFoundException($"La tipologia {cardTypeId.Value} non esiste.");
        }
    }

    private static async Task<string> GetColumnNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long columnId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "SELECT Name FROM Columns WHERE Id = @ColumnId;",
            new { ColumnId = columnId },
            transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<string>(command);
    }

    private static async Task<List<CardOrderRow>> GetColumnOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long columnId,
        CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT Id, ColumnId, CardTypeId, SortOrder FROM Cards " +
            "WHERE ColumnId = @ColumnId ORDER BY SortOrder, Id;";
        var command = new CommandDefinition(
            sql,
            new { ColumnId = columnId },
            transaction,
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<CardOrderRow>(command);
        return rows.AsList();
    }

    private static async Task<CardTypeMoveRow> GetCardTypeForMovementAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardTypeId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "SELECT Id, Name, IsActive FROM CardTypes WHERE Id = @CardTypeId;",
            new { CardTypeId = cardTypeId },
            transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CardTypeMoveRow>(command)
            ?? throw new KeyNotFoundException($"La fascia {cardTypeId} non esiste.");
    }

    private static async Task<IReadOnlyDictionary<long, int>> GetCardTypeRanksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            SELECT Id,
                   CASE WHEN SystemKey = @GenericSystemKey THEN -1 ELSE SortOrder END AS Rank
            FROM CardTypes
            ORDER BY Rank, Id;
            """,
            new { GenericSystemKey = SystemCardTypeKeys.Generic },
            transaction,
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<CardTypeRankRow>(command);
        return rows.ToDictionary(row => row.Id, row => row.Rank);
    }

    private static List<CardOrderRow> BuildCanonicalColumnOrder(
        IReadOnlyList<CardOrderRow> cards,
        IReadOnlyDictionary<long, int> cardTypeRanks,
        long? overriddenCardTypeId = null,
        IReadOnlyList<CardOrderRow>? overriddenCellOrder = null)
    {
        var cardsByType = cards
            .GroupBy(GetRequiredCardTypeId)
            .ToDictionary(group => group.Key, group => group.ToList());
        if (overriddenCardTypeId is long overriddenTypeId)
        {
            cardsByType[overriddenTypeId] = overriddenCellOrder?.ToList() ?? [];
        }

        foreach (var cardTypeId in cardsByType.Keys)
        {
            if (!cardTypeRanks.ContainsKey(cardTypeId))
            {
                throw new InvalidOperationException(
                    $"La colonna contiene una card assegnata alla fascia inesistente {cardTypeId}.");
            }
        }

        return cardsByType
            .OrderBy(pair => cardTypeRanks[pair.Key])
            .ThenBy(pair => pair.Key)
            .SelectMany(pair => pair.Value)
            .ToList();
    }

    private static long GetRequiredCardTypeId(CardOrderRow card) =>
        card.CardTypeId
        ?? throw new InvalidOperationException(
            $"La card {card.Id} non ha una fascia assegnata nello schema kanban corrente.");

    private static async Task PersistOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CardOrderRow> orderedCards,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE Cards
            SET ColumnId = @ColumnId,
                SortOrder = @SortOrder
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
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            card.SortOrder = index;
        }
    }

    private static async Task EnsurePriorityCanBeAssignedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long priorityId,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "SELECT Name, IsActive FROM Priorities WHERE Id = @Id;",
            new { Id = priorityId },
            transaction,
            cancellationToken: cancellationToken);
        var priority = await connection.QuerySingleOrDefaultAsync<PriorityAssignmentRow>(command)
            ?? throw new KeyNotFoundException($"La priorità {priorityId} non esiste.");
        if (!priority.IsActive)
        {
            throw new InvalidOperationException(
                $"La priorità {priority.Name} è inattiva e non può essere assegnata.");
        }
    }

    private static async Task<string?> CalculateDueAtAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? priorityId,
        long? cardTypeId,
        string? assignedAtUtc,
        CancellationToken cancellationToken)
    {
        if (priorityId is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(assignedAtUtc))
        {
            throw new InvalidOperationException("La data di assegnazione è obbligatoria per una card con priorità.");
        }

        var command = new CommandDefinition(
            """
            SELECT priority.DefaultDueHours,
                   rule.DueHours AS OverrideDueHours
            FROM Priorities priority
            LEFT JOIN PriorityTypeDeadlines rule
              ON rule.PriorityId = priority.Id
             AND rule.CardTypeId = @CardTypeId
            WHERE priority.Id = @PriorityId;
            """,
            new { PriorityId = priorityId.Value, CardTypeId = cardTypeId },
            transaction,
            cancellationToken: cancellationToken);
        var deadline = await connection.QuerySingleOrDefaultAsync<PriorityDeadlineRow>(command)
            ?? throw new KeyNotFoundException($"La priorità {priorityId.Value} non esiste.");
        var assignedAt = DateTimeOffset.Parse(
            assignedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return FormatUtc(PriorityDeadlineCalculator.CalculateDueAt(
            assignedAt,
            deadline.DefaultDueHours,
            deadline.OverrideDueHours));
    }

    private async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Card card,
        string eventType,
        string occurredAtUtc,
        string userName,
        string summary,
        object data,
        CancellationToken cancellationToken)
    {
        var audit = _auditContextProvider.Current;
        var command = new CommandDefinition(
            """
            INSERT INTO CardEvents
                (CardStableId, CardId, EventType, OccurredAtUtc, UserName,
                 SessionId, MachineName, Summary, DataJson, FormatVersion)
            VALUES
                (@CardStableId, @CardId, @EventType, @OccurredAtUtc, @UserName,
                 @SessionId, @MachineName, @Summary, @DataJson, 1);
            """,
            new
            {
                CardStableId = card.StableId,
                CardId = card.Id,
                EventType = eventType,
                OccurredAtUtc = occurredAtUtc,
                UserName = NormalizeUserName(userName),
                audit.SessionId,
                audit.MachineName,
                Summary = summary,
                DataJson = JsonSerializer.Serialize(data),
            },
            transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static string BuildContentSummary(bool titleChanged, bool notesChanged)
    {
        return (titleChanged, notesChanged) switch
        {
            (true, true) => "Titolo e note della card modificati.",
            (true, false) => "Titolo della card modificato.",
            (false, true) => "Note della card modificate.",
            _ => "Card modificata.",
        };
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

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeUserName(params string?[] candidates) =>
        candidates
            .Select(candidate => candidate?.Trim())
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
        ?? "Sconosciuto";

    private static void EnsureSingleRowAffected(int affectedRows, long cardId, string operation)
    {
        if (affectedRows != 1)
        {
            throw new KeyNotFoundException(
                $"Impossibile {operation} la card {cardId}: la card non esiste più.");
        }
    }

    private sealed class PriorityDeadlineRow
    {
        public int DefaultDueHours { get; set; }

        public int? OverrideDueHours { get; set; }
    }

    private sealed class PriorityAssignmentRow
    {
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class CardOrderRow
    {
        public long Id { get; set; }

        public long ColumnId { get; set; }

        public long? CardTypeId { get; set; }

        public int SortOrder { get; set; }
    }

    private sealed class CardTypeMoveRow
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class CardTypeRankRow
    {
        public long Id { get; set; }

        public int Rank { get; set; }
    }
}
