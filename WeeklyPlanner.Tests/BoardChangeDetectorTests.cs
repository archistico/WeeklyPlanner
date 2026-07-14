using WeeklyPlanner.Core.Polling;
using WeeklyPlanner.Core.Repositories;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardChangeDetectorTests
{
    [Fact]
    public async Task First_check_reports_changed_and_captures_baseline()
    {
        var fakeRepository = new FakeBoardRevisionRepository(0);
        var detector = new BoardChangeDetector(fakeRepository);

        var changed = await detector.HasChangedSinceLastCheckAsync();

        Assert.True(changed);
    }

    [Fact]
    public async Task Second_check_reports_unchanged_if_revision_is_the_same()
    {
        var fakeRepository = new FakeBoardRevisionRepository(3);
        var detector = new BoardChangeDetector(fakeRepository);

        await detector.HasChangedSinceLastCheckAsync();
        var secondCheck = await detector.HasChangedSinceLastCheckAsync();

        Assert.False(secondCheck);
    }

    [Fact]
    public async Task Reports_changed_again_after_revision_advances()
    {
        var fakeRepository = new FakeBoardRevisionRepository(7);
        var detector = new BoardChangeDetector(fakeRepository);

        await detector.HasChangedSinceLastCheckAsync();
        fakeRepository.CurrentRevision = 8;
        var changed = await detector.HasChangedSinceLastCheckAsync();

        Assert.True(changed);
    }

    private sealed class FakeBoardRevisionRepository : IBoardRevisionRepository
    {
        public long CurrentRevision { get; set; }

        public FakeBoardRevisionRepository(long initialRevision)
        {
            CurrentRevision = initialRevision;
        }

        public Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentRevision);
    }
}
