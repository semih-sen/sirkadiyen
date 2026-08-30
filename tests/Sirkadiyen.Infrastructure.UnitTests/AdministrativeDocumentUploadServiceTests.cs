using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers administrative acquisition: the upload becomes immutable evidence for
/// every source the document serves, and nothing else happens here (ADR-080).
/// </summary>
public sealed class AdministrativeDocumentUploadServiceTests
{
    private const string AnatomyGroup = "g2-anatomy-autumn";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneUploadBecomesEvidenceForEveryProgramTheDocumentServes()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        Assert.Equal(DocumentUploadOutcome.Accepted, result.Outcome);

        // The Turkish and the English program each need their own revision,
        // because a canonical record reaches a student only when its program
        // language matches theirs. The administrator uploads once.
        Assert.Equal(
            ["G2-ANATOMY-AUTUMN", "G2-ANATOMY-AUTUMN-EN"],
            result.Targets.Select(target => target.SourceId));
        Assert.Equal(
            [ProgramLanguage.Turkish, ProgramLanguage.English],
            result.Targets.Select(target => target.ProgramLanguage));
        Assert.All(
            result.Targets,
            target => Assert.Equal(SourceDocumentUploadOutcome.Stored, target.Outcome));

        // Each target holds the document under its own source identity.
        Assert.Equal(
            ["G2-ANATOMY-AUTUMN", "G2-ANATOMY-AUTUMN-EN"],
            fixture.SnapshotStore.Stored.Select(entry => entry.Snapshot.SourceId));
    }

    [Fact]
    public async Task UploadingToEitherMemberServesTheWholeGroup()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN-EN");

        Assert.Equal(
            ["G2-ANATOMY-AUTUMN", "G2-ANATOMY-AUTUMN-EN"],
            result.Targets.Select(target => target.SourceId));
    }

    [Fact]
    public async Task TheSameDocumentNormalizesToTheSameContentForEveryTarget()
    {
        Fixture fixture = new();

        await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        // The content hash excludes acquisition metadata (ADR-014), so one
        // document produces one content hash however many sources it serves.
        // Anything else would mean the programs were reading different schedules.
        Assert.Single(fixture.SnapshotStore.Stored
            .Select(entry => entry.Snapshot.ContentHash)
            .Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReUploadingAnUnchangedDocumentStoresNothingAndStillAudits()
    {
        Fixture fixture = new();
        fixture.SnapshotStore.Changed = false;

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        Assert.Equal(DocumentUploadOutcome.Accepted, result.Outcome);
        Assert.All(
            result.Targets,
            target => Assert.Equal(SourceDocumentUploadOutcome.Unchanged, target.Outcome));

        // Knowing that an administrator re-uploaded an unchanged file is what
        // explains why no revision followed, so the audit row is still written.
        Assert.Equal(2, fixture.AuditStore.Appended.Count);
        Assert.All(
            fixture.AuditStore.Appended,
            upload => Assert.Equal(SourceDocumentUploadOutcome.Unchanged, upload.Outcome));
    }

    [Fact]
    public async Task TheAuditRecordsWhoUploadedWhatWithoutTrustingTheFileName()
    {
        Fixture fixture = new();

        await fixture.UploadAsync(
            "G2-ANATOMY-AUTUMN",
            fileName: "anatomi.docx",
            uploadedBy: "admin@example.test");

        SourceDocumentUpload upload = fixture.AuditStore.Appended[0];
        Assert.Equal("admin@example.test", upload.UploadedBy);
        Assert.Equal("anatomi.docx", upload.FileName);
        Assert.Equal(fixture.Document.Length, upload.ByteCount);
        Assert.Equal(64, upload.ContentSha256.Length);
        Assert.Equal(Now, upload.UploadedAtUtc);
    }

    [Fact]
    public async Task ASourceThatIsFetchedRatherThanUploadedIsRefused()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync("G2-TR-ANNUAL");

        // Accepting this would replace evidence the next poll silently overwrites.
        Assert.Equal(DocumentUploadOutcome.SourceIsNotUploadable, result.Outcome);
        Assert.Empty(fixture.SnapshotStore.Stored);
        Assert.Empty(fixture.AuditStore.Appended);
    }

    [Fact]
    public async Task AnUnknownSourceIsRefused()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync("G9-NOT-CONFIGURED");

        Assert.Equal(DocumentUploadOutcome.SourceNotFound, result.Outcome);
        Assert.Empty(fixture.SnapshotStore.Stored);
    }

    [Fact]
    public async Task AFileThatIsNotTheDeclaredFormatIsRefused()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync(
            "G2-ANATOMY-AUTUMN",
            fileName: "anatomi.xlsx");

        // The mistake is the upload, not the schedule, so it is named here rather
        // than surfacing later as a parse rejection.
        Assert.Equal(DocumentUploadOutcome.UnsupportedDocumentFormat, result.Outcome);
        Assert.Empty(fixture.SnapshotStore.Stored);
    }

    [Fact]
    public async Task AnEmptyDocumentIsRefused()
    {
        Fixture fixture = new();
        fixture.Document = [];

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        Assert.Equal(DocumentUploadOutcome.EmptyDocument, result.Outcome);
    }

    [Fact]
    public async Task ADocumentOverTheSizeBoundIsRefusedBeforeItIsConverted()
    {
        Fixture fixture = new();
        fixture.Document = new byte[AdministrativeDocumentUploadService.MaximumDocumentBytes + 1];

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        Assert.Equal(DocumentUploadOutcome.DocumentTooLarge, result.Outcome);
        Assert.Equal(0, fixture.Converter.ConvertCallCount);
    }

    [Fact]
    public async Task AFrozenPipelineAcceptsNoUpload()
    {
        Fixture fixture = new();
        fixture.Freeze.IsFrozen = true;

        DocumentUploadResult result = await fixture.UploadAsync("G2-ANATOMY-AUTUMN");

        // An upload is an acquisition, and a freeze stops acquisitions (ADR-034).
        Assert.Equal(DocumentUploadOutcome.Frozen, result.Outcome);
        Assert.Empty(fixture.SnapshotStore.Stored);
        Assert.Empty(fixture.AuditStore.Appended);
    }

    [Fact]
    public async Task ASourceWithNoSharedGroupServesOnlyItself()
    {
        Fixture fixture = new();

        DocumentUploadResult result = await fixture.UploadAsync("G2-SOLO-UPLOAD");

        DocumentUploadTargetResult target = Assert.Single(result.Targets);
        Assert.Equal("G2-SOLO-UPLOAD", target.SourceId);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new AdministrativeDocumentUploadService(
                SourceStore,
                SnapshotStore,
                AuditStore,
                Converter,
                Freeze,
                new FixedTimeProvider(Now));
        }

        public AdministrativeDocumentUploadService Service { get; }

        public FakeSourceStore SourceStore { get; } = new();

        public RecordingSnapshotStore SnapshotStore { get; } = new();

        public RecordingAuditStore AuditStore { get; } = new();

        public CountingConverter Converter { get; } = new();

        public FakeOperationalFreezeStore Freeze { get; } = new();

        public byte[] Document { get; set; } = [1, 2, 3, 4];

        public Task<DocumentUploadResult> UploadAsync(
            string sourceId,
            string fileName = "anatomi.docx",
            string uploadedBy = "admin@example.test") =>
            Service.UploadAsync(
                new DocumentUploadRequest
                {
                    SourceId = sourceId,
                    FileName = fileName,
                    Content = Document,
                    UploadedBy = uploadedBy,
                    CorrelationId = "correlation-1",
                },
                CancellationToken.None);
    }

    private sealed class FakeSourceStore : IScheduleSourceStore
    {
        private readonly List<ScheduleSource> sources =
        [
            Upload("G2-ANATOMY-AUTUMN", ProgramLanguage.Turkish, AnatomyGroup),
            Upload("G2-ANATOMY-AUTUMN-EN", ProgramLanguage.English, AnatomyGroup),
            Upload("G2-SOLO-UPLOAD", ProgramLanguage.Turkish, sharedDocumentGroup: null),
            new ScheduleSource(
                SourceId.Parse("G2-TR-ANNUAL"),
                "Fetched workbook",
                ScheduleSourceTransport.GoogleSheets,
                ScheduleDocumentFormat.GoogleSheet,
                "https://docs.google.com/spreadsheets/d/example",
                "grade2_yearly_v1",
                "1.0.0",
                "2025-2026",
                2,
                ProgramLanguage.Turkish,
                "Europe/Istanbul",
                "spreadsheet-1",
                1),
        ];

        public Task<IReadOnlyList<ScheduleSource>> ListAsync(
            bool onlyPollingEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduleSource>>(sources);

        public Task<ScheduleSource?> FindAsync(
            SourceId sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(sources.SingleOrDefault(source => source.SourceId == sourceId));

        public Task<IReadOnlyList<ScheduleSource>> ListSharingDocumentAsync(
            SourceId sourceId,
            CancellationToken cancellationToken)
        {
            ScheduleSource? source = sources.SingleOrDefault(
                candidate => candidate.SourceId == sourceId);
            if (source is null)
            {
                return Task.FromResult<IReadOnlyList<ScheduleSource>>([]);
            }

            IReadOnlyList<ScheduleSource> group = source.SharedDocumentGroup is { } name
                ? [.. sources
                    .Where(candidate => candidate.SharedDocumentGroup == name)
                    .OrderBy(candidate => candidate.SourceId.Value, StringComparer.Ordinal)]
                : [source];
            return Task.FromResult(group);
        }

        public Task<int> UpsertAsync(
            IReadOnlyCollection<ScheduleSource> incoming,
            CancellationToken cancellationToken) => Task.FromResult(0);

        // An administrative upload never polls, so nothing here records a poll failure.
        public Task RecordPollFailureAsync(
            SourceId sourceId,
            DateTimeOffset failedAtUtc,
            string reason,
            CancellationToken cancellationToken) => Task.CompletedTask;

        private static ScheduleSource Upload(
            string sourceId,
            ProgramLanguage language,
            string? sharedDocumentGroup) => new(
                SourceId.Parse(sourceId),
                sourceId,
                ScheduleSourceTransport.AdministrativeUpload,
                ScheduleDocumentFormat.Docx,
                $"urn:sirkadiyen:upload:{sourceId}",
                "grade2_anatomy_autumn_v1",
                "1.0.0",
                "2025-2026",
                2,
                language,
                "Europe/Istanbul",
                supportedAudienceSelectors: null,
                sharedDocumentGroup: sharedDocumentGroup);
    }

    private sealed class RecordingSnapshotStore : ISourceSnapshotStore
    {
        public List<(SourceId SourceId, NormalizedSpreadsheetSnapshot Snapshot)> Stored { get; } = [];

        public bool Changed { get; set; } = true;

        public Task<StoreSnapshotResult> StoreIfChangedAsync(
            SourceId sourceId,
            NormalizedSpreadsheetSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            Stored.Add((sourceId, snapshot));
            return Task.FromResult(new StoreSnapshotResult
            {
                Outcome = Changed ? StoreSnapshotOutcome.Stored : StoreSnapshotOutcome.Unchanged,
                Snapshot = new SourceSnapshot(
                    Guid.CreateVersion7(),
                    sourceId,
                    snapshot.SnapshotId,
                    snapshot.SpreadsheetId,
                    "2025-2026",
                    snapshot.AcquiredAtUtc,
                    snapshot.ContentHash,
                    snapshot.ContractVersion,
                    "{}",
                    snapshot.Worksheets.Count,
                    0,
                    snapshot.Diagnostics.Count),
            });
        }

        public Task<SourceSnapshot?> GetLatestAsync(
            SourceId sourceId,
            CancellationToken cancellationToken) => Task.FromResult<SourceSnapshot?>(null);
    }

    private sealed class RecordingAuditStore : ISourceDocumentUploadAuditStore
    {
        public List<SourceDocumentUpload> Appended { get; } = [];

        public Task AppendAsync(SourceDocumentUpload upload, CancellationToken cancellationToken)
        {
            Appended.Add(upload);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SourceDocumentUpload>> ListForSourceAsync(
            SourceId sourceId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceDocumentUpload>>(
                [.. Appended.Where(upload => upload.SourceId == sourceId)]);
    }

    /// <summary>
    /// Answers the format question the real converter answers, without needing a
    /// real Word document: these tests are about the upload rules.
    /// </summary>
    private sealed class CountingConverter : IUploadedDocumentConverter
    {
        private readonly UploadedDocumentConverter inner = new(new DocxSnapshotConverter());

        public int ConvertCallCount { get; private set; }

        public bool CanConvert(ScheduleDocumentFormat format, string fileName) =>
            inner.CanConvert(format, fileName);

        public NormalizedSpreadsheetSnapshot Convert(
            ScheduleDocumentFormat format,
            ReadOnlyMemory<byte> content,
            AcquireSpreadsheetSnapshotRequest request)
        {
            ConvertCallCount++;
            return new NormalizedSpreadsheetSnapshot
            {
                ContractVersion = SpreadsheetContractVersions.V1,
                SourceId = request.SourceId,
                SnapshotId = request.SnapshotId,
                SpreadsheetId = request.SpreadsheetId,
                AcquiredAtUtc = request.AcquiredAtUtc,

                // The real converter hashes the normalized content, which does not
                // include the source identity, so every target sees the same hash.
                ContentHash = $"sha256:{content.Length}",
                ContentHashAlgorithm = "SHA-256",
            };
        }
    }

    private sealed class FakeOperationalFreezeStore : IOperationalFreezeStore
    {
        public bool IsFrozen { get; set; }

        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot
            {
                IsFrozen = IsFrozen,
            });

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
