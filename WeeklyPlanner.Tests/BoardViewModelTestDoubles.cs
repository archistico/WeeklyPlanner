using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using WeeklyPlanner.Core.Time;

namespace WeeklyPlanner.Tests;

internal static class BoardViewModelTestDoubles
{
    public static TestBoardContext Create(AppSettings? settings = null)
    {
        var initializer = new StubDatabaseInitializer();
        var cards = new StubCardRepository();
        var locks = new StubCardEditLockRepository();
        var columns = new StubColumnRepository();
        var detector = new StubBoardChangeDetector();
        var polling = new ManualRecurringTaskScheduler();
        var heartbeat = new ManualRecurringTaskScheduler();
        var session = new StubApplicationSession("test-session", "TEST-PC");
        var clock = new StubClock(new DateTimeOffset(2026, 7, 14, 18, 30, 0, TimeSpan.Zero));
        var effectiveSettings = settings ?? new AppSettings
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "weeklyplanner-test.db"),
            UserName = "Emilie",
            PollingIntervalSeconds = 7,
        };

        var viewModel = new BoardViewModel(
            effectiveSettings,
            initializer,
            cards,
            locks,
            columns,
            detector,
            polling,
            heartbeat,
            session,
            clock);

        return new TestBoardContext(
            viewModel,
            initializer,
            cards,
            locks,
            columns,
            detector,
            polling,
            heartbeat,
            session,
            clock);
    }

    internal sealed record TestBoardContext(
        BoardViewModel ViewModel,
        StubDatabaseInitializer Initializer,
        StubCardRepository Cards,
        StubCardEditLockRepository Locks,
        StubColumnRepository Columns,
        StubBoardChangeDetector ChangeDetector,
        ManualRecurringTaskScheduler PollingScheduler,
        ManualRecurringTaskScheduler HeartbeatScheduler,
        StubApplicationSession Session,
        StubClock Clock);

    internal sealed class StubDatabaseInitializer : IDatabaseInitializer
    {
        public int EnsureInitializedCallCount { get; private set; }

        public bool? LastAllowCreate { get; private set; }

        public void EnsureInitialized(bool allowCreate = true)
        {
            EnsureInitializedCallCount++;
            LastAllowCreate = allowCreate;
        }
    }

    internal sealed class StubCardRepository : ICardRepository
    {
        public List<Card> Items { get; } = [];

        public Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Card>>(Items.Select(Clone).ToList());

        public Task<Card> CreateAsync(Card card, CancellationToken cancellationToken = default)
        {
            var created = Clone(card);
            created.Id = Items.Count == 0 ? 1 : Items.Max(item => item.Id) + 1;
            Items.Add(created);
            return Task.FromResult(Clone(created));
        }

        public Task<Card> UpdateAsync(
            Card card,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            var index = Items.FindIndex(item => item.Id == card.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException();
            }

            Items[index] = Clone(card);
            return Task.FromResult(Clone(card));
        }

        public Task DeleteAsync(
            long cardId,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(card => card.Id == cardId);
            return Task.CompletedTask;
        }

        public Task MoveAsync(
            long cardId,
            long targetColumnId,
            int targetIndex,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            var card = Items.Single(item => item.Id == cardId);
            card.ColumnId = targetColumnId;
            card.SortOrder = targetIndex;
            return Task.CompletedTask;
        }

        private static Card Clone(Card card) => new()
        {
            Id = card.Id,
            ColumnId = card.ColumnId,
            Title = card.Title,
            Notes = card.Notes,
            SortOrder = card.SortOrder,
            CreatedBy = card.CreatedBy,
            UpdatedBy = card.UpdatedBy,
            UpdatedAtUtc = card.UpdatedAtUtc,
            Version = card.Version,
        };
    }

    internal sealed class StubCardEditLockRepository : ICardEditLockRepository
    {
        public List<CardEditLock> ActiveLocks { get; } = [];

        public string? ReleasedSessionId { get; private set; }

        public Task<CardEditLockAcquisitionResult> TryAcquireAsync(
            long cardId,
            string sessionId,
            string userName,
            string? machineName,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var editLock = new CardEditLock
            {
                CardId = cardId,
                SessionId = sessionId,
                UserName = userName,
                MachineName = machineName,
                AcquiredAtUtc = now.UtcDateTime.ToString("O"),
                LastHeartbeatUtc = now.UtcDateTime.ToString("O"),
                ExpiresAtUtc = now.Add(leaseDuration).UtcDateTime.ToString("O"),
            };
            ActiveLocks.RemoveAll(item => item.CardId == cardId);
            ActiveLocks.Add(editLock);
            return Task.FromResult(new CardEditLockAcquisitionResult(true, editLock));
        }

        public Task<bool> RenewAsync(
            long cardId,
            string sessionId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveLocks.Any(item =>
                item.CardId == cardId && item.SessionId == sessionId));

        public Task ReleaseAsync(
            long cardId,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            ActiveLocks.RemoveAll(item => item.CardId == cardId && item.SessionId == sessionId);
            return Task.CompletedTask;
        }

        public Task ReleaseSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            ReleasedSessionId = sessionId;
            ActiveLocks.RemoveAll(item => item.SessionId == sessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CardEditLock>> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CardEditLock>>(ActiveLocks.ToList());
    }

    internal sealed class StubColumnRepository : IColumnRepository
    {
        public IReadOnlyList<Column> Items { get; set; } =
        [
            new Column { Id = 0, Name = "Backlog", SortOrder = 0 },
        ];

        public Task<IReadOnlyList<Column>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items);
    }

    internal sealed class StubBoardChangeDetector : IBoardChangeDetector
    {
        public bool HasChanged { get; set; }

        public int CallCount { get; private set; }

        public Task<bool> HasChangedSinceLastCheckAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(HasChanged);
        }
    }

    internal sealed class ManualRecurringTaskScheduler : IRecurringTaskScheduler
    {
        private Func<CancellationToken, Task>? _callback;
        private CancellationToken _cancellationToken;

        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

        public bool IsRunning { get; private set; }

        public bool IsDisposed { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start(
            Func<CancellationToken, Task> callback,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _callback = callback;
            _cancellationToken = cancellationToken;
            IsRunning = true;
            StartCount++;
        }

        public void Stop()
        {
            IsRunning = false;
            StopCount++;
        }

        public Task TriggerAsync() => _callback is null
            ? Task.CompletedTask
            : _callback(_cancellationToken);

        public void Dispose()
        {
            IsRunning = false;
            IsDisposed = true;
            _callback = null;
        }
    }

    internal sealed record StubApplicationSession(
        string SessionId,
        string MachineName) : IApplicationSession;

    internal sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
