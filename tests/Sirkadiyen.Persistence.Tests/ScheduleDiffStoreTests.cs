using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Proves that publishing a revision leaves a stored, single, correct record of
/// what it changed — including after the crash and race paths.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScheduleDiffStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheFirstPublishedRevisionOfASourceCreatesEverything()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a", "b", "c"]);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, revision.Id);

        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, result.Outcome);
        Assert.Null(result.Diff.PreviousRevisionId);
        Assert.Equal(3, result.Diff.CreatedCount);
        Assert.Equal(ScheduleDiffState.Ready, result.Diff.State);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await ReadDiffAsync(context, revision.Id);
        Assert.Equal(3, stored.Entries.Count);
        Assert.All(
            stored.Entries,
            entry => Assert.Equal(ScheduleDiffChange.Created, entry.Change));
        Assert.All(stored.Entries, entry => Assert.Null(entry.PreviousRecordId));
    }

    [Fact]
    public async Task ASecondRevisionIsDiffedAgainstTheOneItSuperseded()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision first = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a", "b", "c"]);

        // "a" is untouched, "b" changes room, "c" disappears and "d" is new.
        ScheduleRevision second = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now.AddHours(1),
            ["a", "b", "d"],
            changedContentIdentities: ["b"]);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, second.Id);

        Assert.Equal(first.Id, result.Diff.PreviousRevisionId);
        Assert.Equal(1, result.Diff.UnchangedCount);
        Assert.Equal(1, result.Diff.UpdatedCount);
        Assert.Equal(1, result.Diff.DeletedCount);
        Assert.Equal(1, result.Diff.CreatedCount);
        Assert.Equal(0, result.Diff.AmbiguousCount);

        // Three deletions out of three would be a mass deletion; one out of three
        // is under the minimum count, so this diff is safe to act on.
        Assert.Equal(ScheduleDiffState.Ready, result.Diff.State);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await ReadDiffAsync(context, second.Id);
        ScheduleDiffEntry updated = Assert.Single(
            stored.Entries,
            entry => entry.Change is ScheduleDiffChange.Updated);
        Assert.NotNull(updated.PreviousRecordId);
        Assert.NotNull(updated.CurrentRecordId);
        Assert.Equal(ScheduleDiffMatch.ExactStableIdentity, updated.Match);
    }

    [Fact]
    public async Task ARevisionIsDiffedOnlyOnce()
    {
        // Calculation is retried after a crash, and a second stored diff would
        // mean a second set of calendar operations for the same change.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        await AssertCalculatedAsync(context, revision.Id);

        context.ChangeTracker.Clear();
        Assert.Null(await Service(context).CalculateAsync(revision.Id, Token));

        context.ChangeTracker.Clear();
        Assert.Single(await context.ScheduleDiffs
            .Where(diff => diff.CurrentRevisionId == revision.Id)
            .ToListAsync(Token));
    }

    [Fact]
    public async Task ARaceThatLosesReportsTheExistingDiffRatherThanWritingASecond()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);
        ScheduleDiffInput input = (await store.LoadAsync(revision.Id, Token))!;

        // Both passes read the same two immutable revisions, so both build the
        // same diff; only one of them may reach the table.
        ScheduleDiffPersistenceResult first = await store.SaveAsync(Build(input), Token);
        ScheduleDiffPersistenceResult second = await store.SaveAsync(Build(input), Token);

        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, first.Outcome);
        Assert.Equal(ScheduleDiffPersistenceOutcome.AlreadyCalculated, second.Outcome);
        Assert.Equal(first.ScheduleDiffId, second.ScheduleDiffId);
    }

    [Fact]
    public async Task AnAmbiguousCandidateSetIsStoredRatherThanRejectedByTheEntryTable()
    {
        // The differ's ambiguity contract and the entry table's uniqueness rule
        // meet here, and only here. A set that named the same record on two
        // entries could not be inserted at all: the revision stayed pending and
        // the worker recalculated it forever, so this is a regression test for
        // the whole loop, not only for the differ's output shape.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);

        // Two lessons the identity pass cannot carry over, and one new lesson
        // that both of them look like: many-to-one, which is the shape that
        // reached production.
        await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now,
            ["old-one", "old-two"],
            secondaryMatchable: true);
        ScheduleRevision second = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now.AddHours(1),
            ["new-one"],
            secondaryMatchable: true);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, second.Id);

        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, result.Outcome);
        Assert.Equal(3, result.Diff.AmbiguousCount);
        Assert.Equal(0, result.Diff.DeletedCount);
        Assert.Equal(0, result.Diff.CreatedCount);

        // Ambiguity holds the diff; nothing here may reach a calendar.
        Assert.Equal(ScheduleDiffState.Held, result.Diff.State);
        Assert.False(result.Diff.IsDispatchable);
        Assert.False(result.Diff.IsReleasable);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await ReadDiffAsync(context, second.Id);
        Assert.Equal(3, stored.Entries.Count);
        Assert.All(
            stored.Entries,
            entry => Assert.Equal(ScheduleDiffChange.Ambiguous, entry.Change));

        // Every record involved is classified, and none of them twice.
        Assert.Equal(2, stored.Entries.Count(entry => entry.PreviousRecordId is not null));
        Assert.Single(stored.Entries, entry => entry.CurrentRecordId is not null);
        Assert.DoesNotContain(
            stored.Entries,
            entry => entry.PreviousRecordId is not null && entry.CurrentRecordId is not null);

        // The revision is no longer pending, which is what ends the retry loop.
        context.ChangeTracker.Clear();
        Assert.DoesNotContain(
            second.Id,
            await new ScheduleDiffStore(context).ListPendingDiffAsync(500, Now.AddDays(1), Token));
    }

    [Fact]
    public async Task AnEntryContradictionIsReportedRatherThanReadAsAnEarlierPass()
    {
        // AlreadyCalculated means another pass won the race for this revision.
        // A unique violation on the entries says something else entirely — the
        // diff contradicts itself — and reporting that as success left a real
        // failure invisible while the worker retried it on every cycle.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);
        ScheduleDiffInput input = (await store.LoadAsync(revision.Id, Token))!;
        Guid recordId = input.CurrentRecords[0].Id;

        ScheduleDiff diff = ScheduleDiff.Create(
            input.ScheduleSourceId,
            input.SourceId,
            input.PreviousRevisionId,
            input.CurrentRevisionId,
            [
                new ScheduleDiffEntry
                {
                    Change = ScheduleDiffChange.Created,
                    Match = ScheduleDiffMatch.None,
                    CurrentRecordId = recordId,
                },
                new ScheduleDiffEntry
                {
                    Change = ScheduleDiffChange.Ambiguous,
                    Match = ScheduleDiffMatch.SecondaryAttributes,
                    CurrentRecordId = recordId,
                },
            ],
            new ScheduleDiffSafetyThresholds(),
            Now);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(diff, Token));

        // And the revision stays pending, so the failure is visible as one.
        context.ChangeTracker.Clear();
        Assert.Contains(revision.Id, await new ScheduleDiffStore(context).ListPendingDiffAsync(500, Now.AddDays(1), Token));
    }

    [Fact]
    public async Task AFailedCalculationIsDeferredInsteadOfBeingDueAgainImmediately()
    {
        // "Pending" is derived — published, no diff row — so a failed attempt used to leave the
        // revision due again on the very next cycle. That is why a permanent fault burned a worker
        // cycle every six seconds for three weeks (ADR-164).
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);
        Assert.Contains(revision.Id, await store.ListPendingDiffAsync(500, Now, Token));

        RevisionDiffState? state = await store.RecordDiffCalculationFailureAsync(
            revision.Id,
            "duplicate key value violates unique constraint",
            TimeSpan.FromMinutes(1),
            maxAttempts: 6,
            Now,
            Token);

        Assert.Equal(RevisionDiffState.Pending, state);

        context.ChangeTracker.Clear();
        store = new ScheduleDiffStore(context);

        // Not due yet at thirty seconds, due again at two minutes.
        Assert.DoesNotContain(
            revision.Id,
            await store.ListPendingDiffAsync(500, Now.AddSeconds(30), Token));
        Assert.Contains(
            revision.Id,
            await store.ListPendingDiffAsync(500, Now.AddMinutes(2), Token));
    }

    [Fact]
    public async Task ARevisionThatKeepsFailingStopsBeingQueuedAtAll()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);
        RevisionDiffState? state = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            state = await store.RecordDiffCalculationFailureAsync(
                revision.Id,
                "still broken",
                TimeSpan.FromMinutes(1),
                maxAttempts: 3,
                Now,
                Token);
        }

        Assert.Equal(RevisionDiffState.Failed, state);

        context.ChangeTracker.Clear();
        store = new ScheduleDiffStore(context);

        // However far in the future the worker looks, it is no longer offered.
        Assert.DoesNotContain(revision.Id, await store.ListPendingDiffAsync(500, Now.AddYears(1), Token));

        // Recording another failure against it is refused rather than silently counted.
        Assert.Null(await store.RecordDiffCalculationFailureAsync(
            revision.Id,
            "again",
            TimeSpan.FromMinutes(1),
            maxAttempts: 3,
            Now,
            Token));

        // And it is still published: giving up on the diff is not an unpublication.
        context.ChangeTracker.Clear();
        ScheduleRevision stored = await context.ScheduleRevisions
            .SingleAsync(candidate => candidate.Id == revision.Id, Token);
        Assert.Equal(RevisionState.Published, stored.State);
        Assert.Equal(RevisionDiffState.Failed, stored.DiffState);
        Assert.Equal(3, stored.DiffAttempts);
        Assert.Equal("still broken", stored.DiffFailureReason);
    }

    [Fact]
    public async Task AnOperatorCanReturnAGivenUpRevisionToTheQueue()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);
        await store.RecordDiffCalculationFailureAsync(
            revision.Id,
            "broken",
            TimeSpan.FromMinutes(1),
            maxAttempts: 1,
            Now,
            Token);

        context.ChangeTracker.Clear();
        store = new ScheduleDiffStore(context);

        Assert.Equal(
            RevisionDiffRetryOutcome.Queued,
            await store.RetryDiffCalculationAsync(
                revision.Id,
                "semih",
                "The differ fix is deployed.",
                Now.AddHours(2),
                Token));

        context.ChangeTracker.Clear();
        store = new ScheduleDiffStore(context);
        Assert.Contains(revision.Id, await store.ListPendingDiffAsync(500, Now.AddHours(2), Token));

        // A revision already queued has nothing to retry, and one that does not exist says so.
        Assert.Equal(
            RevisionDiffRetryOutcome.NotRetriable,
            await store.RetryDiffCalculationAsync(revision.Id, "semih", "Again.", Now, Token));
        Assert.Equal(
            RevisionDiffRetryOutcome.NotFound,
            await store.RetryDiffCalculationAsync(
                Guid.CreateVersion7(),
                "semih",
                "Nothing here.",
                Now,
                Token));
    }

    [Fact]
    public async Task AMassDeletionHoldsTheDiffInsteadOfEmptyingCalendars()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        string[] full = [.. Enumerable.Range(0, 40).Select(index => $"lesson-{index:D2}")];

        await ScheduleDiffScenario.PublishAsync(context, source, Now, full);
        ScheduleRevision second = await ScheduleDiffScenario.PublishAsync(context, source, Now.AddHours(1), full[..20]);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, second.Id);

        Assert.Equal(20, result.Diff.DeletedCount);
        Assert.Equal(ScheduleDiffState.Held, result.Diff.State);
        Assert.False(result.Diff.IsDispatchable);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await ReadDiffAsync(context, second.Id);
        Assert.Equal(ScheduleDiffState.Held, stored.State);
        Assert.Contains("20 of 40", stored.HoldReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARevisionSupersededBeforeItWasDiffedIsStillDiffed()
    {
        // The worker may be killed between publication and diffing, and a third
        // revision may go live before it restarts. Skipping the middle revision
        // would lose everything it changed.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision first = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a"]);
        ScheduleRevision second = await ScheduleDiffScenario.PublishAsync(context, source, Now.AddHours(1), ["a", "b"]);

        context.ChangeTracker.Clear();
        ScheduleDiffStore store = new(context);
        IReadOnlyList<Guid> pending = await store.ListPendingDiffAsync(500, Now.AddDays(1), Token);

        Assert.Contains(first.Id, pending);
        Assert.Contains(second.Id, pending);

        // Oldest first, so a consumer replays the changes in the order students
        // would have received them.
        Assert.True(pending.ToList().IndexOf(first.Id) < pending.ToList().IndexOf(second.Id));
    }

    [Fact]
    public async Task AnUnpublishedRevisionIsNeverDiffed()
    {
        // Deletion requires a published revision. Diffing a candidate would let a
        // revision nobody approved decide what disappears from a calendar.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision candidate = await ScheduleDiffScenario.AddRevisionAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);

        Assert.Null(await store.LoadAsync(candidate.Id, Token));
        Assert.DoesNotContain(candidate.Id, await store.ListPendingDiffAsync(500, Now.AddDays(1), Token));
        Assert.Null(await Service(context).CalculateAsync(candidate.Id, Token));
    }

    [Fact]
    public async Task DiffCalculationWorksUnderTheHostsRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, ["a", "b"]);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, revision.Id);

        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, result.Outcome);
    }

    [Fact]
    public async Task AReadyDiffIsListedForDispatchAndLoadsItsEntries()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        Guid diffId = await CreateReadyDiffAsync(context, ["a", "b", "c"]);

        ScheduleDiffStore store = new(context);
        context.ChangeTracker.Clear();

        IReadOnlyList<Guid> pending = await store.ListPendingDispatchAsync(50, Now.AddDays(1), Token);
        Assert.Contains(diffId, pending);

        DispatchableDiff? loaded = await store.LoadForDispatchAsync(diffId, Token);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Entries.Count);
        Assert.All(loaded.Entries, entry => Assert.Equal(ScheduleDiffChange.Created, entry.Change));
    }

    [Fact]
    public async Task AHeldDiffIsNeverListedOrLoadedForDispatch()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        string[] full = [.. Enumerable.Range(0, 40).Select(index => $"lesson-{index:D2}")];
        await ScheduleDiffScenario.PublishAsync(context, source, Now, full);
        ScheduleRevision second =
            await ScheduleDiffScenario.PublishAsync(context, source, Now.AddHours(1), full[..20]);
        await AssertCalculatedAsync(context, second.Id);

        context.ChangeTracker.Clear();
        ScheduleDiff held = await ReadDiffAsync(context, second.Id);
        Assert.Equal(ScheduleDiffState.Held, held.State);

        ScheduleDiffStore store = new(context);
        Assert.DoesNotContain(held.Id, await store.ListPendingDispatchAsync(50, Now.AddDays(1), Token));
        Assert.Null(await store.LoadForDispatchAsync(held.Id, Token));
    }

    [Fact]
    public async Task MarkingADiffDispatchedRemovesItFromThePendingList()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        Guid diffId = await CreateReadyDiffAsync(context, ["a"]);

        ScheduleDiffStore store = new(context);
        context.ChangeTracker.Clear();
        await store.MarkDispatchedAsync(diffId, Now.AddMinutes(5), Token);

        context.ChangeTracker.Clear();
        Assert.DoesNotContain(diffId, await store.ListPendingDispatchAsync(50, Now.AddDays(1), Token));
        ScheduleDiff stored = await context.ScheduleDiffs.SingleAsync(diff => diff.Id == diffId, Token);
        Assert.Equal(CalendarDispatchState.Dispatched, stored.CalendarDispatchState);
        Assert.Equal(Now.AddMinutes(5), stored.DispatchedAtUtc);
    }

    [Fact]
    public async Task ATransientFailureDefersWithABackOffThenGivesUp()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        Guid diffId = await CreateReadyDiffAsync(context, ["a"]);

        ScheduleDiffStore store = new(context);
        context.ChangeTracker.Clear();

        CalendarDispatchState afterFirst = await store.RecordDispatchFailureAsync(
            diffId,
            "rate limited",
            TimeSpan.FromSeconds(30),
            maxAttempts: 2,
            Now,
            Token);
        Assert.Equal(CalendarDispatchState.Pending, afterFirst);

        // Deferred: not due at Now, due once the back-off has passed.
        context.ChangeTracker.Clear();
        Assert.DoesNotContain(diffId, await store.ListPendingDispatchAsync(50, Now, Token));
        Assert.Contains(diffId, await store.ListPendingDispatchAsync(50, Now.AddMinutes(1), Token));

        context.ChangeTracker.Clear();
        CalendarDispatchState afterSecond = await store.RecordDispatchFailureAsync(
            diffId,
            "rate limited",
            TimeSpan.FromSeconds(30),
            maxAttempts: 2,
            Now,
            Token);
        Assert.Equal(CalendarDispatchState.Failed, afterSecond);

        context.ChangeTracker.Clear();
        Assert.DoesNotContain(diffId, await store.ListPendingDispatchAsync(50, Now.AddDays(1), Token));
    }

    [Fact]
    public async Task ReconciliationListsOnlyDispatchedDiffsAfterTheOrderedCursor()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        Guid first = await CreateReadyDiffAsync(context, ["first"]);
        Guid sameTimeA = await CreateReadyDiffAsync(context, ["second"]);
        Guid sameTimeB = await CreateReadyDiffAsync(context, ["third"]);
        _ = await CreateReadyDiffAsync(context, ["still-pending"]);

        DateTimeOffset firstAt = Now.AddMinutes(1);
        DateTimeOffset sameAt = Now.AddMinutes(2);
        ScheduleDiffStore store = new(context);
        context.ChangeTracker.Clear();
        await store.MarkDispatchedAsync(first, firstAt, Token);
        context.ChangeTracker.Clear();
        await store.MarkDispatchedAsync(sameTimeA, sameAt, Token);
        context.ChangeTracker.Clear();
        await store.MarkDispatchedAsync(sameTimeB, sameAt, Token);
        context.ChangeTracker.Clear();

        IReadOnlyList<DispatchedDiff> replay = await store.ListDispatchedForReplayAsync(
            firstAt,
            first,
            50,
            Token);

        Guid[] expectedSameTime = [sameTimeA, sameTimeB];
        Array.Sort(expectedSameTime);
        Assert.Equal(expectedSameTime, replay.Select(diff => diff.DiffId));
        Assert.All(replay, diff => Assert.Equal(sameAt, diff.DispatchedAtUtc));
        Assert.All(replay, diff => Assert.NotEmpty(diff.Entries));

        IReadOnlyList<DispatchedDiff> afterFirstAtSameTimestamp =
            await store.ListDispatchedForReplayAsync(
                sameAt,
                expectedSameTime[0],
                50,
                Token);
        Assert.Equal(expectedSameTime[1], Assert.Single(afterFirstAtSameTimestamp).DiffId);
    }

    private async Task<Guid> CreateReadyDiffAsync(SirkadiyenDbContext context, string[] identities)
    {
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(context, source, Now, identities);
        await AssertCalculatedAsync(context, revision.Id);

        context.ChangeTracker.Clear();
        ScheduleDiff stored = await ReadDiffAsync(context, revision.Id);
        Assert.Equal(ScheduleDiffState.Ready, stored.State);
        return stored.Id;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ScheduleDiffService Service(SirkadiyenDbContext context) => new(
        new ScheduleDiffStore(context),
        new SemanticScheduleDiffer(new SemanticDiffOptions()),
        new ScheduleDiffSafetyThresholds(),
        new ScheduleDiffRetryOptions(),
        new ScheduleDiffScenario.FixedClock(Now));

    private static ScheduleDiff Build(ScheduleDiffInput input) => ScheduleDiff.Create(
        input.ScheduleSourceId,
        input.SourceId,
        input.PreviousRevisionId,
        input.CurrentRevisionId,
        new SemanticScheduleDiffer(new SemanticDiffOptions())
            .Diff(input.PreviousRecords, input.CurrentRecords),
        new ScheduleDiffSafetyThresholds(),
        Now);

    private static async Task<ScheduleDiffCalculationResult> AssertCalculatedAsync(
        SirkadiyenDbContext context,
        Guid revisionId)
    {
        context.ChangeTracker.Clear();
        ScheduleDiffCalculationResult? result = await Service(context)
            .CalculateAsync(revisionId, Token);
        Assert.NotNull(result);
        return result;
    }

    private static async Task<ScheduleDiff> ReadDiffAsync(
        SirkadiyenDbContext context,
        Guid revisionId) =>
        await context.ScheduleDiffs
            .Include(diff => diff.Entries)
            .SingleAsync(diff => diff.CurrentRevisionId == revisionId, Token);
}
