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
    public async Task GetAllAsync_returns_created_card()
    {
        await CreateCardAsync("Card A");

        var all = await _repository.GetAllAsync();

        Assert.Contains(all, card => card.Title == "Card A");
    }

    [Fact]
    public async Task MoveAsync_reorders_every_affected_card_within_same_column()
    {
        var first = await CreateCardAsync("A");
        await CreateCardAsync("B");
        await CreateCardAsync("C");
        var fourth = await CreateCardAsync("D");

        await _repository.MoveAsync(fourth.Id, targetColumnId: 0, targetIndex: 1, updatedBy: "Mover");
        await AssertColumnAsync(0, "A", "D", "B", "C");

        await _repository.MoveAsync(first.Id, targetColumnId: 0, targetIndex: 4, updatedBy: "Mover");
        await AssertColumnAsync(0, "D", "B", "C", "A");
    }

    [Fact]
    public async Task MoveAsync_reorders_source_and_target_columns_atomically()
    {
        await CreateCardAsync("A", columnId: 0);
        var moving = await CreateCardAsync("B", columnId: 0);
        await CreateCardAsync("C", columnId: 0);
        await CreateCardAsync("X", columnId: 1);
        await CreateCardAsync("Y", columnId: 1);

        await _repository.MoveAsync(moving.Id, targetColumnId: 1, targetIndex: 1, updatedBy: "Mover");

        await AssertColumnAsync(0, "A", "C");
        await AssertColumnAsync(1, "X", "B", "Y");

        var allCards = await _repository.GetAllAsync();
        var moved = allCards.Single(card => card.Id == moving.Id);
        Assert.Equal(1, moved.ColumnId);
        Assert.Equal(1, moved.SortOrder);
        Assert.Equal("Mover", moved.UpdatedBy);

        var reorderedTimestamps = allCards
            .Where(card => card.Title is "B" or "C" or "Y")
            .Select(card => card.UpdatedAtUtc)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Single(reorderedTimestamps);
    }

    [Fact]
    public async Task MoveAsync_clamps_target_index_to_end_of_column()
    {
        var moving = await CreateCardAsync("A", columnId: 0);
        await CreateCardAsync("X", columnId: 1);

        await _repository.MoveAsync(moving.Id, targetColumnId: 1, targetIndex: 500, updatedBy: "Mover");

        await AssertColumnAsync(0);
        await AssertColumnAsync(1, "X", "A");
    }

    [Fact]
    public async Task MoveAsync_no_op_does_not_advance_revision()
    {
        var card = await CreateCardAsync("A");
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        await _repository.MoveAsync(card.Id, targetColumnId: 0, targetIndex: 0, updatedBy: "Mover");

        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
        await AssertColumnAsync(0, "A");
    }

    [Fact]
    public async Task MoveAsync_unknown_target_column_rolls_back_without_changing_order()
    {
        var first = await CreateCardAsync("A");
        await CreateCardAsync("B");
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repository.MoveAsync(first.Id, targetColumnId: 999, targetIndex: 0, updatedBy: "Mover"));

        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
        await AssertColumnAsync(0, "A", "B");
    }

    [Fact]
    public async Task MoveAsync_rolls_back_every_update_when_one_reorder_step_fails()
    {
        var first = await CreateCardAsync("A");
        await CreateCardAsync("B");
        var third = await CreateCardAsync("C");
        var revisionBefore = await _revisionRepository.GetCurrentRevisionAsync();

        using (var connection = _connectionFactory.Create())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                CREATE TRIGGER TR_Test_AbortReorder
                BEFORE UPDATE OF SortOrder ON Cards
                WHEN NEW.Id = {first.Id}
                BEGIN
                    SELECT RAISE(ABORT, 'forced reorder failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            _repository.MoveAsync(third.Id, targetColumnId: 0, targetIndex: 0, updatedBy: "Mover"));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(revisionBefore, await _revisionRepository.GetCurrentRevisionAsync());
        await AssertColumnAsync(0, "A", "B", "C");
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
        Assert.Equal("Deleter", remaining.Single(card => card.Title == "C").UpdatedBy);
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
            _repository.MoveAsync(card.Id, 1, 0, "Mover"));
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

        var exception = await Assert.ThrowsAsync<SqliteException>(() => _repository.CreateAsync(card));

        Assert.Equal(19, exception.SqliteErrorCode);
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

        await _repository.MoveAsync(card.Id, 0, 2, "Test 3");
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
        int requestedSortOrder = 0)
    {
        return await _repository.CreateAsync(new Card
        {
            ColumnId = columnId,
            Title = title,
            SortOrder = requestedSortOrder,
            CreatedBy = "Test",
            UpdatedBy = "Test",
        });
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
