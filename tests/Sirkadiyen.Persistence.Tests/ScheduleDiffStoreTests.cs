using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision revision = await PublishAsync(context, source, Now, ["a", "b", "c"]);

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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision first = await PublishAsync(context, source, Now, ["a", "b", "c"]);

        // "a" is untouched, "b" changes room, "c" disappears and "d" is new.
        ScheduleRevision second = await PublishAsync(
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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision revision = await PublishAsync(context, source, Now, ["a"]);

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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision revision = await PublishAsync(context, source, Now, ["a"]);

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
    public async Task AMassDeletionHoldsTheDiffInsteadOfEmptyingCalendars()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await AddSourceAsync(context);
        string[] full = [.. Enumerable.Range(0, 40).Select(index => $"lesson-{index:D2}")];

        await PublishAsync(context, source, Now, full);
        ScheduleRevision second = await PublishAsync(context, source, Now.AddHours(1), full[..20]);

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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision first = await PublishAsync(context, source, Now, ["a"]);
        ScheduleRevision second = await PublishAsync(context, source, Now.AddHours(1), ["a", "b"]);

        context.ChangeTracker.Clear();
        ScheduleDiffStore store = new(context);
        IReadOnlyList<Guid> pending = await store.ListPendingDiffAsync(500, Token);

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
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision candidate = await AddRevisionAsync(context, source, Now, ["a"]);

        ScheduleDiffStore store = new(context);

        Assert.Null(await store.LoadAsync(candidate.Id, Token));
        Assert.DoesNotContain(candidate.Id, await store.ListPendingDiffAsync(500, Token));
        Assert.Null(await Service(context).CalculateAsync(candidate.Id, Token));
    }

    [Fact]
    public async Task DiffCalculationWorksUnderTheHostsRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision revision = await PublishAsync(context, source, Now, ["a", "b"]);

        ScheduleDiffCalculationResult result = await AssertCalculatedAsync(context, revision.Id);

        Assert.Equal(ScheduleDiffPersistenceOutcome.Stored, result.Outcome);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ScheduleDiffService Service(SirkadiyenDbContext context) => new(
        new ScheduleDiffStore(context),
        new SemanticScheduleDiffer(new SemanticDiffOptions()),
        new ScheduleDiffSafetyThresholds(),
        new FixedClock(Now));

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

    private static async Task<ScheduleSource> AddSourceAsync(SirkadiyenDbContext context)
    {
        ScheduleSource source = new(
            SourceId.Parse($"G1-DIFF-{Guid.NewGuid():N}"[..24]),
            "Diff test source",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_yearly_v1",
            "1.0.0",
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "spreadsheet-1",
            1);

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);
        return source;
    }

    /// <summary>
    /// Adds a validated revision holding one record per stable identity.
    /// </summary>
    /// <param name="changedContentIdentities">
    /// Identities whose content hash differs from the default, which is how a
    /// room or instructor change reaches the diff as an update.
    /// </param>
    private static async Task<ScheduleRevision> AddRevisionAsync(
        SirkadiyenDbContext context,
        ScheduleSource source,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> identities,
        IReadOnlyCollection<string>? changedContentIdentities = null)
    {
        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            $"snapshot-{Guid.NewGuid():N}",
            "spreadsheet-1",
            createdAtUtc,
            $"sha256:{Guid.NewGuid():N}",
            "1.0",
            "{}",
            1,
            identities.Count,
            0);

        ParseRun run = new(
            snapshot.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            $"c-{Guid.NewGuid():N}",
            createdAtUtc);

        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, createdAtUtc);
        revision.SetRecordCount(identities.Count);
        revision.TransitionTo(RevisionState.Validating, createdAtUtc);
        revision.TransitionTo(RevisionState.Validated, createdAtUtc, "All validation rules passed.");

        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        context.ScheduleRevisions.Add(revision);

        foreach (string identity in identities)
        {
            context.CanonicalScheduleRecords.Add(Materialize(
                revision.Id,
                source.SourceId,
                identity,
                changedContentIdentities?.Contains(identity) == true
                    ? $"sha256:changed-{identity}"
                    : $"sha256:{identity}"));
        }

        await context.SaveChangesAsync(Token);
        return revision;
    }

    private static async Task<ScheduleRevision> PublishAsync(
        SirkadiyenDbContext context,
        ScheduleSource source,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> identities,
        IReadOnlyCollection<string>? changedContentIdentities = null)
    {
        ScheduleRevision revision = await AddRevisionAsync(
            context,
            source,
            createdAtUtc,
            identities,
            changedContentIdentities);

        context.ChangeTracker.Clear();
        RevisionPublicationResult result = await new ScheduleRevisionPublicationStore(context)
            .PublishAsync(revision.Id, createdAtUtc, Token);
        Assert.Equal(RevisionPublicationOutcome.Published, result.Outcome);

        context.ChangeTracker.Clear();
        return revision;
    }

    private static CanonicalScheduleRecord Materialize(
        Guid revisionId,
        SourceId sourceId,
        string identity,
        string contentHash)
    {
        IReadOnlyList<AudienceSelector> audience =
        [
            new AudienceSelector { Dimension = "practiceGroup", Value = "A" },
        ];

        return new CanonicalScheduleRecord(
            revisionId,
            sourceId,
            $"candidate-{identity}",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Theory,
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            $"Lesson {identity}",
            null,
            new DateOnly(2025, 10, 3),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            "Europe/Istanbul",
            identity,
            contentHash,
            1.0m,
            "[]");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
