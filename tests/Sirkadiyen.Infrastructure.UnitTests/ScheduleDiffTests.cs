using System.Globalization;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleSources;
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
