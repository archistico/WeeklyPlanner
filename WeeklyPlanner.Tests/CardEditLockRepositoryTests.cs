using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardEditLockRepositoryTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(
        Path.GetTempPath(),
        $"weeklyplanner-lock-tests-{Guid.NewGuid():N}.db");
    private readonly ManualTimeProvider _timeProvider = new(
        new DateTimeOffset(2026, 7, 14, 18, 0, 0, TimeSpan.Zero));
    private readonly ICardRepository _cardRepository;
    private readonly ICardEditLockRepository _lockRepository;
    private readonly IBoardRevisionRepository _revisionRepository;

    public CardEditLockRepositoryTests()
    {
        var connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        new DatabaseInitializer(connectionFactory).EnsureInitialized();

        var pipeline = RetryPolicyFactory.CreateSqliteWritePipeline();
        _cardRepository = new CardRepository(connectionFactory, pipeline, _timeProvider);
        _lockRepository = new CardEditLockRepository(connectionFactory, pipeline, _timeProvider);
        _revisionRepository = new BoardRevisionRepository(connectionFactory);
    }

    [Fact]
    public async Task TryAcquireAsync_assigns_free_card_to_requesting_session()
    {
        var card = await CreateCardAsync();

        var result = await _lockRepository.TryAcquireAsync(
            card.Id,
            "session-a",
            "Emilie",
            "PC-A",
            TimeSpan.FromSeconds(30));

        Assert.True(result.Acquired);
        Assert.Equal("session-a", result.CurrentLock.SessionId);
        Assert.Equal("Emilie", result.CurrentLock.UserName);
        Assert.Equal("PC-A", result.CurrentLock.MachineName);
        Assert.Single(await _lockRepository.GetActiveAsync());
    }

    [Fact]
    public async Task TryAcquireAsync_reports_existing_owner_to_second_session()
    {
        var card = await CreateCardAsync();
        await AcquireAsync(card.Id, "session-a", "Emilie");

        var result = await _lockRepository.TryAcquireAsync(
            card.Id,
            "session-b",
            "Alice",
            "PC-B",
            TimeSpan.FromSeconds(30));

        Assert.False(result.Acquired);
        Assert.Equal("session-a", result.CurrentLock.SessionId);
        Assert.Equal("Emilie", result.CurrentLock.UserName);
    }

    [Fact]
    public async Task Expired_lock_can_be_acquired_by_another_session()
    {
        var card = await CreateCardAsync();
        await AcquireAsync(card.Id, "session-a", "Emilie", TimeSpan.FromSeconds(30));
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        var result = await _lockRepository.TryAcquireAsync(
            card.Id,
            "session-b",
            "Alice",
            "PC-B",
            TimeSpan.FromSeconds(30));

        Assert.True(result.Acquired);
        Assert.Equal("session-b", result.CurrentLock.SessionId);
        Assert.Equal("Alice", result.CurrentLock.UserName);
    }

    [Fact]
    public async Task RenewAsync_extends_lease_only_for_owner_session()
    {
        var card = await CreateCardAsync();
        var first = await AcquireAsync(card.Id, "session-a", "Emilie", TimeSpan.FromSeconds(30));
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.False(await _lockRepository.RenewAsync(
            card.Id,
            "session-b",
            TimeSpan.FromSeconds(30)));
        Assert.True(await _lockRepository.RenewAsync(
            card.Id,
            "session-a",
            TimeSpan.FromSeconds(30)));

        var renewed = Assert.Single(await _lockRepository.GetActiveAsync());
        Assert.True(string.CompareOrdinal(renewed.ExpiresAtUtc, first.ExpiresAtUtc) > 0);
    }

    [Fact]
    public async Task RenewAsync_does_not_revive_an_expired_lease()
    {
        var card = await CreateCardAsync();
        await AcquireAsync(card.Id, "session-a", "Emilie", TimeSpan.FromSeconds(30));
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        Assert.False(await _lockRepository.RenewAsync(
            card.Id,
            "session-a",
            TimeSpan.FromSeconds(30)));
        Assert.Empty(await _lockRepository.GetActiveAsync());
    }

    [Fact]
    public async Task ReleaseAsync_removes_only_lock_owned_by_session()
    {
        var card = await CreateCardAsync();
        await AcquireAsync(card.Id, "session-a", "Emilie");

        await _lockRepository.ReleaseAsync(card.Id, "session-b");
        Assert.Single(await _lockRepository.GetActiveAsync());

        await _lockRepository.ReleaseAsync(card.Id, "session-a");
        Assert.Empty(await _lockRepository.GetActiveAsync());
    }

    [Fact]
    public async Task Acquire_and_release_advance_revision_but_heartbeat_does_not()
    {
        var card = await CreateCardAsync();
        var revision = await _revisionRepository.GetCurrentRevisionAsync();

        await AcquireAsync(card.Id, "session-a", "Emilie");
        revision = await AssertRevisionAdvancedAsync(revision);

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await _lockRepository.RenewAsync(
            card.Id,
            "session-a",
            TimeSpan.FromSeconds(30)));
        Assert.Equal(revision, await _revisionRepository.GetCurrentRevisionAsync());

        await _lockRepository.ReleaseAsync(card.Id, "session-a");
        await AssertRevisionAdvancedAsync(revision);
    }

    private async Task<Card> CreateCardAsync()
    {
        return await _cardRepository.CreateAsync(new Card
        {
            ColumnId = 0,
            Title = "Card",
            CreatedBy = "Test",
            UpdatedBy = "Test",
        });
    }

    private async Task<CardEditLock> AcquireAsync(
        long cardId,
        string sessionId,
        string userName,
        TimeSpan? leaseDuration = null)
    {
        var result = await _lockRepository.TryAcquireAsync(
            cardId,
            sessionId,
            userName,
            "TestMachine",
            leaseDuration ?? TimeSpan.FromSeconds(30));

        Assert.True(result.Acquired);
        return result.CurrentLock;
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
