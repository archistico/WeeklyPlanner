using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Auditing;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardHistoryFoundationTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-history-tests-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly CardRepository _cards;
    private readonly CardEditLockRepository _locks;
    private readonly CardEventRepository _events;
    private readonly CardCatalogRepository _catalogs;
    private readonly BoardRevisionRepository _revisions;

    public CardHistoryFoundationTests()
    {
        _connectionFactory = new SqliteConnectionFactory(_databasePath);
        new DatabaseInitializer(_connectionFactory).EnsureInitialized();
        var pipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        _cards = new CardRepository(
            _connectionFactory,
            pipeline,
            timeProvider,
            auditContextProvider: new FixedAuditContextProvider("session-42", "TEST-PC"));
        _locks = new CardEditLockRepository(_connectionFactory, pipeline, timeProvider);
        _events = new CardEventRepository(_connectionFactory);
        _catalogs = new CardCatalogRepository(_connectionFactory);
        _revisions = new BoardRevisionRepository(_connectionFactory);
    }

    [Fact]
    public async Task Default_catalogs_include_requested_priorities_types_and_differible_exception()
    {
        var snapshot = await _catalogs.GetSnapshotAsync();

        Assert.Equal(new[] { "U", "B", "D", "P" }, snapshot.Priorities.Select(item => item.Code));
        Assert.Equal(new[] { 72, 240, 720, 2880 }, snapshot.Priorities.Select(item => item.DefaultDueHours));
        var generic = Assert.Single(snapshot.CardTypes, item => item.SystemKey == SystemCardTypeKeys.Generic);
        Assert.Equal("Generica", generic.Name);
        Assert.True(generic.IsSystem);
        Assert.True(generic.IsDefault);
        Assert.Equal(0, generic.SortOrder);
        Assert.Contains(snapshot.CardTypes, item => item.Name == "WinCliente" && item.ColorHex == "#2563EB");
        Assert.Contains(snapshot.CardTypes, item => item.Name == "Report");
        Assert.Contains(snapshot.CardTypes, item => item.Name == "SQL");

        var differibile = Assert.Single(snapshot.Priorities, item => item.Code == "D");
        var instrumentalExam = Assert.Single(snapshot.CardTypes, item => item.Name == "Esame strumentale");
        var exception = Assert.Single(
            snapshot.DeadlineRules,
            item => item.PriorityId == differibile.Id && item.CardTypeId == instrumentalExam.Id);
        Assert.Equal(60 * 24, exception.DueHours);
    }

    [Fact]
    public async Task Create_update_move_reorder_and_delete_write_persistent_events()
    {
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Titolo riservato",
            Notes = "Note riservate",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        var stableId = card.StableId;

        Assert.False(string.IsNullOrWhiteSpace(stableId));
        Assert.Equal(32, stableId.Length);
        Assert.False(card.CreatedAtIsEstimated);
        Assert.Equal(card.CreatedAtUtc, card.UpdatedAtUtc);

        var acquired = await _locks.TryAcquireAsync(
            card.Id,
            "edit-session",
            "Emilie",
            "TEST-PC",
            TimeSpan.FromMinutes(1));
        Assert.True(acquired.Acquired);

        var catalogs = await _catalogs.GetSnapshotAsync();
        var targetCardType = Assert.Single(
            catalogs.CardTypes,
            item => item.Name == "Esame strumentale");

        card.Title = "Titolo aggiornato";
        card.Notes = "Note aggiornate";
        card.PriorityId = 3;
        card.CardTypeId = targetCardType.Id;
        card.UpdatedBy = "Emilie";
        card = await _cards.UpdateAsync(card, "edit-session");
        await _locks.ReleaseAsync(card.Id, "edit-session");

        var assignedAt = DateTimeOffset.Parse(card.PriorityAssignedAtUtc!);
        var dueAt = DateTimeOffset.Parse(card.DueAtUtc!);
        Assert.Equal(TimeSpan.FromDays(60), dueAt - assignedAt);

        await _cards.MoveToCellAsync(
            card.Id,
            targetColumnId: 1,
            targetCardTypeId: targetCardType.Id,
            targetCellIndex: 0,
            updatedBy: "Emilie");
        var secondCard = await _cards.CreateAsync(new Card
        {
            ColumnId = 1,
            Title = "Seconda",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        await _cards.MoveToCellAsync(
            secondCard.Id,
            targetColumnId: 1,
            targetCardTypeId: targetCardType.Id,
            targetCellIndex: 1,
            updatedBy: "Emilie");
        await _cards.MoveToCellAsync(
            card.Id,
            targetColumnId: 1,
            targetCardTypeId: targetCardType.Id,
            targetCellIndex: 2,
            updatedBy: "Emilie");
        await _cards.DeleteAsync(card.Id, "Emilie");

        var history = await _events.GetByCardStableIdAsync(stableId, take: 50);

        Assert.Contains(history, item => item.EventType == CardEventTypes.Created);
        Assert.Contains(history, item => item.EventType == CardEventTypes.Updated);
        Assert.Contains(history, item => item.EventType == CardEventTypes.PriorityChanged);
        Assert.Contains(history, item => item.EventType == CardEventTypes.TypeChanged);
        Assert.Contains(history, item => item.EventType == CardEventTypes.Moved);
        Assert.Contains(history, item => item.EventType == CardEventTypes.Reordered);
        var deleted = Assert.Single(history, item => item.EventType == CardEventTypes.Deleted);
        Assert.Null(deleted.CardId);
        Assert.All(history, item => Assert.Equal("session-42", item.SessionId));
        Assert.All(history, item => Assert.Equal("TEST-PC", item.MachineName));
        Assert.DoesNotContain(history, item => item.DataJson.Contains("Titolo riservato", StringComparison.Ordinal));
        Assert.DoesNotContain(history, item => item.DataJson.Contains("Note riservate", StringComparison.Ordinal));
        Assert.DoesNotContain(history, item => item.DataJson.Contains("Titolo aggiornato", StringComparison.Ordinal));
        Assert.DoesNotContain(history, item => item.DataJson.Contains("Note aggiornate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Event_failure_rolls_back_the_card_mutation_and_board_revision()
    {
        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                """
                CREATE TRIGGER TR_Test_AbortCardEvent
                BEFORE INSERT ON CardEvents
                WHEN NEW.EventType = 'Created'
                BEGIN
                    SELECT RAISE(ABORT, 'forced event failure');
                END;
                """);
        }
        var revisionBefore = await _revisions.GetCurrentRevisionAsync();

        await Assert.ThrowsAsync<SqliteException>(() => _cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Non deve restare",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        }));

        Assert.Empty(await _cards.GetAllAsync());
        Assert.Equal(revisionBefore, await _revisions.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task Catalog_changes_advance_the_monotonic_board_revision()
    {
        var before = await _revisions.GetCurrentRevisionAsync();

        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                "UPDATE CardTypes SET ColorHex = '#112233', Version = Version + 1 WHERE Name = 'SQL';");
        }

        Assert.True(await _revisions.GetCurrentRevisionAsync() > before);
    }

    [Fact]
    public async Task Card_event_reader_supports_bounded_pagination()
    {
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Paginata",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        var acquired = await _locks.TryAcquireAsync(
            card.Id,
            "edit-session",
            "Emilie",
            "TEST-PC",
            TimeSpan.FromMinutes(1));
        Assert.True(acquired.Acquired);

        card.Title = "Paginata 2";
        card = await _cards.UpdateAsync(card, "edit-session");
        card.Title = "Paginata 3";
        await _cards.UpdateAsync(card, "edit-session");

        var firstPage = await _events.GetByCardStableIdAsync(card.StableId, take: 2);
        var secondPage = await _events.GetByCardStableIdAsync(
            card.StableId,
            take: 2,
            beforeEventId: firstPage[^1].Id);

        Assert.Equal(2, firstPage.Count);
        Assert.NotEmpty(secondPage);
        Assert.True(firstPage[^1].Id > secondPage[0].Id);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _events.GetByCardStableIdAsync(card.StableId, take: CardEventRepository.MaxPageSize + 1));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class FixedAuditContextProvider(string sessionId, string machineName)
        : ICardAuditContextProvider
    {
        public CardAuditContext Current { get; } = new(sessionId, machineName);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
