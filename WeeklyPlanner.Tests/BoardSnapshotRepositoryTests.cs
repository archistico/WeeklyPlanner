using Dapper;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardSnapshotRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-snapshot-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;

    public BoardSnapshotRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(_databasePath);
        new DatabaseInitializer(_factory).EnsureInitialized();
    }

    [Fact]
    public async Task GetAsync_reads_the_complete_kanban_state_in_one_snapshot()
    {
        var writePipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var cards = new CardRepository(_factory, writePipeline);
        var locks = new CardEditLockRepository(_factory, writePipeline);
        var created = await cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Snapshot",
            CreatedBy = "Emilie",
            UpdatedBy = "Emilie",
        });
        await locks.TryAcquireAsync(
            created.Id,
            "session",
            "Emilie",
            "PC",
            TimeSpan.FromMinutes(1));

        var repository = new BoardSnapshotRepository(_factory);
        var snapshot = await repository.GetAsync();

        Assert.True(snapshot.Revision > 0);
        Assert.Equal(5, snapshot.Columns.Count);
        Assert.Equal(WorkflowColumnKeys.Ordered, snapshot.Columns.Select(item => item.SystemKey!));
        Assert.Single(snapshot.Cards, item => item.Id == created.Id);
        Assert.Equal(4, snapshot.Priorities.Count);
        Assert.Equal(6, snapshot.CardTypes.Count);
        Assert.Single(snapshot.CardTypes, item => item.SystemKey == SystemCardTypeKeys.Generic);
        Assert.Single(snapshot.DeadlineRules);
        Assert.Single(snapshot.ActiveLocks, item => item.CardId == created.Id);
    }

    [Fact]
    public async Task GetAsync_excludes_expired_locks_without_mutating_the_database()
    {
        using (var connection = _factory.Create())
        {
            var genericId = connection.ExecuteScalar<long>(
                "SELECT Id FROM CardTypes WHERE SystemKey = 'generic';");
            connection.Execute(
                """
                INSERT INTO Cards
                    (ColumnId, StableId, CreatedAtUtc, CreatedAtIsEstimated, CardTypeId,
                     Title, SortOrder, CreatedBy, UpdatedBy, UpdatedAtUtc, Version)
                VALUES
                    (0, 'expired-lock-card', @Now, 0, @GenericId,
                     'Scaduta', 0, 'Test', 'Test', @Now, 1);
                """,
                new { Now = "2026-01-01T00:00:00.0000000Z", GenericId = genericId });
            var cardId = connection.ExecuteScalar<long>("SELECT last_insert_rowid();");
            connection.Execute(
                """
                INSERT INTO CardEditLocks
                    (CardId, SessionId, UserName, MachineName,
                     AcquiredAtUtc, LastHeartbeatUtc, ExpiresAtUtc)
                VALUES
                    (@CardId, 'old', 'Test', 'PC', @Old, @Old, @Old);
                """,
                new { CardId = cardId, Old = "2020-01-01T00:00:00.0000000Z" });
        }

        var snapshot = await new BoardSnapshotRepository(_factory).GetAsync();

        Assert.Empty(snapshot.ActiveLocks);
        using var verify = _factory.Create();
        Assert.Equal(1, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM CardEditLocks;"));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
