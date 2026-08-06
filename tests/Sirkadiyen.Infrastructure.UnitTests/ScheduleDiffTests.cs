using System.Globalization;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Coverage for the gate that decides whether a calculated diff may reach a
/// calendar at all.
/// </summary>
public sealed class ScheduleDiffTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SourceRowId = Guid.CreateVersion7();
    private static readonly Guid PreviousRevisionId = Guid.CreateVersion7();
    private static readonly Guid CurrentRevisionId = Guid.CreateVersion7();

    [Fact]
    public void AnOrdinaryDiffIsCountedAndReadyToDispatch()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Created, 2),
            Entries(ScheduleDiffChange.Updated, 3),
            Entries(ScheduleDiffChange.Deleted, 1),
            Entries(ScheduleDiffChange.Unchanged, 40));

        Assert.Equal(ScheduleDiffState.Ready, diff.State);
        Assert.True(diff.IsDispatchable);
        Assert.Null(diff.HoldReason);
        Assert.Equal(2, diff.CreatedCount);
        Assert.Equal(3, diff.UpdatedCount);
        Assert.Equal(1, diff.DeletedCount);
        Assert.Equal(40, diff.UnchangedCount);
        Assert.Equal(0, diff.AmbiguousCount);

        // A previous record is anything the diff matched or deleted; a current
        // record is anything it matched or created.
        Assert.Equal(44, diff.PreviousRecordCount);
        Assert.Equal(45, diff.CurrentRecordCount);
    }

    [Fact]
    public void EveryEntryIsStampedWithTheDiffItBelongsTo()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        Assert.All(diff.Entries, entry => Assert.Equal(diff.Id, entry.ScheduleDiffId));
        Assert.All(diff.Entries, entry => Assert.NotEqual(Guid.Empty, entry.Id));
        Assert.Equal(3, diff.Entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void ASingleAmbiguousEntryHoldsTheWholeDiff()
    {
        // Acting on the unambiguous part while ignoring an ambiguous pair would
        // delete the previous record of that pair from a student's calendar.
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Unchanged, 100),
            Entries(ScheduleDiffChange.Ambiguous, 1));

        Assert.Equal(ScheduleDiffState.Held, diff.State);
        Assert.False(diff.IsDispatchable);
        Assert.Contains("ambiguous", diff.HoldReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void MassDeletionOverTheToleratedShareHoldsTheDiff()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 28));

        Assert.Equal(ScheduleDiffState.Held, diff.State);
        Assert.Contains("12 of 40", diff.HoldReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletionsWithinTheToleratedShareAreDispatched()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 88));

        Assert.Equal(ScheduleDiffState.Ready, diff.State);
    }

    [Fact]
    public void ASmallSourceIsNotHeldForAFewDeletions()
    {
        // Five of five records disappearing is 100 percent, but a source this
        // small trips the share on ordinary editing. Holding it every time would
        // train an operator to approve without reading.
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Deleted, 5));

        Assert.Equal(ScheduleDiffState.Ready, diff.State);
    }

    [Fact]
    public void AFirstPublicationCreatesEverythingAndIsNotHeld()
    {
        ScheduleDiff diff = ScheduleDiff.Create(
            SourceRowId,
            SourceId.Parse("G1-TR-ANNUAL"),
            previousRevisionId: null,
            CurrentRevisionId,
            Entries(ScheduleDiffChange.Created, 400),
            new ScheduleDiffSafetyThresholds(),
            Now);

        Assert.Null(diff.PreviousRevisionId);
        Assert.Equal(0, diff.PreviousRecordCount);
        Assert.Equal(400, diff.CreatedCount);
        Assert.Equal(ScheduleDiffState.Ready, diff.State);
    }

    [Fact]
    public void TheHoldReasonIsWrittenInvariantlyOnATurkishHost()
    {
        // A Turkish host writes 0.300 as "0,300". A stored reason has to read the
        // same everywhere, the way validation thresholds already do.
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            ScheduleDiff diff = Create(
                Entries(ScheduleDiffChange.Deleted, 12),
                Entries(ScheduleDiffChange.Unchanged, 28));

            Assert.Contains("0.300", diff.HoldReason!, StringComparison.Ordinal);
            Assert.DoesNotContain("0,300", diff.HoldReason!, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ARevisionCannotBeDiffedAgainstItself()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ScheduleDiff.Create(
                SourceRowId,
                SourceId.Parse("G1-TR-ANNUAL"),
                CurrentRevisionId,
                CurrentRevisionId,
                [],
                new ScheduleDiffSafetyThresholds(),
                Now));

        Assert.Equal("previousRevisionId", exception.ParamName);
    }

    [Fact]
    public void AnUnusableThresholdIsRefused()
    {
        ScheduleDiffSafetyThresholds thresholds = new() { MaximumDeletionShare = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(thresholds.Validate);
    }

    [Fact]
    public void ReleasingAHeldDiffRecordsWhoTookResponsibilityAndKeepsTheHoldReason()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 28));
        string holdReason = diff.HoldReason!;

        Assert.True(diff.IsReleasable);
        diff.Release("semih", "The 12 deletions are the ended anatomy block.", Now.AddHours(2));

        Assert.Equal(ScheduleDiffState.Released, diff.State);
        Assert.True(diff.IsDispatchable);
        Assert.Equal("semih", diff.ReleasedBy);
        Assert.Equal(Now.AddHours(2), diff.ReleasedAtUtc);

        // "Held for this reason, then released by this person" is the record
        // that matters when someone later asks why lessons left a calendar.
        Assert.Equal(holdReason, diff.HoldReason);
        Assert.False(diff.IsReleasable);
    }

    [Fact]
    public void AnAmbiguousDiffIsNeverReleasable()
    {
        // An operator can confirm a large deletion by reading the source, but
        // cannot decide which of several candidates a record became. Releasing
        // it would leave the previous lesson in every affected calendar and
        // never write its replacement.
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Ambiguous, 2),
            Entries(ScheduleDiffChange.Unchanged, 100));

        Assert.False(diff.IsReleasable);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => diff.Release("semih", "Looks fine to me.", Now));

        Assert.Contains("source", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ScheduleDiffState.Held, diff.State);
        Assert.False(diff.IsDispatchable);
    }

    [Fact]
    public void AReadyDiffHasNothingToRelease()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        Assert.False(diff.IsReleasable);
        Assert.Throws<InvalidOperationException>(() => diff.Release("semih", "Why not.", Now));
    }

    [Fact]
    public void AReleaseMustNameAnOperatorAndAReason()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 28));

        Assert.Throws<ArgumentException>(() => diff.Release(" ", "A reason.", Now));
        Assert.Throws<ArgumentException>(() => diff.Release("semih", " ", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => diff.Release(
            new string('x', ScheduleDiff.MaximumReleasedByLength + 1),
            "A reason.",
            Now));
        Assert.Equal(ScheduleDiffState.Held, diff.State);
    }

    [Fact]
    public void AReadyDiffStartsPendingDispatchAndUntried()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        Assert.Equal(CalendarDispatchState.Pending, diff.CalendarDispatchState);
        Assert.True(diff.IsDispatchPending);
        Assert.Equal(0, diff.DispatchAttempts);
        Assert.Null(diff.NextAttemptAtUtc);
        Assert.Null(diff.DispatchedAtUtc);
    }

    [Fact]
    public void MarkingADiffDispatchedRecordsWhenAndStopsItBeingPending()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        diff.MarkDispatched(Now.AddMinutes(5));

        Assert.Equal(CalendarDispatchState.Dispatched, diff.CalendarDispatchState);
        Assert.Equal(Now.AddMinutes(5), diff.DispatchedAtUtc);
        Assert.False(diff.IsDispatchPending);
    }

    [Fact]
    public void MarkingAnAlreadyDispatchedDiffAgainIsANoOp()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));
        diff.MarkDispatched(Now.AddMinutes(5));

        diff.MarkDispatched(Now.AddMinutes(9));

        // The first dispatch time stands; a resumed pass must not rewrite it.
        Assert.Equal(Now.AddMinutes(5), diff.DispatchedAtUtc);
    }

    [Fact]
    public void AHeldDiffCannotBeDispatched()
    {
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Ambiguous, 1),
            Entries(ScheduleDiffChange.Unchanged, 100));

        Assert.False(diff.IsDispatchPending);
        Assert.Throws<InvalidOperationException>(() => diff.MarkDispatched(Now));
        Assert.Throws<InvalidOperationException>(
            () => diff.RecordDispatchFailure("boom", TimeSpan.FromSeconds(30), 3, Now));
    }

    [Fact]
    public void ATransientFailureDefersTheDiffWithABackOffThatGrows()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        diff.RecordDispatchFailure("rate limited", TimeSpan.FromSeconds(30), maxAttempts: 3, Now);
        Assert.Equal(CalendarDispatchState.Pending, diff.CalendarDispatchState);
        Assert.Equal(1, diff.DispatchAttempts);
        Assert.Equal(Now.AddSeconds(30), diff.NextAttemptAtUtc);
        Assert.Contains("rate limited", diff.DispatchFailureReason!, StringComparison.Ordinal);

        diff.RecordDispatchFailure("rate limited", TimeSpan.FromSeconds(30), maxAttempts: 3, Now);
        Assert.Equal(2, diff.DispatchAttempts);
        Assert.Equal(Now.AddSeconds(60), diff.NextAttemptAtUtc);
        Assert.True(diff.IsDispatchPending);
    }

    [Fact]
    public void TooManyTransientFailuresGiveUpAndNeedAnOperator()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));

        diff.RecordDispatchFailure("boom", TimeSpan.FromSeconds(30), maxAttempts: 2, Now);
        diff.RecordDispatchFailure("boom", TimeSpan.FromSeconds(30), maxAttempts: 2, Now);

        Assert.Equal(CalendarDispatchState.Failed, diff.CalendarDispatchState);
        Assert.Null(diff.NextAttemptAtUtc);
        Assert.False(diff.IsDispatchPending);
        Assert.Throws<InvalidOperationException>(() => diff.MarkDispatched(Now));
    }

    [Fact]
    public void ReleasingAHeldDiffLeavesItPendingDispatchAndReadyToActOn()
    {
        // A held diff cannot be dispatched at all, so releasing it is what first makes it eligible.
        ScheduleDiff diff = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 28));
        Assert.False(diff.IsDispatchPending);

        diff.Release("semih", "The 12 deletions are the ended anatomy block.", Now.AddHours(1));

        // Release changes only the review state; the dispatch fields are untouched, so the freshly
        // dispatchable diff is now pending with no attempts spent.
        Assert.Equal(CalendarDispatchState.Pending, diff.CalendarDispatchState);
        Assert.Equal(0, diff.DispatchAttempts);
        Assert.True(diff.IsDispatchPending);
    }

    [Fact]
    public void RetryingAFailedDispatchReturnsItToTheQueueWithFreshAttempts()
    {
        ScheduleDiff diff = Failed();

        diff.RetryDispatch("semih", "The Calendar outage is over.", Now.AddHours(2));

        Assert.Equal(CalendarDispatchState.Pending, diff.CalendarDispatchState);
        Assert.Equal(0, diff.DispatchAttempts);
        Assert.Null(diff.NextAttemptAtUtc);
        Assert.True(diff.IsDispatchPending);
    }

    [Fact]
    public void ARetryIsAttributableAndCounted()
    {
        ScheduleDiff diff = Failed();

        diff.RetryDispatch("semih", "First try.", Now.AddHours(2));
        diff.RecordDispatchFailure("boom", TimeSpan.FromSeconds(30), maxAttempts: 1, Now);
        diff.RetryDispatch("semih", "Second try.", Now.AddHours(3));

        // Attempts reset with each retry, so the retry count is what shows a diff that keeps
        // failing rather than one bad night.
        Assert.Equal(2, diff.DispatchRetryCount);
        Assert.Equal("semih", diff.LastDispatchRetriedBy);
        Assert.Equal("Second try.", diff.LastDispatchRetryReason);
        Assert.Equal(Now.AddHours(3), diff.LastDispatchRetriedAtUtc);
    }

    [Fact]
    public void ARetryKeepsTheFailureReasonUntilTheNextAttemptReportsItsOwn()
    {
        ScheduleDiff diff = Failed();

        diff.RetryDispatch("semih", "Retrying.", Now.AddHours(2));

        Assert.Equal("boom", diff.DispatchFailureReason);
    }

    [Fact]
    public void OnlyAFailedDispatchCanBeRetried()
    {
        ScheduleDiff pending = Create(Entries(ScheduleDiffChange.Created, 3));
        Assert.Throws<InvalidOperationException>(
            () => pending.RetryDispatch("semih", "Reason.", Now));

        ScheduleDiff dispatched = Create(Entries(ScheduleDiffChange.Created, 3));
        dispatched.MarkDispatched(Now);
        Assert.Throws<InvalidOperationException>(
            () => dispatched.RetryDispatch("semih", "Reason.", Now));
    }

    [Fact]
    public void AHeldDiffCannotBeRetriedIntoDispatch()
    {
        // Retrying a held diff would be releasing it under another name, and release is a
        // different, named decision (ADR-042).
        ScheduleDiff held = Create(
            Entries(ScheduleDiffChange.Deleted, 12),
            Entries(ScheduleDiffChange.Unchanged, 28));

        Assert.False(held.IsDispatchRetriable);
        Assert.Throws<InvalidOperationException>(
            () => held.RetryDispatch("semih", "Reason.", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARetryMustStateWhoAndWhy(string blank)
    {
        ScheduleDiff diff = Failed();

        Assert.ThrowsAny<ArgumentException>(() => diff.RetryDispatch(blank, "Reason.", Now));
        Assert.ThrowsAny<ArgumentException>(() => diff.RetryDispatch("semih", blank, Now));
    }

    private static ScheduleDiff Failed()
    {
        ScheduleDiff diff = Create(Entries(ScheduleDiffChange.Created, 3));
        diff.RecordDispatchFailure("boom", TimeSpan.FromSeconds(30), maxAttempts: 1, Now);
        Assert.Equal(CalendarDispatchState.Failed, diff.CalendarDispatchState);
        return diff;
    }

    private static ScheduleDiff Create(params IReadOnlyList<ScheduleDiffEntry>[] groups) =>
        ScheduleDiff.Create(
            SourceRowId,
            SourceId.Parse("G1-TR-ANNUAL"),
            PreviousRevisionId,
            CurrentRevisionId,
            [.. groups.SelectMany(group => group)],
            new ScheduleDiffSafetyThresholds(),
            Now);

    private static IReadOnlyList<ScheduleDiffEntry> Entries(ScheduleDiffChange change, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new ScheduleDiffEntry
        {
            Change = change,
            Match = change is ScheduleDiffChange.Created or ScheduleDiffChange.Deleted
                ? ScheduleDiffMatch.None
                : ScheduleDiffMatch.ExactStableIdentity,
            PreviousRecordId = change is ScheduleDiffChange.Created
                ? null
                : Guid.CreateVersion7(),
            CurrentRecordId = change is ScheduleDiffChange.Deleted
                ? null
                : Guid.CreateVersion7(),
        })];
}
