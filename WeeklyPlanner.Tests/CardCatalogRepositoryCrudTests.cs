using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Auditing;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardCatalogRepositoryCrudTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-catalog-crud-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly CardCatalogRepository _repository;
    private readonly CardRepository _cards;
    private readonly CardEditLockRepository _locks;
    private readonly CardEventRepository _events;

    public CardCatalogRepositoryCrudTests()
    {
        _connectionFactory = new SqliteConnectionFactory(_databasePath);
        new DatabaseInitializer(_connectionFactory).EnsureInitialized();
        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        var auditContext = new FixedAuditContextProvider("catalog-session", "TEST-PC");
        _repository = new CardCatalogRepository(
            _connectionFactory,
            writePipeline: writePipeline,
            timeProvider: timeProvider,
            auditContextProvider: auditContext);
        _cards = new CardRepository(
            _connectionFactory,
            writePipeline,
            timeProvider,
            auditContextProvider: auditContext);
        _locks = new CardEditLockRepository(
            _connectionFactory,
            writePipeline,
            timeProvider);
        _events = new CardEventRepository(_connectionFactory);
    }

    [Fact]
    public async Task Create_priority_normalizes_values_sets_default_and_saves_overrides()
    {
        var initial = await _repository.GetSnapshotAsync();
        var sqlType = Assert.Single(initial.CardTypes, item => item.Name == "SQL");

        var saved = await _repository.SavePriorityAsync(new PrioritySaveRequest(
            null,
            0,
            " x ",
            "  Straordinaria  ",
            "  Gestione speciale.  ",
            48,
            true,
            true,
            [new PriorityDeadlineOverrideInput(sqlType.Id, 24)]));

        var snapshot = await _repository.GetSnapshotAsync();
        Assert.Equal("X", saved.Code);
        Assert.Equal("Straordinaria", saved.Name);
        Assert.Equal("Gestione speciale.", saved.Description);
        Assert.True(saved.IsDefault);
        Assert.Single(snapshot.Priorities, item => item.IsDefault && item.Id == saved.Id);
        var deadline = Assert.Single(
            snapshot.DeadlineRules,
            item => item.PriorityId == saved.Id && item.CardTypeId == sqlType.Id);
        Assert.Equal(24, deadline.DueHours);
    }

    [Fact]
    public async Task Update_priority_replaces_rules_and_rejects_stale_version()
    {
        var initial = await _repository.GetSnapshotAsync();
        var sqlType = Assert.Single(initial.CardTypes, item => item.Name == "SQL");
        var reportType = Assert.Single(initial.CardTypes, item => item.Name == "Report");
        var priority = Assert.Single(initial.Priorities, item => item.Code == "D");

        var updated = await _repository.SavePriorityAsync(new PrioritySaveRequest(
            priority.Id,
            priority.Version,
            priority.Code,
            "Differibile aggiornata",
            priority.Description,
            600,
            true,
            false,
            [
                new PriorityDeadlineOverrideInput(sqlType.Id, 800),
                new PriorityDeadlineOverrideInput(reportType.Id, 900),
            ]));

        var snapshot = await _repository.GetSnapshotAsync();
        Assert.Equal(priority.Version + 1, updated.Version);
        Assert.Equal(2, snapshot.DeadlineRules.Count(item => item.PriorityId == priority.Id));

        await Assert.ThrowsAsync<CardCatalogConcurrencyException>(() =>
            _repository.SavePriorityAsync(new PrioritySaveRequest(
                priority.Id,
                priority.Version,
                priority.Code,
                priority.Name,
                priority.Description,
                priority.DefaultDueHours,
                true,
                false,
                [])));
    }

    [Fact]
    public async Task Priority_assigned_to_a_card_cannot_be_deleted_but_can_be_deactivated()
    {
        var initial = await _repository.GetSnapshotAsync();
        var priority = Assert.Single(initial.Priorities, item => item.Code == "U");
        await _cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Usa la priorità",
            PriorityId = priority.Id,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });

        await Assert.ThrowsAsync<CardCatalogItemInUseException>(() =>
            _repository.DeletePriorityAsync(priority.Id, priority.Version));

        var deactivated = await _repository.SavePriorityAsync(new PrioritySaveRequest(
            priority.Id,
            priority.Version,
            priority.Code,
            priority.Name,
            priority.Description,
            priority.DefaultDueHours,
            false,
            false,
            []));
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task Delete_unused_priority_compacts_order()
    {
        var snapshot = await _repository.GetSnapshotAsync();
        var breve = Assert.Single(snapshot.Priorities, item => item.Code == "B");

        await _repository.DeletePriorityAsync(breve.Id, breve.Version);

        var remaining = (await _repository.GetSnapshotAsync()).Priorities;
        Assert.Equal(Enumerable.Range(0, remaining.Count), remaining.Select(item => item.SortOrder));
        Assert.DoesNotContain(remaining, item => item.Id == breve.Id);
    }

    [Fact]
    public async Task Reorder_priorities_is_atomic_and_checks_every_version()
    {
        var initial = await _repository.GetSnapshotAsync();
        var reversed = initial.Priorities
            .Reverse()
            .Select(item => new CatalogOrderItem(item.Id, item.Version))
            .ToArray();

        await _repository.ReorderPrioritiesAsync(reversed);
        var reordered = await _repository.GetSnapshotAsync();
        Assert.Equal(initial.Priorities.Select(item => item.Id).Reverse(), reordered.Priorities.Select(item => item.Id));

        await Assert.ThrowsAsync<CardCatalogConcurrencyException>(() =>
            _repository.ReorderPrioritiesAsync(reversed));
    }

    [Fact]
    public async Task Lane_crud_normalizes_color_enforces_uniqueness_and_keeps_generic_fixed()
    {
        var created = await _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
            null,
            0,
            "  Assistenza  ",
            "#a1b2c3",
            true));

        Assert.Equal("Assistenza", created.Name);
        Assert.Equal("#A1B2C3", created.ColorHex);
        Assert.False(created.IsDefault);
        Assert.Equal(0, created.CardCount);

        var updated = await _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
            created.Id,
            created.Version,
            "Assistenza clienti",
            "#112233",
            true));
        Assert.Equal(created.Version + 1, updated.Version);

        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
                null,
                0,
                "sql",
                "#123456",
                true)));

        var generic = Assert.Single(
            (await _repository.GetSnapshotAsync()).CardTypes,
            item => item.SystemKey == SystemCardTypeKeys.Generic);
        Assert.True(generic.IsDefault);
        Assert.True(generic.IsActive);
        Assert.Equal(0, generic.SortOrder);
    }

    [Fact]
    public async Task Deleting_used_lane_transfers_cards_preserves_workflow_order_and_records_history()
    {
        var snapshot = await _repository.GetSnapshotAsync();
        var source = Assert.Single(snapshot.CardTypes, item => item.Name == "SQL");
        var destination = Assert.Single(snapshot.CardTypes, item => item.Name == "Esame strumentale");
        var priority = Assert.Single(snapshot.Priorities, item => item.Code == "D");

        await _cards.CreateAsync(new Card
        {
            ColumnId = 3,
            Title = "Card precedente",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 3,
            Title = "Card da trasferire",
            PriorityId = priority.Id,
            CardTypeId = source.Id,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        var previousVersion = card.Version;
        var previousAssignedAt = card.PriorityAssignedAtUtc;

        await _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
            source.Id,
            source.Version,
            destination.Id,
            destination.Version,
            "  Emilie  "));

        var transferred = Assert.Single(
            await _cards.GetAllAsync(),
            item => item.Id == card.Id);
        Assert.Equal<long?>(destination.Id, transferred.CardTypeId);
        Assert.Equal(3L, transferred.ColumnId);
        Assert.Equal(1, transferred.SortOrder);
        Assert.Equal(previousVersion + 1, transferred.Version);
        Assert.Equal(previousAssignedAt, transferred.PriorityAssignedAtUtc);
        Assert.Equal("Emilie", transferred.UpdatedBy);
        var assignedAt = DateTimeOffset.Parse(transferred.PriorityAssignedAtUtc!);
        var dueAt = DateTimeOffset.Parse(transferred.DueAtUtc!);
        Assert.Equal(TimeSpan.FromDays(60), dueAt - assignedAt);

        var current = await _repository.GetSnapshotAsync();
        Assert.DoesNotContain(current.CardTypes, item => item.Id == source.Id);
        Assert.Equal(1, Assert.Single(current.CardTypes, item => item.Id == destination.Id).CardCount);

        var history = await _events.GetByCardStableIdAsync(card.StableId);
        var typeChanged = Assert.Single(history, item => item.EventType == CardEventTypes.TypeChanged);
        Assert.Equal("Emilie", typeChanged.UserName);
        Assert.Equal("catalog-session", typeChanged.SessionId);
        Assert.Equal("TEST-PC", typeChanged.MachineName);
        Assert.Contains("CardTypeDeleted", typeChanged.DataJson, StringComparison.Ordinal);
        Assert.Contains($"\"previousCardTypeId\":{source.Id}", typeChanged.DataJson, StringComparison.Ordinal);
        Assert.Contains($"\"cardTypeId\":{destination.Id}", typeChanged.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Used_lane_requires_an_active_destination_and_rolls_back_on_validation_error()
    {
        var snapshot = await _repository.GetSnapshotAsync();
        var source = Assert.Single(snapshot.CardTypes, item => item.Name == "Report");
        var inactiveDestination = await _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
            null,
            0,
            "Archivio inattivo",
            "#334455",
            false));
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 1,
            Title = "Resta nella fascia",
            CardTypeId = source.Id,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });

        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                source.Id,
                source.Version,
                null,
                null,
                "Emilie")));
        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                source.Id,
                source.Version,
                inactiveDestination.Id,
                inactiveDestination.Version,
                "Emilie")));

        Assert.Contains(
            (await _repository.GetSnapshotAsync()).CardTypes,
            item => item.Id == source.Id);
        Assert.Equal<long?>(
            source.Id,
            Assert.Single(await _cards.GetAllAsync(), item => item.Id == card.Id).CardTypeId);
        Assert.DoesNotContain(
            await _events.GetByCardStableIdAsync(card.StableId),
            item => item.EventType == CardEventTypes.TypeChanged);
    }

    [Fact]
    public async Task Active_card_edit_lock_blocks_lane_deletion()
    {
        var snapshot = await _repository.GetSnapshotAsync();
        var source = Assert.Single(snapshot.CardTypes, item => item.Name == "Visita");
        var generic = Assert.Single(snapshot.CardTypes, item => item.IsSystem);
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 2,
            Title = "Card in modifica",
            CardTypeId = source.Id,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        var acquired = await _locks.TryAcquireAsync(
            card.Id,
            "editing-session",
            "Emilie",
            "TEST-PC",
            TimeSpan.FromMinutes(5));
        Assert.True(acquired.Acquired);

        var exception = await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                source.Id,
                source.Version,
                generic.Id,
                generic.Version,
                "Emilie")));

        Assert.Contains("attualmente in modifica", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            (await _repository.GetSnapshotAsync()).CardTypes,
            item => item.Id == source.Id);
        Assert.Equal<long?>(
            source.Id,
            Assert.Single(await _cards.GetAllAsync(), item => item.Id == card.Id).CardTypeId);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_lane_transfer_and_delete()
    {
        var snapshot = await _repository.GetSnapshotAsync();
        var source = Assert.Single(snapshot.CardTypes, item => item.Name == "Report");
        var generic = Assert.Single(snapshot.CardTypes, item => item.IsSystem);
        var card = await _cards.CreateAsync(new Card
        {
            ColumnId = 1,
            Title = "Trasferimento atomico",
            CardTypeId = source.Id,
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });

        using (var connection = _connectionFactory.Create())
        {
            connection.Execute(
                """
                CREATE TRIGGER TR_Test_AbortLaneTypeChanged
                BEFORE INSERT ON CardEvents
                WHEN NEW.EventType = 'TypeChanged'
                BEGIN
                    SELECT RAISE(ABORT, 'forced lane audit failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<SqliteException>(() =>
            _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                source.Id,
                source.Version,
                generic.Id,
                generic.Version,
                "Emilie")));

        Assert.Contains(
            (await _repository.GetSnapshotAsync()).CardTypes,
            item => item.Id == source.Id);
        Assert.Equal<long?>(
            source.Id,
            Assert.Single(await _cards.GetAllAsync(), item => item.Id == card.Id).CardTypeId);
        Assert.DoesNotContain(
            await _events.GetByCardStableIdAsync(card.StableId),
            item => item.EventType == CardEventTypes.TypeChanged);
    }

    [Fact]
    public async Task Reorder_and_delete_empty_lanes_keep_generic_first_and_user_order_compact()
    {
        var initial = await _repository.GetSnapshotAsync();
        var generic = Assert.Single(initial.CardTypes, item => item.IsSystem);
        var reversedUsers = initial.CardTypes
            .Where(item => !item.IsSystem)
            .Reverse()
            .Select(item => new CatalogOrderItem(item.Id, item.Version))
            .ToArray();

        await _repository.ReorderCardTypesAsync(reversedUsers);

        var reordered = await _repository.GetSnapshotAsync();
        Assert.Equal(generic.Id, reordered.CardTypes[0].Id);
        Assert.Equal(
            reversedUsers.Select(item => item.Id),
            reordered.CardTypes.Where(item => !item.IsSystem).Select(item => item.Id));

        var removable = reordered.CardTypes.First(item => !item.IsSystem && item.CardCount == 0);
        await _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
            removable.Id,
            removable.Version,
            null,
            null,
            "Emilie"));

        var remaining = (await _repository.GetSnapshotAsync()).CardTypes;
        Assert.Equal(generic.Id, remaining[0].Id);
        Assert.Equal(
            Enumerable.Range(1, remaining.Count - 1),
            remaining.Where(item => !item.IsSystem).Select(item => item.SortOrder));
    }

    [Fact]
    public async Task Generic_lane_can_change_only_color_and_never_participates_in_delete_or_reorder()
    {
        var initial = await _repository.GetSnapshotAsync();
        var generic = Assert.Single(initial.CardTypes, item => item.IsSystem);

        var recolored = await _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
            generic.Id,
            generic.Version,
            generic.Name,
            "#123456",
            true));
        Assert.Equal("#123456", recolored.ColorHex);

        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
                generic.Id,
                recolored.Version,
                "Altra",
                recolored.ColorHex,
                true)));
        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.DeleteCardTypeAsync(new CardTypeDeleteRequest(
                generic.Id,
                recolored.Version,
                null,
                null,
                "Emilie")));
        var orderIncludingGeneric = (await _repository.GetSnapshotAsync()).CardTypes
            .Select(item => new CatalogOrderItem(item.Id, item.Version))
            .ToArray();

        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.ReorderCardTypesAsync(orderIncludingGeneric));
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public async Task Invalid_lane_colors_are_rejected(string color)
    {
        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.SaveCardTypeAsync(new CardTypeSaveRequest(
                null,
                0,
                "Nuova",
                color,
                true)));
    }

    [Fact]
    public async Task A_new_default_priority_replaces_the_previous_default_atomically()
    {
        var first = await _repository.SavePriorityAsync(new PrioritySaveRequest(
            null, 0, "X", "Prima", null, 24, true, true, []));
        var second = await _repository.SavePriorityAsync(new PrioritySaveRequest(
            null, 0, "Y", "Seconda", null, 48, true, true, []));

        var snapshot = await _repository.GetSnapshotAsync();
        Assert.Single(snapshot.Priorities, item => item.IsDefault && item.Id == second.Id);
        Assert.False(Assert.Single(snapshot.Priorities, item => item.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task An_inactive_priority_cannot_be_default()
    {
        await Assert.ThrowsAsync<CardCatalogValidationException>(() =>
            _repository.SavePriorityAsync(new PrioritySaveRequest(
                null, 0, "X", "Non attiva", null, 24, false, true, [])));
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
