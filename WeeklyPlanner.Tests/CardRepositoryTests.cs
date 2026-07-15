using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

/// <summary>
/// Test di integrazione su un file SQLite temporaneo reale. Le operazioni di ordinamento vengono
/// verificate dopo una nuova lettura, così il test controlla lo stato persistito e non quello in memoria.
/// </summary>
public sealed class CardRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private const string TestSessionId = "test-session";

    private readonly ICardRepository _repository;
    private readonly ICardEditLockRepository _editLockRepository;
    private readonly IBoardRevisionRepository _revisionRepository;

    public CardRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"weeklyplanner-tests-{Guid.NewGuid():N}.db");

        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        new DatabaseInitializer(_connectionFactory).EnsureInitialized();

        var pipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        _repository = new CardRepository(_connectionFactory, pipeline);
        _editLockRepository = new CardEditLockRepository(_connectionFactory, pipeline);
        _revisionRepository = new BoardRevisionRepository(_connectionFactory);
    }


    [Fact]
    public async Task CreateAsync_rejects_blank_title_without_advancing_revision()
    {
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        var exception = await Assert.ThrowsAsync<CardValidationException>(() =>
            _repository.CreateAsync(new Card
            {
                ColumnId = 0,
                Title = "   ",
                CreatedBy = "Test",
                UpdatedBy = "Test",
            }));

        Assert.Contains("obbligatorio", exception.Message);
        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_title_longer_than_domain_limit()
    {
        var title = new string('X', Card.MaxTitleLength + 1);

        var exception = await Assert.ThrowsAsync<CardValidationException>(() =>
            _repository.CreateAsync(new Card
            {
                ColumnId = 0,
                Title = title,
                CreatedBy = "Test",
                UpdatedBy = "Test",
            }));

        Assert.Contains(Card.MaxTitleLength.ToString(), exception.Message);
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task UpdateAsync_trims_title_before_persisting()
    {
        var card = await CreateCardAsync("Originale");
        await AcquireEditLockAsync(card.Id);
        card.Title = "  Titolo aggiornato  ";

        var updated = await _repository.UpdateAsync(card, TestSessionId);

        Assert.Equal("Titolo aggiornato", updated.Title);
        Assert.Equal("Titolo aggiornato",
            (await _repository.GetAllAsync()).Single(item => item.Id == card.Id).Title);
    }

    [Fact]
    public async Task CreateAsync_appends_card_and_ignores_caller_sort_order()
    {
        var first = await CreateCardAsync("Prima", columnId: 0, requestedSortOrder: 42);
        var second = await CreateCardAsync("Seconda", columnId: 0, requestedSortOrder: 42);

        Assert.True(first.Id > 0);
        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.NotEmpty(first.UpdatedAtUtc);

        await AssertColumnAsync(0, "Prima", "Seconda");
    }

    [Fact]
    public async Task CreateAsync_repairs_legacy_gaps_before_appending()
    {
        var first = await CreateCardAsync("A");
        var second = await CreateCardAsync("B");
        var third = await CreateCardAsync("C");

        using (var connection = _connectionFactory.Create())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"UPDATE Cards SET SortOrder = 5 WHERE Id IN ({second.Id}, {third.Id});";
            command.ExecuteNonQuery();
        }

        var fourth = await CreateCardAsync("D", requestedSortOrder: 99);

        Assert.Equal(3, fourth.SortOrder);
        await AssertColumnAsync(0, "A", "B", "C", "D");
        Assert.Equal(0, first.SortOrder);
    }

    [Fact]
    public async Task CreateAsync_places_the_new_card_at_the_end_of_its_cell_and_keeps_canonical_lane_order()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var sqlId = GetCardTypeId("SQL");
        await CreateCardAsync("SQL prima", cardTypeId: sqlId);
        await CreateCardAsync("Generica dopo", cardTypeId: genericId);
        await CreateCardAsync("SQL seconda", cardTypeId: sqlId);

        var cards = (await _repository.GetAllAsync())
            .Where(card => card.ColumnId == 0)
            .OrderBy(card => card.SortOrder)
            .ThenBy(card => card.Id)
            .ToList();

        Assert.Equal(
            new[] { "Generica dopo", "SQL prima", "SQL seconda" },
            cards.Select(card => card.Title));
        Assert.Equal(Enumerable.Range(0, cards.Count), cards.Select(card => card.SortOrder));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_inactive_card_type()
    {
        var sqlId = GetCardTypeId("SQL");
        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                "UPDATE CardTypes SET IsActive = 0, Version = Version + 1 WHERE Id = @Id;",
                new { Id = sqlId });
        }
        var revisionBeforeCreate = await _revisionRepository.GetCurrentRevisionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateCardAsync("Non creare", cardTypeId: sqlId));

        Assert.Contains("inattiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _repository.GetAllAsync());
        Assert.Equal(revisionBeforeCreate, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_inactive_priority_without_advancing_revision()
    {
        var priorityId = GetPriorityId("D");
        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                "UPDATE Priorities SET IsActive = 0, Version = Version + 1 WHERE Id = @Id;",
                new { Id = priorityId });
        }
        var revisionBeforeCreate = await _revisionRepository.GetCurrentRevisionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateCardAsync("Non creare", priorityId: priorityId));

        Assert.Contains("inattiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _repository.GetAllAsync());
        Assert.Equal(revisionBeforeCreate, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task UpdateAsync_rejects_assigning_an_inactive_priority_without_mutating_the_card()
    {
        var card = await CreateCardAsync("Senza priorità");
        var priorityId = GetPriorityId("D");
        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                "UPDATE Priorities SET IsActive = 0, Version = Version + 1 WHERE Id = @Id;",
                new { Id = priorityId });
        }
        await AcquireEditLockAsync(card.Id);
        var revisionBeforeUpdate = await _revisionRepository.GetCurrentRevisionAsync();
        card.PriorityId = priorityId;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.UpdateAsync(card, TestSessionId));

        Assert.Contains("inattiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        var persisted = Assert.Single(await _repository.GetAllAsync());
        Assert.Null(persisted.PriorityId);
        Assert.Equal(revisionBeforeUpdate, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task GetAllAsync_returns_created_card()
    {
        await CreateCardAsync("Card A");

        var all = await _repository.GetAllAsync();

        Assert.Contains(all, card => card.Title == "Card A");
    }

    [Fact]
    public async Task CreateAsync_assigns_the_generic_system_type_when_not_specified()
    {
        var created = await CreateCardAsync("Card generica");

        Assert.NotNull(created.CardTypeId);
        using var connection = _connectionFactory.Create();
        Assert.Equal(
            SystemCardTypeKeys.Generic,
            connection.ExecuteScalar<string>(
                "SELECT SystemKey FROM CardTypes WHERE Id = @CardTypeId;",
                new { created.CardTypeId }));
    }

    [Fact]
    public async Task MoveToCellAsync_changes_type_and_state_and_recalculates_due_date_atomically()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var instrumentalExamId = GetCardTypeId("Esame strumentale");
        var differiblePriorityId = GetPriorityId("D");
        var card = await CreateCardAsync(
            "Da riclassificare",
            columnId: 0,
            cardTypeId: genericId,
            priorityId: differiblePriorityId);
        var previousDueAt = card.DueAtUtc;

        await _repository.MoveToCellAsync(
            card.Id,
            targetColumnId: 1,
            targetCardTypeId: instrumentalExamId,
            targetCellIndex: 0,
            updatedBy: "Mover");

        var moved = (await _repository.GetAllAsync()).Single(item => item.Id == card.Id);
        Assert.Equal(1, moved.ColumnId);
        Assert.Equal(instrumentalExamId, moved.CardTypeId);
        Assert.NotEqual(previousDueAt, moved.DueAtUtc);
        Assert.Equal(
            TimeSpan.FromDays(60),
            DateTimeOffset.Parse(moved.DueAtUtc!) -
            DateTimeOffset.Parse(moved.PriorityAssignedAtUtc!));
        Assert.Equal(card.Version + 1, moved.Version);

        using var connection = _connectionFactory.Create();
        var history = connection.QuerySingle<CardEvent>(
            """
            SELECT EventType, Summary, DataJson
            FROM CardEvents
            WHERE CardId = @CardId
              AND EventType = @EventType
            ORDER BY Id DESC
            LIMIT 1;
            """,
            new { CardId = card.Id, EventType = CardEventTypes.Moved });
        Assert.Equal(CardEventTypes.Moved, history.EventType);
        Assert.Contains("Generica / BACKLOG", history.Summary, StringComparison.Ordinal);
        Assert.Contains("Esame strumentale / TODO", history.Summary, StringComparison.Ordinal);
        Assert.Contains("previousCardTypeId", history.DataJson, StringComparison.Ordinal);
        Assert.Contains("previousColumnId", history.DataJson, StringComparison.Ordinal);
        Assert.Contains("previousDueAtUtc", history.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveToCellAsync_uses_a_cell_local_index_and_preserves_other_lanes()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var sqlId = GetCardTypeId("SQL");
        await CreateCardAsync("Generica A", columnId: 1, cardTypeId: genericId);
        await CreateCardAsync("SQL A", columnId: 1, cardTypeId: sqlId);
        await CreateCardAsync("Generica B", columnId: 1, cardTypeId: genericId);
        await CreateCardAsync("SQL B", columnId: 1, cardTypeId: sqlId);
        var moving = await CreateCardAsync("SQL nuova", columnId: 0, cardTypeId: sqlId);

        await _repository.MoveToCellAsync(
            moving.Id,
            targetColumnId: 1,
            targetCardTypeId: sqlId,
            targetCellIndex: 1,
            updatedBy: "Mover");

        var targetCards = (await _repository.GetAllAsync())
            .Where(card => card.ColumnId == 1)
            .OrderBy(card => card.SortOrder)
            .ThenBy(card => card.Id)
            .ToList();
        Assert.Equal(
            new[] { "Generica A", "Generica B" },
            targetCards.Where(card => card.CardTypeId == genericId).Select(card => card.Title));
        Assert.Equal(
            new[] { "SQL A", "SQL nuova", "SQL B" },
            targetCards.Where(card => card.CardTypeId == sqlId).Select(card => card.Title));
        Assert.Equal(
            Enumerable.Range(0, targetCards.Count),
            targetCards.Select(card => card.SortOrder));
    }

    [Fact]
    public async Task MoveToCellAsync_no_op_does_not_advance_revision_or_history()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var card = await CreateCardAsync("Ferma", cardTypeId: genericId);
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();
        int eventsBefore;
        using (var connection = _connectionFactory.Create())
        {
            eventsBefore = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM CardEvents WHERE CardId = @CardId;",
                new { CardId = card.Id });
        }

        await _repository.MoveToCellAsync(
            card.Id,
            targetColumnId: 0,
            targetCardTypeId: genericId,
            targetCellIndex: 0,
            updatedBy: "Mover");

        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
        using var verifyConnection = _connectionFactory.Create();
        Assert.Equal(
            eventsBefore,
            verifyConnection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM CardEvents WHERE CardId = @CardId;",
                new { CardId = card.Id }));
    }

    [Fact]
    public async Task MoveToCellAsync_rejects_an_inactive_destination_without_mutating_the_card()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var sqlId = GetCardTypeId("SQL");
        var card = await CreateCardAsync("Non archiviare", cardTypeId: genericId);
        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                "UPDATE CardTypes SET IsActive = 0, Version = Version + 1 WHERE Id = @Id;",
                new { Id = sqlId });
        }
        var revisionBeforeMove = await _revisionRepository.GetCurrentRevisionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.MoveToCellAsync(
                card.Id,
                targetColumnId: 0,
                targetCardTypeId: sqlId,
                targetCellIndex: 0,
                updatedBy: "Mover"));

        Assert.Contains("inattiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        var persisted = (await _repository.GetAllAsync()).Single(item => item.Id == card.Id);
        Assert.Equal(genericId, persisted.CardTypeId);
        Assert.Equal(0, persisted.ColumnId);
        Assert.Equal(revisionBeforeMove, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task MoveToCellAsync_rolls_back_position_type_due_date_and_revision_when_audit_fails()
    {
        var genericId = GetCardTypeId(SystemCardTypeKeys.Generic, bySystemKey: true);
        var instrumentalExamId = GetCardTypeId("Esame strumentale");
        var differiblePriorityId = GetPriorityId("D");
        var card = await CreateCardAsync(
            "Rollback",
            columnId: 0,
            cardTypeId: genericId,
            priorityId: differiblePriorityId);
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                """
                CREATE TRIGGER TR_Test_AbortBidimensionalMoveEvent
                BEFORE INSERT ON CardEvents
                WHEN NEW.EventType = 'Moved'
                BEGIN
                    SELECT RAISE(ABORT, 'forced bidimensional move audit failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<SqliteException>(() =>
            _repository.MoveToCellAsync(
                card.Id,
                targetColumnId: 1,
                targetCardTypeId: instrumentalExamId,
                targetCellIndex: 0,
                updatedBy: "Mover"));

        var persisted = (await _repository.GetAllAsync()).Single(item => item.Id == card.Id);
        Assert.Equal(0, persisted.ColumnId);
        Assert.Equal(genericId, persisted.CardTypeId);
        Assert.Equal(card.DueAtUtc, persisted.DueAtUtc);
        Assert.Equal(card.Version, persisted.Version);
        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task DeleteAsync_compacts_remaining_sort_orders()
    {
        await CreateCardAsync("A");
        var second = await CreateCardAsync("B");
        await CreateCardAsync("C");

        await _repository.DeleteAsync(second.Id, "Deleter");

        await AssertColumnAsync(0, "A", "C");
        var remaining = (await _repository.GetAllAsync()).Where(card => card.ColumnId == 0).ToList();
        Assert.Equal("Test", remaining.Single(card => card.Title == "C").UpdatedBy);
    }

    [Fact]
    public async Task DeleteAsync_unknown_card_throws_without_advancing_revision()
    {
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repository.DeleteAsync(long.MaxValue, "Deleter"));

        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task UpdateAsync_unknown_card_throws_instead_of_succeeding_silently()
    {
        var missing = new Card
        {
            Id = long.MaxValue,
            ColumnId = 0,
            Title = "Non esiste",
            UpdatedBy = "Test",
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repository.UpdateAsync(missing, TestSessionId));
    }

    [Fact]
    public async Task UpdateAsync_requires_an_active_lock_owned_by_the_session()
    {
        var card = await CreateCardAsync("Da modificare");
        card.Title = "Modifica senza lock";

        var exception = await Assert.ThrowsAsync<CardEditLockException>(() =>
            _repository.UpdateAsync(card, TestSessionId));

        Assert.Contains("lock", exception.Message.ToLowerInvariant());
        Assert.Equal(1, (await _repository.GetAllAsync()).Single(item => item.Id == card.Id).Version);
    }

    [Fact]
    public async Task UpdateAsync_increments_version_and_returns_persisted_card()
    {
        var card = await CreateCardAsync("Versione 1");
        await AcquireEditLockAsync(card.Id);
        card.Title = "Versione 2";

        var updated = await _repository.UpdateAsync(card, TestSessionId);

        Assert.Equal(2, updated.Version);
        Assert.Equal("Versione 2", updated.Title);
        Assert.Equal(2, (await _repository.GetAllAsync()).Single(item => item.Id == card.Id).Version);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_stale_version_without_overwriting_data()
    {
        var card = await CreateCardAsync("Originale");
        await AcquireEditLockAsync(card.Id);

        var staleCopy = new Card
        {
            Id = card.Id,
            ColumnId = card.ColumnId,
            Title = "Bozza vecchia",
            SortOrder = card.SortOrder,
            CreatedBy = card.CreatedBy,
            UpdatedBy = "Test",
            UpdatedAtUtc = card.UpdatedAtUtc,
            Version = card.Version,
        };

        card.Title = "Salvataggio corrente";
        card.UpdatedBy = "Test";
        var current = await _repository.UpdateAsync(card, TestSessionId);

        var exception = await Assert.ThrowsAsync<CardConcurrencyException>(() =>
            _repository.UpdateAsync(staleCopy, TestSessionId));

        Assert.Equal(1, exception.ExpectedVersion);
        Assert.Equal(2, exception.ActualVersion);
        Assert.Equal("Salvataggio corrente", current.Title);
        Assert.Equal("Salvataggio corrente",
            (await _repository.GetAllAsync()).Single(item => item.Id == card.Id).Title);
    }

    [Fact]
    public async Task Move_and_delete_are_rejected_while_card_is_being_edited()
    {
        var card = await CreateCardAsync("Bloccata");
        await AcquireEditLockAsync(card.Id);

        await Assert.ThrowsAsync<CardEditLockException>(() =>
            _repository.MoveToCellAsync(
                card.Id,
                targetColumnId: 1,
                targetCardTypeId: card.CardTypeId!.Value,
                targetCellIndex: 0,
                updatedBy: "Mover"));
        await Assert.ThrowsAsync<CardEditLockException>(() =>
            _repository.DeleteAsync(card.Id, "Deleter"));

        Assert.Contains(await _repository.GetAllAsync(), item => item.Id == card.Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_column_without_advancing_revision()
    {
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();
        var card = new Card
        {
            ColumnId = 999,
            Title = "Colonna inesistente",
            SortOrder = 0,
            CreatedBy = "Test",
        };

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repository.CreateAsync(card));

        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task Every_logical_mutation_advances_board_revision()
    {
        var revision = await _revisionRepository.GetCurrentRevisionAsync();

        var card = await CreateCardAsync("Revisioni");
        revision = await AssertRevisionAdvancedAsync(revision);

        card.Title = "Revisioni aggiornate";
        card.UpdatedBy = "Test 2";
        await AcquireEditLockAsync(card.Id);
        card = await _repository.UpdateAsync(card, TestSessionId);
        revision = await AssertRevisionAdvancedAsync(revision);
        await _editLockRepository.ReleaseAsync(card.Id, TestSessionId);
        revision = await AssertRevisionAdvancedAsync(revision);

        await CreateCardAsync("Altra card");
        revision = await AssertRevisionAdvancedAsync(revision);

        await _repository.MoveToCellAsync(
            card.Id,
            targetColumnId: 0,
            targetCardTypeId: card.CardTypeId!.Value,
            targetCellIndex: 2,
            updatedBy: "Test 3");
        revision = await AssertRevisionAdvancedAsync(revision);

        await _repository.DeleteAsync(card.Id, "Test 4");
        await AssertRevisionAdvancedAsync(revision);
    }


    private async Task AcquireEditLockAsync(long cardId)
    {
        var result = await _editLockRepository.TryAcquireAsync(
            cardId,
            TestSessionId,
            "Test",
            "TestMachine",
            TimeSpan.FromSeconds(30));

        Assert.True(result.Acquired);
    }

    private async Task<Card> CreateCardAsync(
        string title,
        long columnId = 0,
        int requestedSortOrder = 0,
        long? cardTypeId = null,
        long? priorityId = null)
    {
        return await _repository.CreateAsync(new Card
        {
            ColumnId = columnId,
            CardTypeId = cardTypeId,
            PriorityId = priorityId,
            Title = title,
            SortOrder = requestedSortOrder,
            CreatedBy = "Test",
            UpdatedBy = "Test",
        });
    }

    private long GetCardTypeId(string value, bool bySystemKey = false)
    {
        using var connection = _connectionFactory.Create();
        return connection.ExecuteScalar<long>(
            bySystemKey
                ? "SELECT Id FROM CardTypes WHERE SystemKey = @Value;"
                : "SELECT Id FROM CardTypes WHERE Name = @Value;",
            new { Value = value });
    }

    private long GetPriorityId(string code)
    {
        using var connection = _connectionFactory.Create();
        return connection.ExecuteScalar<long>(
            "SELECT Id FROM Priorities WHERE Code = @Code;",
            new { Code = code });
    }

    private async Task AssertColumnAsync(long columnId, params string[] expectedTitles)
    {
        var cards = (await _repository.GetAllAsync())
            .Where(card => card.ColumnId == columnId)
            .OrderBy(card => card.SortOrder)
            .ThenBy(card => card.Id)
            .ToList();

        Assert.Equal(expectedTitles, cards.Select(card => card.Title));
        Assert.Equal(Enumerable.Range(0, cards.Count), cards.Select(card => card.SortOrder));
    }

    private async Task<long> AssertRevisionAdvancedAsync(long previousRevision)
    {
        var currentRevision = await _revisionRepository.GetCurrentRevisionAsync();
        Assert.True(currentRevision > previousRevision);
        return currentRevision;
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }
    }
}
