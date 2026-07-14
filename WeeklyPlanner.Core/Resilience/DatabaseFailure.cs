namespace WeeklyPlanner.Core.Resilience;

public sealed record DatabaseFailure(
    DatabaseFailureKind Kind,
    string UserMessage,
    bool CanRetryAutomatically,
    bool RequiresAttention);
