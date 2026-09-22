using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The ceiling on diff recalculation (ADR-164): how a failed attempt defers the
/// next one, when the attempts stop, and what it takes to start them again.
/// </summary>
/// <remarks>
/// These rules exist because "pending" is derived — a published revision with no
/// diff row — so a failed attempt leaves no trace of itself and the revision is
/// due again immediately. Everything here is about bounding what a fault that
/// will never succeed costs, without taking away a transient fault's recovery.
/// </remarks>
public sealed class ScheduleRevisionDiffRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(1);

    [Fact]
    public void APublishedRevisionStartsPendingAndDueImmediately()
    {
        ScheduleRevision revision = Published();

        Assert.Equal(RevisionDiffState.Pending, revision.DiffState);
        Assert.Equal(0, revision.DiffAttempts);
        Assert.Null(revision.NextDiffAttemptAtUtc);
        Assert.False(revision.IsDiffCalculationRetriable);
    }

    [Fact]
    public void TheFirstFailureDefersTheNextAttemptByTheBaseDelay()
    {
        ScheduleRevision revision = Published();

        revision.RecordDiffCalculationFailure("duplicate key", BaseDelay, maxAttempts: 6, Now);

        Assert.Equal(RevisionDiffState.Pending, revision.DiffState);
        Assert.Equal(1, revision.DiffAttempts);
        Assert.Equal(Now + BaseDelay, revision.NextDiffAttemptAtUtc);
        Assert.Equal("duplicate key", revision.DiffFailureReason);
    }

    [Fact]
    public void EachFurtherFailureDoublesTheWait()
    {
        ScheduleRevision revision = Published();

        revision.RecordDiffCalculationFailure("first", BaseDelay, maxAttempts: 6, Now);
        revision.RecordDiffCalculationFailure("second", BaseDelay, maxAttempts: 6, Now);
        revision.RecordDiffCalculationFailure("third", BaseDelay, maxAttempts: 6, Now);

        Assert.Equal(Now + (BaseDelay * 4), revision.NextDiffAttemptAtUtc);
        Assert.Equal(3, revision.DiffAttempts);
    }

    [Fact]
    public void TheLastAllowedFailureGivesUpInsteadOfSchedulingAnother()
    {
        // This is the whole point: without it, four revisions were recalculated every six seconds
        // for three weeks because the diff they produced could never be stored.
        ScheduleRevision revision = Published();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            revision.RecordDiffCalculationFailure("still broken", BaseDelay, maxAttempts: 3, Now);
        }

        Assert.Equal(RevisionDiffState.Failed, revision.DiffState);
        Assert.Null(revision.NextDiffAttemptAtUtc);
        Assert.True(revision.IsDiffCalculationRetriable);
    }

    [Fact]
    public void GivingUpDoesNotUnpublishTheRevision()
    {
        // The revision is still live and its changes still happened; only the automatic
        // recalculation stopped. Conflating the two would make a calculation fault look like an
        // unpublication to everything downstream.
        ScheduleRevision revision = Published();

        revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 1, Now);

        Assert.Equal(RevisionDiffState.Failed, revision.DiffState);
        Assert.Equal(RevisionState.Published, revision.State);
        Assert.Equal(Now, revision.PublishedAtUtc);
    }

    [Fact]
    public void AFailureCannotBeRecordedOnceTheRevisionHasGivenUp()
    {
        ScheduleRevision revision = Published();
        revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 1, Now);

        Assert.Throws<InvalidOperationException>(() =>
            revision.RecordDiffCalculationFailure("again", BaseDelay, maxAttempts: 1, Now));
    }

    [Fact]
    public void ARevisionThatWasNeverPublishedCannotFailCalculation()
    {
        // Nothing diffs an unpublished revision, so a failure recorded against one would be a
        // count nobody can explain.
        ScheduleRevision revision = new(
            Guid.CreateVersion7(),
            SourceId.Parse("G1-TR-ANNUAL"),
            Guid.CreateVersion7(),
            Now);

        Assert.Throws<InvalidOperationException>(() =>
            revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 6, Now));
    }

    [Fact]
    public void AnOperatorRetryClearsTheAttemptsButKeepsThatItWasRetried()
    {
        ScheduleRevision revision = Published();
        revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 1, Now);

        revision.RetryDiffCalculation("semih", "The differ fix is deployed.", Now.AddHours(2));

        Assert.Equal(RevisionDiffState.Pending, revision.DiffState);
        Assert.Equal(0, revision.DiffAttempts);
        Assert.Null(revision.NextDiffAttemptAtUtc);
        Assert.Equal("semih", revision.DiffRetriedBy);
        Assert.Equal("The differ fix is deployed.", revision.DiffRetryReason);
        Assert.Equal(Now.AddHours(2), revision.DiffRetriedAtUtc);

        // The failure that caused it is not erased: a revision retried repeatedly must stay
        // readable as such rather than looking untouched.
        Assert.Equal("broken", revision.DiffFailureReason);
    }

    [Fact]
    public void ARevisionStillBeingRetriedAutomaticallyCannotBeRetriedByHand()
    {
        ScheduleRevision revision = Published();
        revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 6, Now);

        Assert.False(revision.IsDiffCalculationRetriable);
        Assert.Throws<InvalidOperationException>(() =>
            revision.RetryDiffCalculation("semih", "Impatient.", Now));
    }

    [Fact]
    public void ARetryMustSayWhoAndWhy()
    {
        ScheduleRevision revision = Published();
        revision.RecordDiffCalculationFailure("broken", BaseDelay, maxAttempts: 1, Now);

        Assert.Throws<ArgumentException>(() => revision.RetryDiffCalculation(" ", "Reason.", Now));
        Assert.Throws<ArgumentException>(() => revision.RetryDiffCalculation("semih", " ", Now));
    }

    [Fact]
    public void AnOverLongFailureReasonIsTruncatedRatherThanRefused()
    {
        // The reason comes from an exception message, which no caller controls the length of.
        // Losing the tail of it is acceptable; losing the record of the failure is not.
        ScheduleRevision revision = Published();

        revision.RecordDiffCalculationFailure(
            new string('x', ScheduleRevision.MaximumDiffFailureReasonLength + 500),
            BaseDelay,
            maxAttempts: 6,
            Now);

        Assert.Equal(
            ScheduleRevision.MaximumDiffFailureReasonLength,
            revision.DiffFailureReason!.Length);
    }

    private static ScheduleRevision Published()
    {
        ScheduleRevision revision = new(
            Guid.CreateVersion7(),
            SourceId.Parse("G1-TR-ANNUAL"),
            Guid.CreateVersion7(),
            Now);

        revision.TransitionTo(RevisionState.Validating, Now);
        revision.TransitionTo(RevisionState.Validated, Now, "All validation rules passed.");
        revision.TransitionTo(RevisionState.Published, Now);
        return revision;
    }
}
