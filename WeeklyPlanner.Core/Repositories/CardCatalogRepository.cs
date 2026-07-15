using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Polly;
using WeeklyPlanner.Core.Auditing;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Resilience;

namespace WeeklyPlanner.Core.Repositories;

public sealed partial class CardCatalogRepository : ICardCatalogRepository
{
    private const int MaximumDueHours = 24 * 3650;
    private const int MaximumPriorityCodeLength = 12;
    private const int MaximumNameLength = 80;
    private const int MaximumDescriptionLength = 500;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ResiliencePipeline _readPipeline;
    private readonly ResiliencePipeline _writePipeline;
    private readonly TimeProvider _timeProvider;
    private readonly ICardAuditContextProvider _auditContextProvider;

    public CardCatalogRepository(
        SqliteConnectionFactory connectionFactory,
        ResiliencePipeline? readPipeline = null,
        ResiliencePipeline? writePipeline = null,
        TimeProvider? timeProvider = null,
        ICardAuditContextProvider? auditContextProvider = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _readPipeline = readPipeline ?? RetryPolicyFactory.CreateSqliteReadPipeline();
        _writePipeline = writePipeline ?? RetryPolicyFactory.CreateSqliteWritePipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _auditContextProvider = auditContextProvider ?? NullCardAuditContextProvider.Instance;
    }

    public async Task<CardCatalogSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        return await _readPipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction();

            var priorities = (await connection.QueryAsync<PriorityDefinition>(new CommandDefinition(
                "SELECT Id, Code, Name, Description, DefaultDueHours, SortOrder, IsActive, IsDefault, Version " +
                "FROM Priorities ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var cardTypes = (await connection.QueryAsync<CardTypeDefinition>(new CommandDefinition(
                "SELECT cardType.Id, cardType.Name, cardType.ColorHex, cardType.SortOrder, " +
                "cardType.IsActive, cardType.IsDefault, cardType.Version, cardType.SystemKey, " +
                "cardType.IsSystem, (SELECT COUNT(*) FROM Cards card WHERE card.CardTypeId = cardType.Id) AS CardCount " +
                "FROM CardTypes cardType ORDER BY cardType.SortOrder, cardType.Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            var deadlineRules = (await connection.QueryAsync<PriorityTypeDeadline>(new CommandDefinition(
                "SELECT PriorityId, CardTypeId, DueHours, Version " +
                "FROM PriorityTypeDeadlines ORDER BY PriorityId, CardTypeId;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            transaction.Commit();
            return new CardCatalogSnapshot(priorities, cardTypes, deadlineRules);
        }, cancellationToken);
    }

    public async Task<PriorityDefinition> SavePriorityAsync(
        PrioritySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizePriorityRequest(request);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            await EnsurePriorityUniqueAsync(
                connection,
                transaction,
                normalized.Code,
                normalized.Name,
                normalized.Id,
                token);
            await EnsureCardTypesExistAsync(
                connection,
                transaction,
                normalized.DeadlineOverrides.Select(item => item.CardTypeId),
                token);

            long priorityId;
            if (normalized.Id is null)
            {
                var sortOrder = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Priorities;",
                    transaction: transaction,
                    cancellationToken: token));

                if (normalized.IsDefault)
                {
                    await ClearOtherPriorityDefaultsAsync(connection, transaction, null, token);
                }

                priorityId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    """
                    INSERT INTO Priorities
                        (Code, Name, Description, DefaultDueHours, SortOrder, IsActive, IsDefault, Version)
                    VALUES
                        (@Code, @Name, @Description, @DefaultDueHours, @SortOrder, @IsActive, @IsDefault, 1);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        normalized.Code,
                        normalized.Name,
                        normalized.Description,
                        normalized.DefaultDueHours,
                        SortOrder = sortOrder,
                        normalized.IsActive,
                        normalized.IsDefault,
                    },
                    transaction,
                    cancellationToken: token));
            }
            else
            {
                priorityId = normalized.Id.Value;
                var current = await GetPriorityAsync(connection, transaction, priorityId, token);
                EnsureVersion("priorità", priorityId, normalized.ExpectedVersion, current?.Version);

                if (normalized.IsDefault)
                {
                    await ClearOtherPriorityDefaultsAsync(connection, transaction, priorityId, token);
                }

                var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE Priorities
                    SET Code = @Code,
                        Name = @Name,
                        Description = @Description,
                        DefaultDueHours = @DefaultDueHours,
                        IsActive = @IsActive,
                        IsDefault = @IsDefault,
                        Version = Version + 1
                    WHERE Id = @Id
                      AND Version = @ExpectedVersion;
                    """,
                    new
                    {
                        Id = priorityId,
                        normalized.Code,
                        normalized.Name,
                        normalized.Description,
                        normalized.DefaultDueHours,
                        normalized.IsActive,
                        normalized.IsDefault,
                        normalized.ExpectedVersion,
                    },
                    transaction,
                    cancellationToken: token));

                if (affectedRows != 1)
                {
                    var actualVersion = await GetPriorityVersionAsync(connection, transaction, priorityId, token);
                    throw new CardCatalogConcurrencyException(
                        "priorità",
                        priorityId,
                        normalized.ExpectedVersion,
                        actualVersion);
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM PriorityTypeDeadlines WHERE PriorityId = @PriorityId;",
                    new { PriorityId = priorityId },
                    transaction,
                    cancellationToken: token));
            }

            foreach (var deadlineOverride in normalized.DeadlineOverrides)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO PriorityTypeDeadlines
                        (PriorityId, CardTypeId, DueHours, Version)
                    VALUES
                        (@PriorityId, @CardTypeId, @DueHours, 1);
                    """,
                    new
                    {
                        PriorityId = priorityId,
                        deadlineOverride.CardTypeId,
                        deadlineOverride.DueHours,
                    },
                    transaction,
                    cancellationToken: token));
            }

            var saved = await GetPriorityAsync(connection, transaction, priorityId, token)
                ?? throw new InvalidOperationException("La priorità salvata non è stata riletta.");
            transaction.Commit();
            return saved;
        }, cancellationToken);
    }

    public async Task DeletePriorityAsync(
        long priorityId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorityId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var current = await GetPriorityAsync(connection, transaction, priorityId, token);
            EnsureVersion("priorità", priorityId, expectedVersion, current?.Version);

            var usageCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM Cards WHERE PriorityId = @PriorityId;",
                new { PriorityId = priorityId },
                transaction,
                cancellationToken: token));
            if (usageCount > 0)
            {
                throw new CardCatalogItemInUseException(
                    "priorità",
                    priorityId,
                    usageCount,
                    current!.Name);
            }

            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM Priorities WHERE Id = @Id AND Version = @ExpectedVersion;",
                new { Id = priorityId, ExpectedVersion = expectedVersion },
                transaction,
                cancellationToken: token));
            if (affectedRows != 1)
            {
                var actualVersion = await GetPriorityVersionAsync(connection, transaction, priorityId, token);
                throw new CardCatalogConcurrencyException(
                    "priorità",
                    priorityId,
                    expectedVersion,
                    actualVersion);
            }

            await CompactPriorityOrderAsync(connection, transaction, token);
            transaction.Commit();
        }, cancellationToken);
    }

    public Task ReorderPrioritiesAsync(
        IReadOnlyList<CatalogOrderItem> orderedItems,
        CancellationToken cancellationToken = default) =>
        ReorderAsync("Priorities", "priorità", orderedItems, cancellationToken);

    public async Task<CardTypeDefinition> SaveCardTypeAsync(
        CardTypeSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeCardTypeRequest(request);

        return await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            await EnsureCardTypeNameUniqueAsync(
                connection,
                transaction,
                normalized.Name,
                normalized.Id,
                token);

            long cardTypeId;
            if (normalized.Id is null)
            {
                var sortOrder = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM CardTypes;",
                    transaction: transaction,
                    cancellationToken: token));

                cardTypeId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    """
                    INSERT INTO CardTypes
                        (Name, ColorHex, SortOrder, IsActive, IsDefault, Version)
                    VALUES
                        (@Name, @ColorHex, @SortOrder, @IsActive, 0, 1);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        normalized.Name,
                        normalized.ColorHex,
                        SortOrder = sortOrder,
                        normalized.IsActive,
                    },
                    transaction,
                    cancellationToken: token));
            }
            else
            {
                cardTypeId = normalized.Id.Value;
                var current = await GetCardTypeAsync(connection, transaction, cardTypeId, token);
                EnsureVersion("fascia", cardTypeId, normalized.ExpectedVersion, current?.Version);

                EnsureCardTypeSystemConstraints(current!, normalized);

                var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE CardTypes
                    SET Name = @Name,
                        ColorHex = @ColorHex,
                        IsActive = @IsActive,
                        Version = Version + 1
                    WHERE Id = @Id
                      AND Version = @ExpectedVersion;
                    """,
                    new
                    {
                        Id = cardTypeId,
                        normalized.Name,
                        normalized.ColorHex,
                        normalized.IsActive,
                        normalized.ExpectedVersion,
                    },
                    transaction,
                    cancellationToken: token));

                if (affectedRows != 1)
                {
                    var actualVersion = await GetCardTypeVersionAsync(connection, transaction, cardTypeId, token);
                    throw new CardCatalogConcurrencyException(
                        "fascia",
                        cardTypeId,
                        normalized.ExpectedVersion,
                        actualVersion);
                }
            }

            var saved = await GetCardTypeAsync(connection, transaction, cardTypeId, token)
                ?? throw new InvalidOperationException("La fascia salvata non è stata riletta.");
            transaction.Commit();
            return saved;
        }, cancellationToken);
    }

    public async Task DeleteCardTypeAsync(
        CardTypeDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.CardTypeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ExpectedVersion);

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var current = await GetCardTypeAsync(
                connection,
                transaction,
                request.CardTypeId,
                token);
            EnsureVersion(
                "fascia",
                request.CardTypeId,
                request.ExpectedVersion,
                current?.Version);
            var source = current!;

            if (source.IsSystem)
            {
                throw new CardCatalogValidationException(
                    "La fascia di sistema Generica non può essere eliminata.");
            }

            var usageCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM Cards WHERE CardTypeId = @CardTypeId;",
                new { CardTypeId = request.CardTypeId },
                transaction,
                cancellationToken: token));

            CardTypeDefinition? destination = null;
            if (usageCount > 0)
            {
                if (request.DestinationCardTypeId is null ||
                    request.DestinationExpectedVersion is null)
                {
                    throw new CardCatalogValidationException(
                        "Scegli una fascia di destinazione per le card da trasferire.");
                }

                if (request.DestinationCardTypeId == request.CardTypeId)
                {
                    throw new CardCatalogValidationException(
                        "La fascia di destinazione deve essere diversa da quella eliminata.");
                }

                destination = await GetCardTypeAsync(
                    connection,
                    transaction,
                    request.DestinationCardTypeId.Value,
                    token);
                EnsureVersion(
                    "fascia di destinazione",
                    request.DestinationCardTypeId.Value,
                    request.DestinationExpectedVersion.Value,
                    destination?.Version);
                var resolvedDestination = destination!;

                if (!resolvedDestination.IsActive)
                {
                    throw new CardCatalogValidationException(
                        "La fascia di destinazione deve essere attiva.");
                }

                var cards = (await connection.QueryAsync<CardTypeReassignmentRow>(new CommandDefinition(
                    """
                    SELECT card.Id, card.StableId, card.ColumnId, card.SortOrder,
                           card.PriorityId, card.PriorityAssignedAtUtc, card.DueAtUtc AS PreviousDueAtUtc,
                           priority.DefaultDueHours,
                           rule.DueHours AS OverrideDueHours
                    FROM Cards card
                    LEFT JOIN Priorities priority
                      ON priority.Id = card.PriorityId
                    LEFT JOIN PriorityTypeDeadlines rule
                      ON rule.PriorityId = card.PriorityId
                     AND rule.CardTypeId = @DestinationCardTypeId
                    WHERE card.CardTypeId = @SourceCardTypeId
                    ORDER BY card.ColumnId, card.SortOrder, card.Id;
                    """,
                    new
                    {
                        DestinationCardTypeId = resolvedDestination.Id,
                        SourceCardTypeId = source.Id,
                    },
                    transaction,
                    cancellationToken: token))).AsList();

                var nowText = FormatUtc(_timeProvider.GetUtcNow());
                var activeLockCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM CardEditLocks editLock
                    JOIN Cards card ON card.Id = editLock.CardId
                    WHERE card.CardTypeId = @CardTypeId
                      AND editLock.ExpiresAtUtc > @NowUtc;
                    """,
                    new { CardTypeId = request.CardTypeId, NowUtc = nowText },
                    transaction,
                    cancellationToken: token));
                if (activeLockCount > 0)
                {
                    throw new CardCatalogValidationException(
                        activeLockCount == 1
                            ? "Una card della fascia è attualmente in modifica. Chiudila prima di eliminare la fascia."
                            : $"{activeLockCount} card della fascia sono attualmente in modifica. Chiudile prima di eliminare la fascia.");
                }

                var updatedBy = NormalizeUserName(request.UpdatedBy);
                foreach (var card in cards)
                {
                    var dueAtUtc = CalculateReassignedDueAt(card);
                    var updatedRows = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE Cards
                        SET CardTypeId = @DestinationCardTypeId,
                            DueAtUtc = @DueAtUtc,
                            UpdatedBy = @UpdatedBy,
                            UpdatedAtUtc = @UpdatedAtUtc,
                            Version = Version + 1
                        WHERE Id = @Id
                          AND CardTypeId = @SourceCardTypeId;
                        """,
                        new
                        {
                            card.Id,
                            DestinationCardTypeId = resolvedDestination.Id,
                            DueAtUtc = dueAtUtc,
                            UpdatedBy = updatedBy,
                            UpdatedAtUtc = nowText,
                            SourceCardTypeId = source.Id,
                        },
                        transaction,
                        cancellationToken: token));
                    if (updatedRows != 1)
                    {
                        throw new CardCatalogConcurrencyException("fasce");
                    }

                    await InsertCardTypeChangedEventAsync(
                        connection,
                        transaction,
                        card,
                        source,
                        resolvedDestination,
                        dueAtUtc,
                        nowText,
                        updatedBy,
                        token);
                }
            }

            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM CardTypes WHERE Id = @Id AND Version = @ExpectedVersion;",
                new { Id = request.CardTypeId, ExpectedVersion = request.ExpectedVersion },
                transaction,
                cancellationToken: token));
            if (affectedRows != 1)
            {
                var actualVersion = await GetCardTypeVersionAsync(
                    connection,
                    transaction,
                    request.CardTypeId,
                    token);
                throw new CardCatalogConcurrencyException(
                    "fascia",
                    request.CardTypeId,
                    request.ExpectedVersion,
                    actualVersion);
            }

            await CompactCardTypeOrderAsync(connection, transaction, token);
            transaction.Commit();
        }, cancellationToken);
    }

    public async Task ReorderCardTypesAsync(
        IReadOnlyList<CatalogOrderItem> orderedItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        if (orderedItems.Select(item => item.Id).Distinct().Count() != orderedItems.Count)
        {
            throw new CardCatalogValidationException("L'ordinamento contiene fasce duplicate.");
        }

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var generic = await connection.QuerySingleOrDefaultAsync<CatalogOrderItemRow>(new CommandDefinition(
                "SELECT Id, Version AS ExpectedVersion, SortOrder, SystemKey " +
                "FROM CardTypes WHERE SystemKey = @SystemKey;",
                new { SystemKey = SystemCardTypeKeys.Generic },
                transaction,
                cancellationToken: token))
                ?? throw new InvalidOperationException(
                    "La fascia di sistema Generica non è disponibile.");

            if (orderedItems.Any(item => item.Id == generic.Id))
            {
                throw new CardCatalogValidationException(
                    "La fascia Generica non partecipa al riordino e deve rimanere al primo posto.");
            }

            var current = (await connection.QueryAsync<CatalogOrderItemRow>(new CommandDefinition(
                "SELECT Id, Version AS ExpectedVersion, SortOrder, SystemKey " +
                "FROM CardTypes WHERE IsSystem = 0 ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            if (current.Count != orderedItems.Count ||
                !current.Select(item => item.Id).Order().SequenceEqual(
                    orderedItems.Select(item => item.Id).Order()))
            {
                throw new CardCatalogConcurrencyException("fasce");
            }

            for (var index = 0; index < orderedItems.Count; index++)
            {
                var requested = orderedItems[index];
                var existing = current.Single(item => item.Id == requested.Id);
                EnsureVersion(
                    "fascia",
                    requested.Id,
                    requested.ExpectedVersion,
                    existing.ExpectedVersion);

                var targetSortOrder = index + 1;
                if (existing.SortOrder == targetSortOrder)
                {
                    continue;
                }

                var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE CardTypes SET SortOrder = @SortOrder, Version = Version + 1 " +
                    "WHERE Id = @Id AND Version = @ExpectedVersion;",
                    new
                    {
                        SortOrder = targetSortOrder,
                        requested.Id,
                        requested.ExpectedVersion,
                    },
                    transaction,
                    cancellationToken: token));
                if (affectedRows != 1)
                {
                    throw new CardCatalogConcurrencyException(
                        "fascia",
                        requested.Id,
                        requested.ExpectedVersion,
                        null);
                }
            }

            transaction.Commit();
        }, cancellationToken);
    }

    private async Task ReorderAsync(
        string tableName,
        string catalogName,
        IReadOnlyList<CatalogOrderItem> orderedItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        if (orderedItems.Select(item => item.Id).Distinct().Count() != orderedItems.Count)
        {
            throw new CardCatalogValidationException("L'ordinamento contiene voci duplicate.");
        }

        await _writePipeline.ExecuteAsync(async token =>
        {
            await using var connection = _connectionFactory.Create();
            using var transaction = connection.BeginTransaction(deferred: false);

            var current = (await connection.QueryAsync<CatalogOrderItemRow>(new CommandDefinition(
                $"SELECT Id, Version AS ExpectedVersion, SortOrder, NULL AS SystemKey " +
                $"FROM {tableName} ORDER BY SortOrder, Id;",
                transaction: transaction,
                cancellationToken: token))).AsList();

            if (current.Count != orderedItems.Count ||
                !current.Select(item => item.Id).Order().SequenceEqual(orderedItems.Select(item => item.Id).Order()))
            {
                throw new CardCatalogConcurrencyException(catalogName);
            }

            for (var index = 0; index < orderedItems.Count; index++)
            {
                var requested = orderedItems[index];
                var existing = current.Single(item => item.Id == requested.Id);
                EnsureVersion(catalogName, requested.Id, requested.ExpectedVersion, existing.ExpectedVersion);

                if (existing.SortOrder == index)
                {
                    continue;
                }

                var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE {tableName} SET SortOrder = @SortOrder, Version = Version + 1 " +
                    "WHERE Id = @Id AND Version = @ExpectedVersion;",
                    new
                    {
                        SortOrder = index,
                        requested.Id,
                        requested.ExpectedVersion,
                    },
                    transaction,
                    cancellationToken: token));
                if (affectedRows != 1)
                {
                    throw new CardCatalogConcurrencyException(
                        catalogName,
                        requested.Id,
                        requested.ExpectedVersion,
                        null);
                }
            }

            transaction.Commit();
        }, cancellationToken);
    }

    private static PrioritySaveRequest NormalizePriorityRequest(PrioritySaveRequest request)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new CardCatalogValidationException("Il codice della priorità è obbligatorio.");
        }

        if (code.Length > MaximumPriorityCodeLength)
        {
            throw new CardCatalogValidationException(
                $"Il codice della priorità non può superare {MaximumPriorityCodeLength} caratteri.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CardCatalogValidationException("Il nome della priorità è obbligatorio.");
        }

        if (name.Length > MaximumNameLength)
        {
            throw new CardCatalogValidationException(
                $"Il nome della priorità non può superare {MaximumNameLength} caratteri.");
        }

        if (description?.Length > MaximumDescriptionLength)
        {
            throw new CardCatalogValidationException(
                $"La descrizione non può superare {MaximumDescriptionLength} caratteri.");
        }

        ValidateDueHours(request.DefaultDueHours, "La scadenza predefinita");
        if (request.IsDefault && !request.IsActive)
        {
            throw new CardCatalogValidationException(
                "Una priorità predefinita deve essere attiva.");
        }

        var overrides = request.DeadlineOverrides ?? [];
        if (overrides.Select(item => item.CardTypeId).Distinct().Count() != overrides.Count)
        {
            throw new CardCatalogValidationException(
                "Una fascia può avere una sola regola alternativa per priorità.");
        }

        foreach (var deadlineOverride in overrides)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deadlineOverride.CardTypeId);
            ValidateDueHours(deadlineOverride.DueHours, "La scadenza alternativa");
        }

        return request with
        {
            Code = code,
            Name = name,
            Description = description,
            DeadlineOverrides = overrides.ToArray(),
        };
    }

    private static CardTypeSaveRequest NormalizeCardTypeRequest(CardTypeSaveRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var colorHex = request.ColorHex?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CardCatalogValidationException("Il nome della fascia è obbligatorio.");
        }

        if (name.Length > MaximumNameLength)
        {
            throw new CardCatalogValidationException(
                $"Il nome della fascia non può superare {MaximumNameLength} caratteri.");
        }

        if (!ColorHexRegex().IsMatch(colorHex))
        {
            throw new CardCatalogValidationException(
                "Il colore deve essere espresso nel formato #RRGGBB.");
        }

        return request with
        {
            Name = name,
            ColorHex = colorHex,
        };
    }

    private static void EnsureCardTypeSystemConstraints(
        CardTypeDefinition current,
        CardTypeSaveRequest request)
    {
        if (!current.IsSystem)
        {
            return;
        }

        if (!string.Equals(current.Name, request.Name, StringComparison.Ordinal) ||
            current.IsActive != request.IsActive)
        {
            throw new CardCatalogValidationException(
                "La fascia di sistema Generica può cambiare soltanto colore.");
        }
    }

    private static void ValidateDueHours(int dueHours, string fieldName)
    {
        if (dueHours <= 0 || dueHours > MaximumDueHours)
        {
            throw new CardCatalogValidationException(
                $"{fieldName} deve essere compresa tra 1 ora e 3650 giorni.");
        }
    }

    private static async Task EnsurePriorityUniqueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string code,
        string name,
        long? excludedId,
        CancellationToken cancellationToken)
    {
        var duplicate = await connection.QuerySingleOrDefaultAsync<DuplicateCatalogRow>(new CommandDefinition(
            """
            SELECT Code AS ExistingCode, Name AS ExistingName
            FROM Priorities
            WHERE (@ExcludedId IS NULL OR Id <> @ExcludedId)
              AND (Code = @Code COLLATE NOCASE OR Name = @Name COLLATE NOCASE)
            LIMIT 1;
            """,
            new { ExcludedId = excludedId, Code = code, Name = name },
            transaction,
            cancellationToken: cancellationToken));

        if (duplicate is null)
        {
            return;
        }

        if (string.Equals(duplicate.ExistingCode, code, StringComparison.OrdinalIgnoreCase))
        {
            throw new CardCatalogValidationException($"Esiste già una priorità con codice '{code}'.");
        }

        throw new CardCatalogValidationException($"Esiste già una priorità chiamata '{name}'.");
    }

    private static async Task EnsureCardTypeNameUniqueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        long? excludedId,
        CancellationToken cancellationToken)
    {
        var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM CardTypes
            WHERE (@ExcludedId IS NULL OR Id <> @ExcludedId)
              AND Name = @Name COLLATE NOCASE;
            """,
            new { ExcludedId = excludedId, Name = name },
            transaction,
            cancellationToken: cancellationToken));
        if (exists > 0)
        {
            throw new CardCatalogValidationException($"Esiste già una fascia chiamata '{name}'.");
        }
    }

    private static async Task EnsureCardTypesExistAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<long> cardTypeIds,
        CancellationToken cancellationToken)
    {
        var ids = cardTypeIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var existingCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM CardTypes WHERE Id IN @Ids;",
            new { Ids = ids },
            transaction,
            cancellationToken: cancellationToken));
        if (existingCount != ids.Length)
        {
            throw new CardCatalogValidationException(
                "Una delle fasce usate nelle regole di scadenza non esiste più.");
        }
    }

    private static async Task ClearOtherPriorityDefaultsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? excludedId,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Priorities
            SET IsDefault = 0,
                Version = Version + 1
            WHERE IsDefault = 1
              AND (@ExcludedId IS NULL OR Id <> @ExcludedId);
            """,
            new { ExcludedId = excludedId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<PriorityDefinition?> GetPriorityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long priorityId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<PriorityDefinition>(new CommandDefinition(
            "SELECT Id, Code, Name, Description, DefaultDueHours, SortOrder, IsActive, IsDefault, Version " +
            "FROM Priorities WHERE Id = @Id;",
            new { Id = priorityId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<CardTypeDefinition?> GetCardTypeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardTypeId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<CardTypeDefinition>(new CommandDefinition(
            "SELECT cardType.Id, cardType.Name, cardType.ColorHex, cardType.SortOrder, " +
            "cardType.IsActive, cardType.IsDefault, cardType.Version, cardType.SystemKey, " +
            "cardType.IsSystem, (SELECT COUNT(*) FROM Cards card WHERE card.CardTypeId = cardType.Id) AS CardCount " +
            "FROM CardTypes cardType WHERE cardType.Id = @Id;",
            new { Id = cardTypeId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<int?> GetPriorityVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long priorityId,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT Version FROM Priorities WHERE Id = @Id;",
            new { Id = priorityId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<int?> GetCardTypeVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long cardTypeId,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT Version FROM CardTypes WHERE Id = @Id;",
            new { Id = cardTypeId },
            transaction,
            cancellationToken: cancellationToken));

    private static void EnsureVersion(
        string catalogName,
        long itemId,
        int expectedVersion,
        int? actualVersion)
    {
        if (actualVersion != expectedVersion)
        {
            throw new CardCatalogConcurrencyException(
                catalogName,
                itemId,
                expectedVersion,
                actualVersion);
        }
    }

    private static async Task CompactPriorityOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var items = (await connection.QueryAsync<CatalogOrderItemRow>(new CommandDefinition(
            "SELECT Id, Version AS ExpectedVersion, SortOrder FROM Priorities ORDER BY SortOrder, Id;",
            transaction: transaction,
            cancellationToken: cancellationToken))).AsList();
        await CompactOrderAsync(connection, transaction, "Priorities", items, cancellationToken);
    }

    private static async Task CompactCardTypeOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var items = (await connection.QueryAsync<CatalogOrderItemRow>(new CommandDefinition(
            "SELECT Id, Version AS ExpectedVersion, SortOrder, SystemKey " +
            "FROM CardTypes WHERE IsSystem = 0 ORDER BY SortOrder, Id;",
            transaction: transaction,
            cancellationToken: cancellationToken))).AsList();

        for (var index = 0; index < items.Count; index++)
        {
            var targetSortOrder = index + 1;
            if (items[index].SortOrder == targetSortOrder)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE CardTypes SET SortOrder = @SortOrder, Version = Version + 1 WHERE Id = @Id;",
                new { SortOrder = targetSortOrder, items[index].Id },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static async Task CompactOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        IReadOnlyList<CatalogOrderItemRow> items,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].SortOrder == index)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE {tableName} SET SortOrder = @SortOrder, Version = Version + 1 WHERE Id = @Id;",
                new { SortOrder = index, items[index].Id },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static string? CalculateReassignedDueAt(CardTypeReassignmentRow card)
    {
        if (card.PriorityId is null)
        {
            return null;
        }

        if (card.DefaultDueHours is null)
        {
            throw new InvalidOperationException(
                $"La priorità {card.PriorityId.Value} della card {card.Id} non esiste più.");
        }

        if (string.IsNullOrWhiteSpace(card.PriorityAssignedAtUtc))
        {
            throw new InvalidOperationException(
                $"La card {card.Id} ha una priorità senza data di assegnazione.");
        }

        var assignedAt = DateTimeOffset.Parse(
            card.PriorityAssignedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return FormatUtc(PriorityDeadlineCalculator.CalculateDueAt(
            assignedAt,
            card.DefaultDueHours.Value,
            card.OverrideDueHours));
    }

    private async Task InsertCardTypeChangedEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CardTypeReassignmentRow card,
        CardTypeDefinition source,
        CardTypeDefinition destination,
        string? dueAtUtc,
        string occurredAtUtc,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        var audit = _auditContextProvider.Current;
        await connection.ExecuteAsync(new CommandDefinition(
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
                EventType = CardEventTypes.TypeChanged,
                OccurredAtUtc = occurredAtUtc,
                UserName = updatedBy,
                audit.SessionId,
                audit.MachineName,
                Summary = $"Fascia modificata da {source.Name} a {destination.Name} durante l'eliminazione della fascia.",
                DataJson = JsonSerializer.Serialize(new
                {
                    previousCardTypeId = source.Id,
                    previousCardTypeName = source.Name,
                    cardTypeId = destination.Id,
                    cardTypeName = destination.Name,
                    card.ColumnId,
                    card.SortOrder,
                    previousDueAtUtc = card.PreviousDueAtUtc,
                    dueAtUtc,
                    reason = "CardTypeDeleted",
                }),
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeUserName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Sconosciuto" : value.Trim();

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorHexRegex();

    private sealed class DuplicateCatalogRow
    {
        public string ExistingCode { get; set; } = string.Empty;

        public string ExistingName { get; set; } = string.Empty;
    }

    private sealed class CardTypeReassignmentRow
    {
        public long Id { get; set; }

        public string StableId { get; set; } = string.Empty;

        public long ColumnId { get; set; }

        public int SortOrder { get; set; }

        public long? PriorityId { get; set; }

        public string? PriorityAssignedAtUtc { get; set; }

        public string? PreviousDueAtUtc { get; set; }

        public int? DefaultDueHours { get; set; }

        public int? OverrideDueHours { get; set; }
    }

    private sealed class CatalogOrderItemRow
    {
        public long Id { get; set; }

        public int ExpectedVersion { get; set; }

        public int SortOrder { get; set; }

        public string? SystemKey { get; set; }
    }
}
