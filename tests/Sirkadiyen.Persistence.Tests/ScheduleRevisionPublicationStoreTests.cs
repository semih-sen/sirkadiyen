using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
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

[Collection(PostgresCollection.Name)]
public sealed class ScheduleRevisionPublicationStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheFirstValidatedRevisionOfASourceIsPublished()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.Validated);

        ScheduleRevisionPublicationStore store = new(context);
        RevisionPublicationResult result = await store.PublishAsync(revision.Id, Now, Token);

        Assert.Equal(RevisionPublicationOutcome.Published, result.Outcome);
        Assert.Null(result.SupersededRevisionId);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await ReadAsync(context, revision.Id);
        Assert.Equal(RevisionState.Published, stored.State);
        Assert.Equal(Now, stored.PublishedAtUtc);
        Assert.Equal(source.Id, stored.ScheduleSourceId);
    }

    [Fact]
    public async Task PublishingSupersedesThePreviousRevisionInTheSameTransaction()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, ScheduleRevision first) =
            await AddRevisionAsync(context, RevisionState.Validated);

        ScheduleRevisionPublicationStore store = new(context);
        await store.PublishAsync(first.Id, Now, Token);

        context.ChangeTracker.Clear();
        ScheduleRevision second = await AddRevisionToSourceAsync(
            context,
            source,
            RevisionState.Validated,
            Now.AddHours(1));

        RevisionPublicationResult result = await store.PublishAsync(
            second.Id,
            Now.AddHours(1),
            Token);

        Assert.Equal(RevisionPublicationOutcome.Published, result.Outcome);
        Assert.Equal(first.Id, result.SupersededRevisionId);

        context.ChangeTracker.Clear();
        ScheduleRevision storedFirst = await ReadAsync(context, first.Id);
        ScheduleRevision storedSecond = await ReadAsync(context, second.Id);

        Assert.Equal(RevisionState.Superseded, storedFirst.State);
        Assert.Equal(second.Id, storedFirst.SupersededByRevisionId);
        Assert.Equal(RevisionState.Published, storedSecond.State);

        // The schema allows exactly one published revision per source; this is the
        // assertion that the swap actually left one behind rather than none.
        Assert.Single(await context.ScheduleRevisions
            .Where(candidate => candidate.ScheduleSourceId == source.Id
                && candidate.State == RevisionState.Published)
            .ToListAsync(Token));
    }

    [Fact]
    public async Task PublicationWorksUnderTheHostsRetryingExecutionStrategy()
    {
        // The worker and the API enable retry on failure, and a retrying strategy
        // rejects a transaction it did not open itself. Without the execution
        // strategy inside the store this test throws where the plain context
        // passes, which is exactly the production-only failure worth catching.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        (_, ScheduleRevision revision) = await AddRevisionAsync(context, RevisionState.Validated);

        ScheduleRevisionPublicationStore store = new(context);
        RevisionPublicationResult result = await store.PublishAsync(revision.Id, Now, Token);

        Assert.Equal(RevisionPublicationOutcome.Published, result.Outcome);
    }

    [Fact]
    public async Task AQuarantinedRevisionCannotBePublishedWithoutApproval()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.ReviewRequired);

        ScheduleRevisionPublicationStore store = new(context);
        RevisionPublicationResult result = await store.PublishAsync(revision.Id, Now, Token);

        Assert.Equal(RevisionPublicationOutcome.NotValidated, result.Outcome);
        Assert.Equal(RevisionState.ReviewRequired, result.ObservedState);

        context.ChangeTracker.Clear();
        Assert.Equal(RevisionState.ReviewRequired, (await ReadAsync(context, revision.Id)).State);
    }

    [Fact]
    public async Task ApprovalRecordsWhoDecidedAndWhy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.ReviewRequired);

        ScheduleRevisionPublicationStore store = new(context);
        RevisionApprovalResult approval = await store.ApproveAsync(
            revision.Id,
            "semih",
            "Checked the source; the drop is the exam period.",
            Now,
            Token);

        Assert.Equal(RevisionApprovalOutcome.Approved, approval.Outcome);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await ReadAsync(context, revision.Id);
        Assert.Equal(RevisionState.Validated, stored.State);
        Assert.Equal("semih", stored.ApprovedBy);
        Assert.Equal("Checked the source; the drop is the exam period.", stored.ApprovalReason);
        Assert.Equal(Now, stored.ApprovedAtUtc);

        // The reason the revision was held survives the approval, so one row still
        // says what was overridden.
        Assert.Contains("held for review", stored.StateReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnApprovedRevisionPublishesThroughTheOrdinaryPath()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.ReviewRequired);

        ScheduleRevisionPublicationStore store = new(context);
        ScheduleRevisionPublicationService service = new(store, new FixedClock(Now));

        RevisionApprovalOutcomeResult result = await service.ApproveAndPublishAsync(
            revision.Id,
            "semih",
            "Reviewed the overlaps and they are real.",
            Token);

        Assert.Equal(RevisionApprovalOutcome.Approved, result.Approval.Outcome);
        Assert.NotNull(result.Publication);
        Assert.Equal(RevisionPublicationOutcome.Published, result.Publication.Outcome);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await ReadAsync(context, revision.Id);
        Assert.Equal(RevisionState.Published, stored.State);
        Assert.Equal("semih", stored.ApprovedBy);
    }

    [Fact]
    public async Task AlreadyValidatedRevisionsCannotBeApproved()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.Validated);

        ScheduleRevisionPublicationStore store = new(context);
        RevisionApprovalResult approval = await store.ApproveAsync(
            revision.Id,
            "semih",
            "Nothing to override.",
            Now,
            Token);

        Assert.Equal(RevisionApprovalOutcome.NotAwaitingReview, approval.Outcome);
        Assert.Equal(RevisionState.Validated, approval.ObservedState);

        // An approval that changed nothing must not leave an audit trail claiming
        // it did.
        context.ChangeTracker.Clear();
        Assert.Null((await ReadAsync(context, revision.Id)).ApprovedBy);
    }

    [Fact]
    public async Task AnOlderRevisionCannotReplaceANewerPublishedOne()
    {
        // Publishing backwards would make the source's live schedule regress, and
        // the semantic diff would read that as a mass deletion.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, ScheduleRevision older) =
            await AddRevisionAsync(context, RevisionState.Validated);

        ScheduleRevision newer = await AddRevisionToSourceAsync(
            context,
            source,
            RevisionState.Validated,
            Now.AddHours(2));

        ScheduleRevisionPublicationStore store = new(context);
        await store.PublishAsync(newer.Id, Now.AddHours(2), Token);

        context.ChangeTracker.Clear();
        RevisionPublicationResult result = await store.PublishAsync(
            older.Id,
            Now.AddHours(3),
            Token);

        Assert.Equal(RevisionPublicationOutcome.SupersededByNewerRevision, result.Outcome);
        Assert.Equal(newer.Id, result.SupersededRevisionId);

        context.ChangeTracker.Clear();
        Assert.Equal(RevisionState.Published, (await ReadAsync(context, newer.Id)).State);
        Assert.Equal(RevisionState.Validated, (await ReadAsync(context, older.Id)).State);
    }

    [Fact]
    public async Task OnlyValidatedRevisionsAreQueuedForPublication()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision validated) =
            await AddRevisionAsync(context, RevisionState.Validated);
        (_, ScheduleRevision quarantined) =
            await AddRevisionAsync(context, RevisionState.ReviewRequired);
        (_, ScheduleRevision parsed) = await AddRevisionAsync(context, RevisionState.Parsed);

        ScheduleRevisionPublicationStore store = new(context);
        IReadOnlyList<Guid> publishable = await store.ListPublishableAsync(500, Token);

        Assert.Contains(validated.Id, publishable);
        Assert.DoesNotContain(quarantined.Id, publishable);
        Assert.DoesNotContain(parsed.Id, publishable);
    }

    [Fact]
    public async Task AMissingRevisionIsReportedRatherThanThrown()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleRevisionPublicationStore store = new(context);

        RevisionPublicationResult publication = await store.PublishAsync(
            Guid.CreateVersion7(),
            Now,
            Token);
        RevisionApprovalResult approval = await store.ApproveAsync(
            Guid.CreateVersion7(),
            "semih",
            "n/a",
            Now,
            Token);

        Assert.Equal(RevisionPublicationOutcome.RevisionNotFound, publication.Outcome);
        Assert.Equal(RevisionApprovalOutcome.RevisionNotFound, approval.Outcome);
    }

    [Fact]
    public async Task TheReviewQueueCarriesTheFindingsThatHeldEachRevision()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) =
            await AddRevisionAsync(context, RevisionState.ReviewRequired);

        context.RevisionValidationFindings.Add(new RevisionValidationFinding(
            revision.Id,
            RevisionValidationRule.MassDeletion,
            ValidationSeverity.Error,
            "40% of the published records are absent.",
            Now,
            detail: "[\"identity-0\"]",
            affectedRecordCount: 4));
        await context.SaveChangesAsync(Token);

        context.ChangeTracker.Clear();
        ScheduleRevisionReadStore store = new(context);

        ScheduleRevisionDetail? detail = await store.FindAsync(revision.Id, Token);
        Assert.NotNull(detail);
        RevisionFindingView finding = Assert.Single(detail.Findings);
        Assert.Equal(RevisionValidationRule.MassDeletion, finding.Rule);
        Assert.Equal(4, finding.AffectedRecordCount);

        IReadOnlyList<ScheduleRevisionSummary> queue = await store.ListByStateAsync(
            RevisionState.ReviewRequired,
            500,
            Token);
        Assert.Contains(queue, summary => summary.RevisionId == revision.Id);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ScheduleRevision> ReadAsync(
        SirkadiyenDbContext context,
        Guid revisionId) =>
        await context.ScheduleRevisions.SingleAsync(
            candidate => candidate.Id == revisionId,
            Token);

    private static async Task<(ScheduleSource Source, ScheduleRevision Revision)> AddRevisionAsync(
        SirkadiyenDbContext context,
        RevisionState state)
    {
        SourceId sourceId = SourceId.Parse($"G1-PUB-{Guid.NewGuid():N}"[..24]);
        ScheduleSource source = new(
            sourceId,
            "Publication test source",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_practice_v1",
            "1.0.0",
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "spreadsheet-1",
            1);

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);

        ScheduleRevision revision = await AddRevisionToSourceAsync(context, source, state, Now);
        return (source, revision);
    }

    private static async Task<ScheduleRevision> AddRevisionToSourceAsync(
        SirkadiyenDbContext context,
        ScheduleSource source,
        RevisionState state,
        DateTimeOffset createdAtUtc)
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
            1,
            0);

        ParseRun run = new(
            snapshot.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            $"c-{Guid.NewGuid():N}",
            createdAtUtc);

        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, createdAtUtc);
        revision.SetRecordCount(1);
        MoveTo(revision, state, createdAtUtc);

        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        context.ScheduleRevisions.Add(revision);
        context.CanonicalScheduleRecords.Add(Materialize(revision.Id, source.SourceId, createdAtUtc));

        await context.SaveChangesAsync(Token);
        return revision;
    }

    /// <summary>
    /// Walks a fresh revision to the state a test needs through the real
    /// transitions, so a fixture can never set up a state the machine forbids.
    /// </summary>
    private static void MoveTo(ScheduleRevision revision, RevisionState state, DateTimeOffset atUtc)
    {
        if (state is RevisionState.Parsed)
        {
            return;
        }

        revision.TransitionTo(RevisionState.Validating, atUtc);
        revision.TransitionTo(
            state is RevisionState.ReviewRequired
                ? RevisionState.ReviewRequired
                : RevisionState.Validated,
            atUtc,
            state is RevisionState.ReviewRequired
                ? "Held for review: MassDeletion"
                : "All validation rules passed.");
    }

    private static CanonicalScheduleRecord Materialize(
        Guid revisionId,
        SourceId sourceId,
        DateTimeOffset createdAtUtc)
    {
        IReadOnlyList<AudienceSelector> audience =
        [
            new AudienceSelector { Dimension = "practiceGroup", Value = "A" },
        ];

        return new CanonicalScheduleRecord(
            revisionId,
            sourceId,
            "candidate-0",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Practice,
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            "Lesson 0",
            null,
            DateOnly.FromDateTime(createdAtUtc.UtcDateTime),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            "Europe/Istanbul",
            "identity-0",
            "sha256:content-0",
            1.0m,
            "[]");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
