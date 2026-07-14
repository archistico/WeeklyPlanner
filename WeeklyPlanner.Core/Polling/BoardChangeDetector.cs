using WeeklyPlanner.Core.Repositories;

namespace WeeklyPlanner.Core.Polling;

public sealed class BoardChangeDetector : IBoardChangeDetector
{
    private readonly IBoardRevisionRepository _revisionRepository;
    private long? _lastKnownRevision;

    public BoardChangeDetector(IBoardRevisionRepository revisionRepository)
    {
        _revisionRepository = revisionRepository;
    }

    public async Task<bool> HasChangedSinceLastCheckAsync(CancellationToken cancellationToken = default)
    {
        var currentRevision = await _revisionRepository.GetCurrentRevisionAsync(cancellationToken);

        if (_lastKnownRevision == currentRevision)
        {
            return false;
        }

        _lastKnownRevision = currentRevision;
        return true;
    }
}
