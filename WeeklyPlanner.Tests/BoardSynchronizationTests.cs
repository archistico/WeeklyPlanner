using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

/// <summary>
/// Simula due istanze dell'app che aprono lo stesso database locale.
/// Non rappresenta sincronizzazione fra computer o accesso tramite share di rete.
/// </summary>
public sealed class BoardSynchronizationTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-sync-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Detector_observes_creation_from_another_repository_instance()
    {
        var (detector, writer) = CreateClients();
        await detector.HasChangedSinceLastCheckAsync();
        Assert.False(await detector.HasChangedSinceLastCheckAsync());

        await writer.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Creata dalla seconda istanza",
            SortOrder = 0,
            CreatedBy = "Client B",
            UpdatedBy = "Client B",
        });

        Assert.True(await detector.HasChangedSinceLastCheckAsync());
        Assert.False(await detector.HasChangedSinceLastCheckAsync());
    }

    [Fact]
    public async Task Detector_observes_deletion_from_another_repository_instance()
    {
        var (detector, writer) = CreateClients();
        var card = await writer.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Da eliminare",
            SortOrder = 0,
            CreatedBy = "Client B",
            UpdatedBy = "Client B",
        });

        await detector.HasChangedSinceLastCheckAsync();
        Assert.False(await detector.HasChangedSinceLastCheckAsync());

        await writer.DeleteAsync(card.Id, "Client B");

        Assert.True(await detector.HasChangedSinceLastCheckAsync());
    }


    [Fact]
    public async Task Detector_observes_edit_lock_changes_from_another_instance()
    {
        var connectionFactoryA = new SqliteConnectionFactory(_tempDbPath);
        var connectionFactoryB = new SqliteConnectionFactory(_tempDbPath);
        new DatabaseInitializer(connectionFactoryA).EnsureInitialized();

        var detector = new BoardChangeDetector(new BoardRevisionRepository(connectionFactoryA));
        var pipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        var cards = new CardRepository(connectionFactoryB, pipeline);
        var locks = new CardEditLockRepository(connectionFactoryB, pipeline);
        var card = await cards.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Da bloccare",
            CreatedBy = "Client B",
            UpdatedBy = "Client B",
        });

        await detector.HasChangedSinceLastCheckAsync();
        Assert.False(await detector.HasChangedSinceLastCheckAsync());

        await locks.TryAcquireAsync(
            card.Id,
            "session-b",
            "Client B",
            "PC-B",
            TimeSpan.FromSeconds(30));

        Assert.True(await detector.HasChangedSinceLastCheckAsync());
        Assert.False(await detector.HasChangedSinceLastCheckAsync());

        await locks.ReleaseAsync(card.Id, "session-b");
        Assert.True(await detector.HasChangedSinceLastCheckAsync());
    }

    private (IBoardChangeDetector Detector, ICardRepository Writer) CreateClients()
    {
        var connectionFactoryA = new SqliteConnectionFactory(_tempDbPath);
        var connectionFactoryB = new SqliteConnectionFactory(_tempDbPath);
        new DatabaseInitializer(connectionFactoryA).EnsureInitialized();

        var detector = new BoardChangeDetector(new BoardRevisionRepository(connectionFactoryA));
        var writer = new CardRepository(
            connectionFactoryB,
            RetryPolicyFactory.CreateSqliteWritePipeline());

        return (detector, writer);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }
    }
}
