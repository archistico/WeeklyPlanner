using WeeklyPlanner.App.Diagnostics;
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
    public static TestBoardContext Create(
        AppSettings? settings = null,
        IAppLogger? logger = null,
        IErrorReferenceGenerator? errorReferences = null)
    {
        var initializer = new StubDatabaseInitializer();
        var cards = new StubCardRepository();
        var events = new StubCardEventRepository();
        var locks = new StubCardEditLockRepository();
        var columns = new StubColumnRepository();
        var snapshot = new StubBoardSnapshotRepository(columns, cards, locks);
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
            events,
            locks,
            snapshot,
            detector,
            polling,
            heartbeat,
            session,
            clock,
            logger,
            errorReferences);

        return new TestBoardContext(
            viewModel,
            initializer,
            cards,
            events,
            locks,
            columns,
            snapshot,
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
        StubCardEventRepository Events,
        StubCardEditLockRepository Locks,
        StubColumnRepository Columns,
        StubBoardSnapshotRepository Snapshot,
        StubBoardChangeDetector ChangeDetector,
        ManualRecurringTaskScheduler PollingScheduler,
        ManualRecurringTaskScheduler HeartbeatScheduler,
        StubApplicationSession Session,
        StubClock Clock);

    internal sealed class StubDatabaseInitializer : IDatabaseInitializer
    {
        private readonly Queue<Exception> _failures = new();

        public int EnsureInitializedCallCount { get; private set; }

        public bool? LastAllowCreate { get; private set; }

        public void EnqueueFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _failures.Enqueue(exception);
        }

        public void EnsureInitialized(bool allowCreate = true)
        {
            EnsureInitializedCallCount++;
            LastAllowCreate = allowCreate;

            if (_failures.Count > 0)
            {
                throw _failures.Dequeue();
            }
        }
    }

    internal sealed class StubCardRepository : ICardRepository
    {
        public List<Card> Items { get; } = [];

        public Func<CancellationToken, Task<IReadOnlyList<Card>>>? GetAllHandler { get; set; }

        public int GetAllCallCount { get; private set; }

        public Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCallCount++;
            return GetAllHandler is null
                ? Task.FromResult<IReadOnlyList<Card>>(Items.Select(Clone).ToList())
                : GetAllHandler(cancellationToken);
        }

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

        public Task MoveToCellAsync(
            long cardId,
            long targetColumnId,
            long targetCardTypeId,
            int targetCellIndex,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            var card = Items.Single(item => item.Id == cardId);
            var sourceColumnId = card.ColumnId;
            var sourceCardTypeId = card.CardTypeId ?? 1;
            var sameCell = sourceColumnId == targetColumnId &&
                           sourceCardTypeId == targetCardTypeId;
            var sourceCell = Items
                .Where(item => item.ColumnId == sourceColumnId &&
                               (item.CardTypeId ?? 1) == sourceCardTypeId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .ToList();
            var sourceCellIndex = sourceCell.IndexOf(card);
            var finalCellIndex = targetCellIndex;
            if (sameCell)
            {
                finalCellIndex = sourceCellIndex < targetCellIndex
                    ? targetCellIndex - 1
                    : targetCellIndex;
                finalCellIndex = Math.Clamp(finalCellIndex, 0, sourceCell.Count - 1);
                if (finalCellIndex == sourceCellIndex)
                {
                    return Task.CompletedTask;
                }
            }

            var sourceColumn = Items
                .Where(item => item.ColumnId == sourceColumnId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .ToList();
            sourceColumn.Remove(card);
            var targetColumn = sourceColumnId == targetColumnId
                ? sourceColumn
                : Items
                    .Where(item => item.ColumnId == targetColumnId)
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .ToList();
            var targetCell = targetColumn
                .Where(item => (item.CardTypeId ?? 1) == targetCardTypeId)
                .ToList();
            if (!sameCell)
            {
                finalCellIndex = Math.Clamp(targetCellIndex, 0, targetCell.Count);
            }

            var globalTargetIndex = targetCell.Count == 0
                ? targetColumn.Count
                : finalCellIndex < targetCell.Count
                    ? targetColumn.IndexOf(targetCell[finalCellIndex])
                    : targetColumn.IndexOf(targetCell[^1]) + 1;
            card.ColumnId = targetColumnId;
            card.CardTypeId = targetCardTypeId;
            targetColumn.Insert(globalTargetIndex, card);

            for (var index = 0; index < sourceColumn.Count; index++)
            {
                sourceColumn[index].SortOrder = index;
            }

            for (var index = 0; index < targetColumn.Count; index++)
            {
                targetColumn[index].SortOrder = index;
            }

            return Task.CompletedTask;
        }

        internal static Card Clone(Card card) => new()
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
    }

    internal sealed class StubCardEventRepository : ICardEventRepository
    {
        public List<CardEvent> Items { get; } = [];

        public List<(string StableId, int Take, long? BeforeEventId)> Requests { get; } = [];

        public Task<IReadOnlyList<CardEvent>> GetByCardStableIdAsync(
            string cardStableId,
            int take = 50,
            long? beforeEventId = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((cardStableId, take, beforeEventId));
            var page = Items
                .Where(item => string.Equals(
                    item.CardStableId,
                    cardStableId,
                    StringComparison.Ordinal))
                .Where(item => beforeEventId is null || item.Id < beforeEventId.Value)
                .OrderByDescending(item => item.Id)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<CardEvent>>(page);
        }
    }

    internal sealed class StubCardEditLockRepository : ICardEditLockRepository
    {
        public List<CardEditLock> ActiveLocks { get; } = [];

        public Func<long, string, TimeSpan, CancellationToken, Task<bool>>? RenewHandler { get; set; }

        public string? ReleasedSessionId { get; private set; }

        public int RenewCallCount { get; private set; }

        public int ReleaseSessionCallCount { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            RenewCallCount++;
            return RenewHandler is null
                ? Task.FromResult(ActiveLocks.Any(item =>
                    item.CardId == cardId && item.SessionId == sessionId))
                : RenewHandler(cardId, sessionId, leaseDuration, cancellationToken);
        }

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
            ReleaseSessionCallCount++;
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
            new Column
            {
                Id = 0,
                Name = "BACKLOG",
                SortOrder = 0,
                SystemKey = WorkflowColumnKeys.Backlog,
                IsSystem = true,
            },
            new Column
            {
                Id = 1,
                Name = "TODO",
                SortOrder = 1,
                SystemKey = WorkflowColumnKeys.Todo,
                IsSystem = true,
            },
            new Column
            {
                Id = 2,
                Name = "IN PROGRESS",
                SortOrder = 2,
                SystemKey = WorkflowColumnKeys.InProgress,
                IsSystem = true,
            },
            new Column
            {
                Id = 3,
                Name = "TESTING",
                SortOrder = 3,
                SystemKey = WorkflowColumnKeys.Testing,
                IsSystem = true,
            },
            new Column
            {
                Id = 4,
                Name = "DONE",
                SortOrder = 4,
                SystemKey = WorkflowColumnKeys.Done,
                IsSystem = true,
            },
        ];

        public Func<CancellationToken, Task<IReadOnlyList<Column>>>? GetAllHandler { get; set; }

        public int GetAllCallCount { get; private set; }

        public Task<IReadOnlyList<Column>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            GetAllCallCount++;
            return GetAllHandler is null
                ? Task.FromResult(Items)
                : GetAllHandler(cancellationToken);
        }
    }

    internal sealed class StubBoardSnapshotRepository : IBoardSnapshotRepository
    {
        private readonly StubColumnRepository _columns;
        private readonly StubCardRepository _cards;
        private readonly StubCardEditLockRepository _locks;

        public long Revision { get; set; }

        public IReadOnlyList<PriorityDefinition> Priorities { get; set; } = [];

        public IReadOnlyList<CardTypeDefinition> CardTypes { get; set; } =
        [
            new CardTypeDefinition
            {
                Id = 1,
                Name = "Generica",
                ColorHex = "#64748B",
                SortOrder = 0,
                IsActive = true,
                IsDefault = true,
                Version = 1,
                SystemKey = SystemCardTypeKeys.Generic,
                IsSystem = true,
            },
        ];

        public IReadOnlyList<PriorityTypeDeadline> DeadlineRules { get; set; } = [];

        public int GetCallCount { get; private set; }

        public StubBoardSnapshotRepository(
            StubColumnRepository columns,
            StubCardRepository cards,
            StubCardEditLockRepository locks)
        {
            _columns = columns;
            _cards = cards;
            _locks = locks;
        }

        public async Task<KanbanBoardSnapshot> GetAsync(
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            var columns = await _columns.GetAllAsync(cancellationToken);
            var cards = await _cards.GetAllAsync(cancellationToken);
            var locks = await _locks.GetActiveAsync(cancellationToken);
            return new KanbanBoardSnapshot(
                Revision,
                columns,
                cards,
                Priorities,
                CardTypes,
                DeadlineRules,
                locks);
        }
    }

    internal sealed class StubBoardChangeDetector : IBoardChangeDetector
    {
        private readonly Queue<Func<CancellationToken, Task<bool>>> _responses = new();

        public bool HasChanged { get; set; }

        public Func<CancellationToken, Task<bool>>? Handler { get; set; }

        public int CallCount { get; private set; }

        public void EnqueueResult(bool hasChanged) =>
            _responses.Enqueue(_ => Task.FromResult(hasChanged));

        public void EnqueueFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _responses.Enqueue(_ => Task.FromException<bool>(exception));
        }

        public Task<bool> HasChangedSinceLastCheckAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (_responses.Count > 0)
            {
                return _responses.Dequeue()(cancellationToken);
            }

            return Handler is null
                ? Task.FromResult(HasChanged)
                : Handler(cancellationToken);
        }
    }

    internal sealed class ManualRecurringTaskScheduler : IRecurringTaskScheduler
    {
        private readonly AsyncRecurringTaskCoordinator _coordinator = new();
        private TimeSpan _interval = TimeSpan.FromSeconds(1);
        private TimeSpan _elapsed;

        public TimeSpan Interval
        {
            get => _interval;
            set
            {
                ThrowIfDisposed();
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _interval = value;
                _elapsed = TimeSpan.Zero;
            }
        }

        public bool IsRunning => !IsDisposed && _coordinator.IsRunning;

        public bool IsExecuting => !IsDisposed && _coordinator.IsExecuting;

        public bool IsDisposed { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start(
            Func<CancellationToken, Task> callback,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(callback);

            _elapsed = TimeSpan.Zero;
            _coordinator.Start(callback, cancellationToken);
            StartCount++;
        }

        public Task<bool> TriggerAsync()
        {
            ThrowIfDisposed();
            return _coordinator.TryExecuteAsync();
        }

        public async Task AdvanceByAsync(TimeSpan elapsed)
        {
            ThrowIfDisposed();
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            _elapsed += elapsed;
            while (IsRunning && _elapsed >= Interval)
            {
                _elapsed -= Interval;
                await TriggerAsync();
            }
        }

        public async Task StopAsync()
        {
            if (IsDisposed)
            {
                return;
            }

            StopCount++;
            await _coordinator.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (IsDisposed)
            {
                return;
            }

            await _coordinator.DisposeAsync();
            IsDisposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ManualRecurringTaskScheduler));
            }
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
