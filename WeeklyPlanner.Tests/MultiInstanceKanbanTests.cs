using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class MultiInstanceKanbanTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-two-instances-{Guid.NewGuid():N}");

    [Fact]
    public async Task Two_instances_share_locks_revisions_and_optimistic_concurrency()
    {
        var databasePath = Path.Combine(_tempDirectory, "shared.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        new DatabaseInitializer(connectionFactory).EnsureInitialized();

        var pipelineA = RetryPolicyFactory.CreateSqliteWritePipeline();
        var pipelineB = RetryPolicyFactory.CreateSqliteWritePipeline();
        var cardsA = new CardRepository(connectionFactory, pipelineA);
        var cardsB = new CardRepository(connectionFactory, pipelineB);
        var locksA = new CardEditLockRepository(connectionFactory, pipelineA);
        var locksB = new CardEditLockRepository(connectionFactory, pipelineB);
        var snapshotsB = new BoardSnapshotRepository(connectionFactory);
        var revisions = new BoardRevisionRepository(connectionFactory);

        var created = await cardsA.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Card condivisa",
            CreatedBy = "Istanza A",
            UpdatedBy = "Istanza A",
        });
        var staleCopyForB = Clone(created);
        var revisionAfterCreate = await revisions.GetCurrentRevisionAsync();

        var acquiredByA = await locksA.TryAcquireAsync(
            created.Id,
            "session-a",
            "Istanza A",
            "PC-A",
            TimeSpan.FromMinutes(1));
        Assert.True(acquiredByA.Acquired);

        var deniedToB = await locksB.TryAcquireAsync(
            created.Id,
            "session-b",
            "Istanza B",
            "PC-B",
            TimeSpan.FromMinutes(1));
        Assert.False(deniedToB.Acquired);
        Assert.Equal("Istanza A", deniedToB.CurrentLock.UserName);

        created.Title = "Aggiornata da A";
        var updatedByA = await cardsA.UpdateAsync(created, "session-a");
        Assert.Equal(2, updatedByA.Version);
        Assert.True(await revisions.GetCurrentRevisionAsync() > revisionAfterCreate);

        await locksA.ReleaseAsync(created.Id, "session-a");
        var acquiredByB = await locksB.TryAcquireAsync(
            created.Id,
            "session-b",
            "Istanza B",
            "PC-B",
            TimeSpan.FromMinutes(1));
        Assert.True(acquiredByB.Acquired);

        staleCopyForB.Title = "Aggiornata da B su copia vecchia";
        var conflict = await Assert.ThrowsAsync<CardConcurrencyException>(() =>
            cardsB.UpdateAsync(staleCopyForB, "session-b"));
        Assert.Equal(created.Id, conflict.CardId);
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);

        var snapshot = await snapshotsB.GetAsync();
        var persisted = Assert.Single(snapshot.Cards, card => card.Id == created.Id);
        Assert.Equal("Aggiornata da A", persisted.Title);
        Assert.Equal(2, persisted.Version);
        Assert.Single(snapshot.ActiveLocks, editLock =>
            editLock.CardId == created.Id && editLock.SessionId == "session-b");
    }

    private static Card Clone(Card card) => new()
    {
        Id = card.Id,
        ColumnId = card.ColumnId,
        StableId = card.StableId,
        CreatedAtUtc = card.CreatedAtUtc,
        CreatedAtIsEstimated = card.CreatedAtIsEstimated,
        PriorityId = card.PriorityId,
        CardTypeId = card.CardTypeId,
        PriorityAssignedAtUtc = card.PriorityAssignedAtUtc,
        DueAtUtc = card.DueAtUtc,
        Title = card.Title,
        Notes = card.Notes,
        SortOrder = card.SortOrder,
        CreatedBy = card.CreatedBy,
        UpdatedBy = card.UpdatedBy,
        UpdatedAtUtc = card.UpdatedAtUtc,
        Version = card.Version,
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
