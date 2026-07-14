using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using WeeklyPlanner.Core.Resilience;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseFailureClassifierTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Busy_and_locked_are_classified_as_transient_contention(int errorCode)
    {
        var failure = DatabaseFailureClassifier.Classify(
            new SqliteException("temporary lock", errorCode));

        Assert.Equal(DatabaseFailureKind.Contention, failure.Kind);
        Assert.True(failure.CanRetryAutomatically);
        Assert.False(failure.RequiresAttention);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    public void Io_and_cantopen_are_classified_as_unavailable(int errorCode)
    {
        var failure = DatabaseFailureClassifier.Classify(
            new SqliteException("unavailable", errorCode));

        Assert.Equal(DatabaseFailureKind.Unavailable, failure.Kind);
        Assert.True(failure.CanRetryAutomatically);
        Assert.False(failure.RequiresAttention);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(26)]
    public void Corrupt_and_notadb_require_attention(int errorCode)
    {
        var failure = DatabaseFailureClassifier.Classify(
            new SqliteException("invalid database", errorCode));

        Assert.Equal(DatabaseFailureKind.Corrupt, failure.Kind);
        Assert.False(failure.CanRetryAutomatically);
        Assert.True(failure.RequiresAttention);
    }

    [Fact]
    public void Wrapped_permission_error_is_unwrapped_and_classified()
    {
        var failure = DatabaseFailureClassifier.Classify(
            new InvalidOperationException("wrapper", new UnauthorizedAccessException("denied")));

        Assert.Equal(DatabaseFailureKind.PermissionDenied, failure.Kind);
        Assert.False(failure.CanRetryAutomatically);
        Assert.True(failure.RequiresAttention);
    }

    [Fact]
    public void Newer_schema_is_reported_with_the_actionable_original_message()
    {
        var exception = new SchemaVersionMismatchException(4, 3);

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(DatabaseFailureKind.Schema, failure.Kind);
        Assert.Equal(exception.Message, failure.UserMessage);
        Assert.False(failure.CanRetryAutomatically);
        Assert.True(failure.RequiresAttention);
    }

    [Fact]
    public void Unknown_error_has_a_safe_user_message()
    {
        var failure = DatabaseFailureClassifier.Classify(new InvalidOperationException("technical detail"));

        Assert.Equal(DatabaseFailureKind.Unknown, failure.Kind);
        Assert.False(failure.CanRetryAutomatically);
        Assert.DoesNotContain("technical detail", failure.UserMessage);
    }
}
