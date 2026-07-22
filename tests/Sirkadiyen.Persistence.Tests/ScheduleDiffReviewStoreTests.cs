using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Proves the operator path for a held diff: what the queue shows, what a
/// release records, and what it refuses (ADR-042).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScheduleDiffReviewStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheQueueShowsAHeldDiffWithTheReasonAndWhetherItCanBeReleased()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        IReadOnlyList<ScheduleDiffSummary> held = await Store(context)
            .ListByStateAsync(ScheduleDiffState.Held, 200, Token);

        ScheduleDiffSummary summary = Assert.Single(
            held,
            candidate => candidate.ScheduleDiffId == diff.Id);
        Assert.Equal(20, summary.DeletedCount);
        Assert.Equal(40, summary.PreviousRecordCount);
        Assert.Contains("20 of 40", summary.HoldReason!, StringComparison.Ordinal);
        Assert.True(summary.IsReleasable);
        Assert.Null(summary.ReleasedBy);
    }

    [Fact]
    public async Task TheDetailShowsTheDeletedLessonsFirstAndCountsTheRest()
    {
        // An operator releasing a diff without seeing which lessons it deletes
        // is rubber-stamping, so the deletions must be what they see first.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        ScheduleDiffDetail detail = (await Store(context).FindAsync(diff.Id, 5, Token))!;

        Assert.Equal(20, detail.ActionableEntryCount);
        Assert.Equal(5, detail.Entries.Count);
        Assert.All(
            detail.Entries,
            entry => Assert.Equal(ScheduleDiffChange.Deleted, entry.Change));

        // Described as a lesson, not as a record identifier.
        ScheduleDiffRecordView previous = Assert.IsType<ScheduleDiffRecordView>(
            detail.Entries[0].Previous);
        Assert.StartsWith("Lesson ", previous.DisplayTitle, StringComparison.Ordinal);
        Assert.Equal(new TimeOnly(9, 0), previous.StartLocalTime);
        Assert.Null(detail.Entries[0].Current);

        // Unchanged entries are excluded: they are the majority and say nothing
        // about whether the hold is legitimate.
        Assert.DoesNotContain(
            detail.Entries,
            entry => entry.Change is ScheduleDiffChange.Unchanged);
    }

    [Fact]
    public async Task ReleasingAHeldDiffMakesItDispatchableAndRecordsWhoTookResponsibility()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        ScheduleDiffReleaseResult result = await Store(context).ReleaseAsync(
            diff.Id,
            "semih",
            "Checked the source: the 20 deleted lessons are the ended block.",
            Now.AddHours(3),
            Token);

        Assert.Equal(ScheduleDiffReleaseOutcome.Released, result.Outcome);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await context.ScheduleDiffs.SingleAsync(
            candidate => candidate.Id == diff.Id,
            Token);
        Assert.Equal(ScheduleDiffState.Released, stored.State);
        Assert.True(stored.IsDispatchable);
        Assert.Equal("semih", stored.ReleasedBy);
        Assert.Equal(Now.AddHours(3), stored.ReleasedAtUtc);

        // The hold survives the release: "held for this reason, then released by
        // this person" is the record that matters afterwards.
        Assert.Contains("20 of 40", stored.HoldReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiffCanOnlyBeReleasedOnce()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        await Store(context).ReleaseAsync(diff.Id, "semih", "Verified.", Now, Token);
        context.ChangeTracker.Clear();
        ScheduleDiffReleaseResult second = await Store(context)
            .ReleaseAsync(diff.Id, "someone-else", "Verified again.", Now, Token);

        Assert.Equal(ScheduleDiffReleaseOutcome.NotHeld, second.Outcome);
        Assert.Equal(ScheduleDiffState.Released, second.ObservedState);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await context.ScheduleDiffs.SingleAsync(
            candidate => candidate.Id == diff.Id,
            Token);
        Assert.Equal("semih", stored.ReleasedBy);
    }

    [Fact]
    public async Task AnAmbiguousHoldIsRefusedAndStaysHeld()
    {
        // An operator cannot decide which of several candidates a record became.
        // Releasing it would leave the previous lesson in every affected calendar
        // and never write its replacement, so the source has to say which is which.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAnAmbiguityAsync(context);

        ScheduleDiffReleaseResult result = await Store(context)
            .ReleaseAsync(diff.Id, "semih", "Looks fine to me.", Now, Token);

        Assert.Equal(ScheduleDiffReleaseOutcome.AmbiguityMustBeResolvedAtSource, result.Outcome);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await context.ScheduleDiffs.SingleAsync(
            candidate => candidate.Id == diff.Id,
            Token);
        Assert.Equal(ScheduleDiffState.Held, stored.State);
        Assert.False(stored.IsDispatchable);
        Assert.Null(stored.ReleasedBy);
    }

    [Fact]
    public async Task AReadyDiffHasNothingToRelease()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now,
            ["a", "b"]);
        ScheduleDiff diff = await CalculateAsync(context, revision.Id);

        Assert.Equal(ScheduleDiffState.Ready, diff.State);
        ScheduleDiffReleaseResult result = await Store(context)
            .ReleaseAsync(diff.Id, "semih", "No reason at all.", Now, Token);

        Assert.Equal(ScheduleDiffReleaseOutcome.NotHeld, result.Outcome);
        Assert.Equal(ScheduleDiffState.Ready, result.ObservedState);
    }

    [Fact]
    public async Task AMissingDiffIsReportedRatherThanThrown()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        Guid missing = Guid.CreateVersion7();

        Assert.Null(await Store(context).FindAsync(missing, 10, Token));
        Assert.Equal(
            ScheduleDiffReleaseOutcome.DiffNotFound,
            (await Store(context).ReleaseAsync(missing, "semih", "Whatever.", Now, Token)).Outcome);
    }

    [Fact]
    public async Task AConcurrentReleaseIsRefusedRatherThanOverwritingTheFirst()
    {
        // Two operators reading the same held diff must not silently overwrite
        // each other's decision; this is the last gate before calendars change.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        await using SirkadiyenDbContext first = fixture.CreateContext();
        await using SirkadiyenDbContext second = fixture.CreateContext();

        // Both load the diff before either writes.
        await first.ScheduleDiffs.SingleAsync(candidate => candidate.Id == diff.Id, Token);
        await second.ScheduleDiffs.SingleAsync(candidate => candidate.Id == diff.Id, Token);

        Assert.Equal(
            ScheduleDiffReleaseOutcome.Released,
            (await Store(first).ReleaseAsync(diff.Id, "first", "Verified.", Now, Token)).Outcome);
        Assert.Equal(
            ScheduleDiffReleaseOutcome.ConcurrentRelease,
            (await Store(second).ReleaseAsync(diff.Id, "second", "Verified.", Now, Token)).Outcome);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await context.ScheduleDiffs.SingleAsync(
            candidate => candidate.Id == diff.Id,
            Token);
        Assert.Equal("first", stored.ReleasedBy);
    }

    [Fact]
    public async Task ReleasingWorksUnderTheHostsRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleDiff diff = await HoldAMassDeletionAsync(context);

        ScheduleDiffReleaseResult result = await Store(context)
            .ReleaseAsync(diff.Id, "semih", "Verified against the source.", Now, Token);

        Assert.Equal(ScheduleDiffReleaseOutcome.Released, result.Outcome);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ScheduleDiffReviewStore Store(SirkadiyenDbContext context) => new(context);

    /// <summary>Publishes 40 lessons and then 20, which trips the dispatch gate.</summary>
    private static async Task<ScheduleDiff> HoldAMassDeletionAsync(SirkadiyenDbContext context)
    {
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        string[] full = [.. Enumerable.Range(0, 40).Select(index => $"lesson-{index:D2}")];

        await ScheduleDiffScenario.PublishAsync(context, source, Now, full);
        ScheduleRevision second = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now.AddHours(1),
            full[..20]);

        ScheduleDiff diff = await CalculateAsync(context, second.Id);
        Assert.Equal(ScheduleDiffState.Held, diff.State);
        return diff;
    }

    /// <summary>
    /// Stores a diff the differ could not resolve.
    /// </summary>
    /// <remarks>
    /// The entries are written directly rather than produced from two revisions.
    /// Ambiguity needs lesson title, instructor and department to be similar on
    /// both sides and to match one-to-many, which is a property of the differ
    /// (ADR-035) and is covered by its own tests; what this store has to prove is
    /// that an ambiguous diff, however it arose, cannot be waved through.
    /// </remarks>
    private static async Task<ScheduleDiff> HoldAnAmbiguityAsync(SirkadiyenDbContext context)
    {
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision previous = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now,
            ["a"]);
        ScheduleRevision current = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now.AddHours(1),
            ["b"]);

        context.ChangeTracker.Clear();
        Guid previousRecordId = await RecordIdAsync(context, previous.Id);
        Guid currentRecordId = await RecordIdAsync(context, current.Id);

        ScheduleDiff diff = ScheduleDiff.Create(
            source.Id,
            source.SourceId,
            previous.Id,
            current.Id,
            [
                new ScheduleDiffEntry
                {
                    Change = ScheduleDiffChange.Ambiguous,
                    Match = ScheduleDiffMatch.SecondaryAttributes,
                    PreviousRecordId = previousRecordId,
                    CurrentRecordId = currentRecordId,
                    MatchScore = 0.85m,
                },
            ],
            new ScheduleDiffSafetyThresholds(),
            Now.AddHours(2));

        ScheduleDiffPersistenceResult stored = await new ScheduleDiffStore(context)
            .SaveAsync(diff, Token);
        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, stored.Outcome);
        Assert.Equal(ScheduleDiffState.Held, diff.State);

        context.ChangeTracker.Clear();
        return diff;
    }

    private static async Task<ScheduleDiff> CalculateAsync(
        SirkadiyenDbContext context,
        Guid revisionId)
    {
        context.ChangeTracker.Clear();
        ScheduleDiffService service = new(
            new ScheduleDiffStore(context),
            new SemanticScheduleDiffer(new SemanticDiffOptions()),
            new ScheduleDiffSafetyThresholds(),
            new ScheduleDiffScenario.FixedClock(Now));

        ScheduleDiffCalculationResult? result = await service.CalculateAsync(revisionId, Token);
        Assert.NotNull(result);
        context.ChangeTracker.Clear();
        return result.Diff;
    }

    private static async Task<Guid> RecordIdAsync(SirkadiyenDbContext context, Guid revisionId) =>
        await context.CanonicalScheduleRecords
            .Where(record => record.ScheduleRevisionId == revisionId)
            .Select(record => record.Id)
            .FirstAsync(Token);
}
