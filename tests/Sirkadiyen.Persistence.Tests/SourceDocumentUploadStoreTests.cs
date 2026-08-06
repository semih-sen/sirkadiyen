using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Covers the two pieces of storage administrative acquisition adds: which
/// sources one document serves, and the audit trail of who supplied it (ADR-080).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SourceDocumentUploadStoreTests(PostgresFixture fixture)
{
    private const string Group = "g9-shared-document";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SourcesSharingADocumentAreFoundTogetherInAStableOrder()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await GivenUploadSourceAsync("G9-SHARED-TR", ProgramLanguage.Turkish, Group);
        await GivenUploadSourceAsync("G9-SHARED-EN", ProgramLanguage.English, Group);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IScheduleSourceStore store = new ScheduleSourceStore(context);

        IReadOnlyList<ScheduleSource> fromTurkish = await store.ListSharingDocumentAsync(
            SourceId.Parse("G9-SHARED-TR"),
            Token);
        IReadOnlyList<ScheduleSource> fromEnglish = await store.ListSharingDocumentAsync(
            SourceId.Parse("G9-SHARED-EN"),
            Token);

        // Whichever member the administrator uploads to, the same set is served,
        // in the same order, so one upload cannot half-apply by accident.
        Assert.Equal(
            ["G9-SHARED-EN", "G9-SHARED-TR"],
            fromTurkish.Select(source => source.SourceId.Value));
        Assert.Equal(
            fromTurkish.Select(source => source.SourceId.Value),
            fromEnglish.Select(source => source.SourceId.Value));
    }

    [Fact]
    public async Task ASourceWithNoGroupIsItsOwnOnlyTarget()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await GivenUploadSourceAsync("G9-UNSHARED", ProgramLanguage.Turkish, sharedDocumentGroup: null);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IScheduleSourceStore store = new ScheduleSourceStore(context);

        IReadOnlyList<ScheduleSource> targets = await store.ListSharingDocumentAsync(
            SourceId.Parse("G9-UNSHARED"),
            Token);

        ScheduleSource single = Assert.Single(targets);
        Assert.Equal("G9-UNSHARED", single.SourceId.Value);
    }

    [Fact]
    public async Task AnUploadIsRecordedAgainstTheSnapshotItBecame()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        ScheduleSource source = await GivenUploadSourceAsync(
            "G9-UPLOAD-AUDIT",
            ProgramLanguage.Turkish,
            sharedDocumentGroup: null);
        SourceSnapshot snapshot = await GivenSnapshotAsync(source);

        await using (SirkadiyenDbContext writeContext = fixture.CreateContext())
        {
            ISourceDocumentUploadAuditStore auditStore = new SourceDocumentUploadAuditStore(
                writeContext);
            await auditStore.AppendAsync(
                new SourceDocumentUpload(
                    source.SourceId,
                    source.Id,
                    "admin@example.test",
                    "anatomi.docx",
                    4096,
                    new string('a', SourceDocumentUpload.ContentHashLength),
                    SourceDocumentUploadOutcome.Stored,
                    snapshot.Id,
                    "correlation-1",
                    new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)),
                Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<SourceDocumentUpload> uploads =
            await new SourceDocumentUploadAuditStore(context).ListForSourceAsync(
                source.SourceId,
                limit: 10,
                Token);

        SourceDocumentUpload stored = Assert.Single(uploads);
        Assert.Equal("admin@example.test", stored.UploadedBy);
        Assert.Equal("anatomi.docx", stored.FileName);
        Assert.Equal(4096, stored.ByteCount);
        Assert.Equal(SourceDocumentUploadOutcome.Stored, stored.Outcome);
        Assert.Equal(snapshot.Id, stored.SnapshotId);
    }

    private async Task<ScheduleSource> GivenUploadSourceAsync(
        string value,
        ProgramLanguage language,
        string? sharedDocumentGroup)
    {
        SourceId sourceId = SourceId.Parse(value);
        await using SirkadiyenDbContext context = fixture.CreateContext();

        ScheduleSource? existing = await context.ScheduleSources
            .SingleOrDefaultAsync(source => source.SourceId == sourceId, Token);
        if (existing is not null)
        {
            return existing;
        }

        ScheduleSource source = new(
            sourceId,
            $"Test upload source {value}",
            ScheduleSourceTransport.AdministrativeUpload,
            ScheduleDocumentFormat.Docx,
            $"urn:sirkadiyen:upload:{value}",
            "grade2_anatomy_autumn_v1",
            "1.0.0",
            "2025-2026",
            2,
            language,
            "Europe/Istanbul",
            supportedAudienceSelectors: null,
            sharedDocumentGroup: sharedDocumentGroup);

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);
        return source;
    }

    private async Task<SourceSnapshot> GivenSnapshotAsync(ScheduleSource source)
    {
        await using SirkadiyenDbContext context = fixture.CreateContext();
        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            "upload:sha256:test",
            "upload:sha256:test",
            source.AcademicYear,
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero),
            "sha256:content",
            SpreadsheetContractVersions.V1,
            "{}",
            1,
            1,
            0);

        context.SourceSnapshots.Add(snapshot);
        await context.SaveChangesAsync(Token);
        return snapshot;
    }
}
