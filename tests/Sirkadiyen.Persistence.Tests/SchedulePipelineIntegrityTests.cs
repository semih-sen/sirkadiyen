using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Checks the guarantees the schema itself has to make, because application
/// code alone cannot hold them under concurrency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchedulePipelineIntegrityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ASourceMayNotBeConfiguredTwice()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-DUPLICATE-SOURCE");
        await AddSourceAsync(sourceId);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddSourceAsync(sourceId));
    }

    [Fact]
    public async Task OnlyOneRevisionPerSourceMayBePublished()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-ONE-PUBLISHED");
        Guid scheduleSourceId = await AddSourceAsync(sourceId);

        await PublishRevisionAsync(scheduleSourceId, sourceId, "snapshot-a");

        // A second live revision would leave two schedules both claiming to be
        // current, and calendar synchronization would follow whichever it read
        // first.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => PublishRevisionAsync(scheduleSourceId, sourceId, "snapshot-b"));
    }

    [Fact]
    public async Task ARevisionMayNotHoldOneLogicalLessonTwice()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-DUPLICATE-LESSON");
        Guid scheduleSourceId = await AddSourceAsync(sourceId);
        Guid revisionId = await PublishRevisionAsync(scheduleSourceId, sourceId, "snapshot-lesson");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        context.CanonicalScheduleRecords.Add(RecordFor(revisionId, sourceId, "sha256:identity-1"));
        context.CanonicalScheduleRecords.Add(RecordFor(revisionId, sourceId, "sha256:identity-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task ARevisionCannotBePublishedWithoutBeingValidated()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        ScheduleRevision revision = new(
            Guid.CreateVersion7(),
            SourceId.Parse("G9-STATES"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => revision.TransitionTo(RevisionState.Published, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ALessonThatEndsBeforeItStartsIsRejectedByTheDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-TIME-ORDER");
        Guid scheduleSourceId = await AddSourceAsync(sourceId);
        Guid revisionId = await PublishRevisionAsync(scheduleSourceId, sourceId, "snapshot-time");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        CanonicalScheduleRecord record = RecordFor(revisionId, sourceId, "sha256:time-order");

        // The domain constructor refuses this, so the value is forced past it to
        // prove the database is a second line of defence.
        context.CanonicalScheduleRecords.Add(record);
        context.Entry(record).Property(entity => entity.EndLocalTime).CurrentValue =
            new TimeOnly(8, 0);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<Guid> AddSourceAsync(SourceId sourceId)
    {
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = new(
            sourceId,
            $"Test source {sourceId}",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_yearly_v1",
            "1.0.0",
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "Europe/Istanbul");

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);
        return source.Id;
    }

    private async Task<Guid> PublishRevisionAsync(
        Guid scheduleSourceId,
        SourceId sourceId,
        string snapshotDiscriminator)
    {
        await using SirkadiyenDbContext context = fixture.CreateContext();
        DateTimeOffset now = new(2026, 7, 21, 9, 0, 0, TimeSpan.Zero);

        SourceSnapshot snapshot = new(
            scheduleSourceId,
            sourceId,
            snapshotDiscriminator,
            "spreadsheet-001",
            now,
            $"sha256:{snapshotDiscriminator}",
            "1.0",
            "{}",
            1,
            1,
            0);
        context.SourceSnapshots.Add(snapshot);

        ParseRun run = new(snapshot.Id, "grade1_yearly_v1", "1.0.0", "correlation-1", now);
        run.Complete(ParseRunStatus.Completed, now, "{}", 1, 0, 0);
        context.ParseRuns.Add(run);

        ScheduleRevision revision = new(scheduleSourceId, sourceId, run.Id, now);
        revision.TransitionTo(RevisionState.Validating, now);
        revision.TransitionTo(RevisionState.Validated, now);
        revision.TransitionTo(RevisionState.Published, now);
        context.ScheduleRevisions.Add(revision);

        await context.SaveChangesAsync(Token);
        return revision.Id;
    }

    private static CanonicalScheduleRecord RecordFor(
        Guid revisionId,
        SourceId sourceId,
        string stableIdentity) => new(
            revisionId,
            sourceId,
            $"candidate-{stableIdentity}",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            ScheduleEventType.Theory,
            AudienceScope.AllStudentsInProgram,
            "[]",
            "Hücre zarı",
            "hucre-zari",
            new DateOnly(2025, 10, 1),
            new TimeOnly(9, 0),
            new TimeOnly(9, 45),
            "Europe/Istanbul",
            stableIdentity,
            "sha256:content",
            1.0m,
            "[]");
}
