using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;
using ContractLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;
using DomainLanguage = Sirkadiyen.Domain.Scheduling.Sources.ProgramLanguage;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class ScheduleSourcePollerTests
{
    [Fact]
    public async Task AnUnchangedSnapshotWithoutASuccessfulParseIsStillParsed()
    {
        ScheduleSource source = Source();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        SourceSnapshot storedSnapshot = StoredSnapshot(source, snapshot);
        FakeSnapshotAcquirer acquirer = new(snapshot);
        FakeSnapshotStore snapshotStore = new(storedSnapshot, changed: false);
        FakeParserClient parserClient = new();
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            snapshotStore,
            parserClient,
            resultStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero)));

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        Assert.False(result.SnapshotChanged);
        Assert.Equal(1, parserClient.CallCount);
        Assert.Equal(source.AcademicYear, parserClient.LastRequest!.SourceContext.AcademicYear);
        Assert.Equal(ContractLanguage.Turkish, parserClient.LastRequest.SourceContext.ProgramLanguage);
        Assert.Equal(storedSnapshot.ExternalSnapshotId, parserClient.LastRequest.Snapshot.SnapshotId);
        Assert.True(resultStore.Completed);
    }

    [Fact]
    public async Task ParserFailureIsPersistedBeforeTheErrorEscapes()
    {
        ScheduleSource source = Source();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        FakeParserClient parserClient = new() { Exception = new HttpRequestException("offline") };
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            parserClient,
            resultStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => poller.PollAsync(source, CancellationToken.None));

        Assert.True(resultStore.Failed);
        Assert.Contains(nameof(HttpRequestException), resultStore.FailureReason);
    }

    [Fact]
    public async Task AnUploadedSourceWithNoDocumentYetWaitsWithoutAcquiringOrParsing()
    {
        ScheduleSource source = UploadSource();
        FakeSnapshotAcquirer acquirer = new(Snapshot(Source()));
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(Source(), Snapshot(Source())), changed: true)
            {
                HasLatest = false,
            },
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        // It is not an unimplemented transport but a source with no location, so
        // the outcome says what an operator is actually waiting for.
        Assert.Equal(ScheduleSourcePollOutcome.AwaitingAdministrativeUpload, result.Outcome);
        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(0, parser.CallCount);
    }

    [Fact]
    public async Task AnUploadedSourceParsesTheDocumentTheAdministratorSupplied()
    {
        ScheduleSource source = UploadSource();
        FakeSnapshotAcquirer acquirer = new(Snapshot(Source()));
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: false),
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        // The upload is the acquisition, so this cycle acquires nothing and still
        // runs the same parse the fetched sources run.
        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        Assert.False(result.SnapshotChanged);
        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(1, parser.CallCount);
        Assert.Equal(
            "grade2_anatomy_autumn_v1",
            parser.LastRequest!.ParserProfile.Name);
    }

    [Fact]
    public async Task AFreezeStopsAnUploadedSourceBeforeItsStoredDocumentIsParsed()
    {
        ScheduleSource source = UploadSource();
        FakeParseResultStore parseStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(Snapshot(Source())),
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: false),
            new FakeParserClient(),
            parseStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore { IsFrozen = true },
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Frozen, result.Outcome);
        Assert.Equal(0, parseStore.BeginCallCount);
    }

    [Fact]
    public async Task UnsupportedTransportNeverAcquiresOrParses()
    {
        ScheduleSource source = FileSource(
            "SHARED-AMPHI",
            ScheduleSourceTransport.HttpFile,
            ScheduleDocumentFormat.Xlsx);
        FakeSnapshotAcquirer acquirer = new(Snapshot(Source()));
        FakeDriveDocumentAcquirer driveAcquirer = new();
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            acquirer,
            driveAcquirer,
            new FakeSnapshotStore(StoredSnapshot(Source(), Snapshot(Source())), changed: true),
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        // Nothing can fetch an HTTP-published file yet, which is a different gap
        // from a fetchable file that nothing can read.
        Assert.Equal(ScheduleSourcePollOutcome.UnsupportedTransport, result.Outcome);
        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(0, driveAcquirer.CallCount);
        Assert.Equal(0, parser.CallCount);
    }

    [Fact]
    public async Task ADriveFileInAFormatNothingReadsIsNeverDownloaded()
    {
        ScheduleSource source = FileSource(
            "G3-TR-A-ANNUAL",
            ScheduleSourceTransport.GoogleDriveFile,
            ScheduleDocumentFormat.Xlsx);
        FakeDriveDocumentAcquirer driveAcquirer = new();
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(Snapshot(Source())),
            driveAcquirer,
            new FakeSnapshotStore(StoredSnapshot(Source(), Snapshot(Source())), changed: true),
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        // The transport works; the workbook has no converter and no profile. The
        // outcome says which of the two is missing, and nothing is downloaded for
        // a document nothing could interpret.
        Assert.Equal(ScheduleSourcePollOutcome.UnsupportedDocumentFormat, result.Outcome);
        Assert.Equal(0, driveAcquirer.CallCount);
        Assert.Equal(0, parser.CallCount);
    }

    [Fact]
    public async Task ADriveDocumentIsDownloadedAndParsedLikeAnyOtherSource()
    {
        ScheduleSource source = FileSource(
            "G2-VERTICAL-AUTUMN",
            ScheduleSourceTransport.GoogleDriveFile,
            ScheduleDocumentFormat.Docx);
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeSnapshotAcquirer sheetsAcquirer = new(snapshot);
        FakeDriveDocumentAcquirer driveAcquirer = new(snapshot);
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            sheetsAcquirer,
            driveAcquirer,
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        Assert.True(result.SnapshotChanged);
        Assert.Equal(1, driveAcquirer.CallCount);

        // Addressed by the Drive file identifier the catalog stores, not by the
        // URL a person opens.
        Assert.Equal("drive-file-1", driveAcquirer.LastRequest!.SpreadsheetId);
        Assert.Equal(ScheduleDocumentFormat.Docx, driveAcquirer.LastFormat);

        // The sheet adapter is never involved, and the parser receives the same
        // normalized snapshot a sheet would have produced.
        Assert.Equal(0, sheetsAcquirer.CallCount);
        Assert.Equal(1, parser.CallCount);
        Assert.Equal("grade2_vertical_corridor_v1", parser.LastRequest!.ParserProfile.Name);
    }

    [Fact]
    public async Task AFreezeStopsADriveDocumentBeforeItIsDownloaded()
    {
        ScheduleSource source = FileSource(
            "G2-VERTICAL-SPRING",
            ScheduleSourceTransport.GoogleDriveFile,
            ScheduleDocumentFormat.Docx);
        FakeDriveDocumentAcquirer driveAcquirer = new(Snapshot(source));
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(Snapshot(source)),
            driveAcquirer,
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: true),
            new FakeParserClient(),
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(isFrozen: true),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        // A freeze means no source is acquired, whichever transport would have
        // done the acquiring (ADR-034).
        Assert.Equal(ScheduleSourcePollOutcome.Frozen, result.Outcome);
        Assert.Equal(0, driveAcquirer.CallCount);
    }

    [Fact]
    public async Task ADriveSourceWithNoFileIdentifierIsRefusedRatherThanGuessedAt()
    {
        ScheduleSource source = new(
            SourceId.Parse("G2-VERTICAL-AUTUMN"),
            "Vertical corridor autumn",
            ScheduleSourceTransport.GoogleDriveFile,
            ScheduleDocumentFormat.Docx,
            "https://drive.google.com/file/d/example/view",
            "grade2_vertical_corridor_v1",
            "1.0.0",
            "2025-2026",
            2,
            DomainLanguage.Turkish,
            "Europe/Istanbul");
        FakeDriveDocumentAcquirer driveAcquirer = new();
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(Snapshot(Source())),
            driveAcquirer,
            new FakeSnapshotStore(StoredSnapshot(Source(), Snapshot(Source())), changed: true),
            new FakeParserClient(),
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        // The URL is for a person to open and the identifier is what the API
        // reads. Deriving one from the other would be inventing provenance.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => poller.PollAsync(source, CancellationToken.None));

        Assert.Equal(0, driveAcquirer.CallCount);
    }

    [Fact]
    public async Task FrozenSourceNeverStartsAcquisition()
    {
        ScheduleSource source = Source();
        FakeSnapshotAcquirer acquirer = new(Snapshot(source));
        FakeParseResultStore parseStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: true),
            new FakeParserClient(),
            parseStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(isFrozen: true),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Frozen, result.Outcome);
        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(0, parseStore.BeginCallCount);
    }

    [Fact]
    public async Task FreezeReadFailureNeverStartsAcquisition()
    {
        ScheduleSource source = Source();
        FakeSnapshotAcquirer acquirer = new(Snapshot(source));
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: true),
            new FakeParserClient(),
            new FakeParseResultStore(shouldInvokeParser: true),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore
            {
                Exception = new InvalidOperationException("database unavailable"),
            },
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => poller.PollAsync(source, CancellationToken.None));

        Assert.Equal(0, acquirer.CallCount);
    }

    [Fact]
    public async Task FreezeDuringAcquisitionStoresEvidenceButDoesNotStartAParseRun()
    {
        ScheduleSource source = Source();
        FakeOperationalFreezeStore freeze = new();
        FakeSnapshotAcquirer acquirer = new(Snapshot(source))
        {
            OnAcquire = () => freeze.IsFrozen = true,
        };
        FakeParseResultStore parseStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, Snapshot(source)), changed: true),
            new FakeParserClient(),
            parseStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            freeze,
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Frozen, result.Outcome);
        Assert.True(result.SnapshotChanged);
        Assert.Equal(1, acquirer.CallCount);
        Assert.Equal(0, parseStore.BeginCallCount);
    }

    /// <summary>
    /// A source that names a companion hands the companion's stored evidence to
    /// the parser alongside its own (ADR-102).
    /// </summary>
    [Fact]
    public async Task ACompanionSnapshotReachesTheParserWithTheSourcesOwn()
    {
        ScheduleSource source = AnnualWithBedsideCompanion();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        SourceSnapshot bedside = CompanionSnapshot(source);
        FakeParserClient parser = new();
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(snapshot),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true)
            {
                OtherSources = new Dictionary<string, SourceSnapshot?>(StringComparer.Ordinal)
                {
                    ["G3-TR-A-BEDSIDE"] = bedside,
                },
            },
            parser,
            resultStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero)));

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        NormalizedSpreadsheetSnapshot auxiliary = Assert.Single(
            parser.LastRequest!.AuxiliarySnapshots);
        Assert.Equal("G3-TR-A-BEDSIDE", auxiliary.SourceId);

        // The run is keyed by that evidence too, otherwise an edit to the bedside
        // document alone would be short-circuited as already parsed.
        Assert.NotEqual(ParseRunCompanionFingerprint.None, resultStore.CompanionFingerprint);
    }

    /// <summary>
    /// A companion that has never been acquired must not hold up the schedule it
    /// only annotates (ADR-102).
    /// </summary>
    [Fact]
    public async Task ACompanionThatWasNeverAcquiredIsLeftOutRatherThanBlocking()
    {
        ScheduleSource source = AnnualWithBedsideCompanion();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeParserClient parser = new();
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(snapshot),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true)
            {
                OtherSources = new Dictionary<string, SourceSnapshot?>(StringComparer.Ordinal)
                {
                    ["G3-TR-A-BEDSIDE"] = null,
                },
            },
            parser,
            resultStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero)));

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        Assert.Empty(parser.LastRequest!.AuxiliarySnapshots);

        // The fingerprint describes what was actually read, so a run that read no
        // companion says so rather than claiming evidence it never saw.
        Assert.Equal(ParseRunCompanionFingerprint.None, resultStore.CompanionFingerprint);
    }

    private static ScheduleSource Source() => new(
        SourceId.Parse("G1-TR-ANNUAL"),
        "Grade 1 Turkish annual",
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://docs.google.com/spreadsheets/d/example",
        "grade1_yearly_v1",
        "1.0.0",
        "2025-2026",
        1,
        DomainLanguage.Turkish,
        "Europe/Istanbul",
        "spreadsheet-1",
        1);

    /// <summary>A source published as a file, addressed by its external ID.</summary>
    private static ScheduleSource FileSource(
        string sourceId,
        ScheduleSourceTransport transport,
        ScheduleDocumentFormat format) => new(
            SourceId.Parse(sourceId),
            $"File source {sourceId}",
            transport,
            format,
            "https://example.invalid/source",
            format is ScheduleDocumentFormat.Docx
                ? "grade2_vertical_corridor_v1"
                : "grade3_yearly_v1",
            "1.0.0",
            "2025-2026",
            2,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "drive-file-1");

    /// <summary>A source whose document is uploaded rather than published (ADR-079).</summary>
    private static ScheduleSource UploadSource() => new(
        SourceId.Parse("G2-ANATOMY-AUTUMN"),
        "Uploaded document",
        ScheduleSourceTransport.AdministrativeUpload,
        ScheduleDocumentFormat.Docx,
        "urn:sirkadiyen:upload:G2-ANATOMY-AUTUMN",
        "grade2_anatomy_autumn_v1",
        "1.0.0",
        "2025-2026",
        2,
        DomainLanguage.Turkish,
        "Europe/Istanbul");

    /// <summary>
    /// A source that defers a group rotation is told which of its dates the
    /// owning sources have already published, for its own program (ADR-126).
    /// </summary>
    [Fact]
    public async Task TheDatesTheRotationOwnersPublishedReachTheParser()
    {
        ScheduleSource source = AnnualWithAnatomyRotation();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeParserClient parser = new();
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        FakeGroupRotationCoverageStore coverage = new(
            new DateOnly(2026, 10, 6),
            new DateOnly(2026, 10, 8));
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(snapshot),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            parser,
            resultStore,
            coverage,
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero)));

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.Parsed, result.Outcome);
        Assert.Equal(
            [new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 8)],
            parser.LastRequest!.SourceContext.GroupRotationCoveredDates);

        // Asked for the deferring source's program, not the owner's: an owner
        // still pointing at last year's document covers nothing (ADR-115).
        Assert.Equal(source.AcademicYear, coverage.RequestedAcademicYear);
        Assert.Equal(source.ClassYear, coverage.RequestedClassYear);
        Assert.Equal(source.ProgramLanguage, coverage.RequestedProgramLanguage);

        // Keyed by that coverage, or uploading the group list would leave the
        // snapshot short-circuited as already parsed and every fallback hour on
        // the calendar beside the real one.
        Assert.NotEqual(ParseRunCompanionFingerprint.None, resultStore.CompanionFingerprint);
    }

    /// <summary>
    /// A source that names no rotation owner asks nothing and is keyed exactly as
    /// it was before the fallback existed (ADR-126).
    /// </summary>
    [Fact]
    public async Task ASourceWithoutARotationOwnerIsUnaffected()
    {
        ScheduleSource source = Source();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeParserClient parser = new();
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        FakeGroupRotationCoverageStore coverage = new(new DateOnly(2026, 10, 6));
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(snapshot),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            parser,
            resultStore,
            coverage,
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero)));

        await poller.PollAsync(source, CancellationToken.None);

        Assert.Null(coverage.RequestedSourceIds);
        Assert.Empty(parser.LastRequest!.SourceContext.GroupRotationCoveredDates);
        Assert.Equal(ParseRunCompanionFingerprint.None, resultStore.CompanionFingerprint);
    }

    /// <summary>
    /// A forced re-poll keys the run on a unique token so it opens a fresh run even for an unchanged
    /// snapshot and profile, rather than short-circuiting as already parsed (ADR-127).
    /// </summary>
    [Fact]
    public async Task AForcedRepollKeysTheRunOnAUniqueToken()
    {
        ScheduleSource source = Source();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeParseResultStore resultStore = new(shouldInvokeParser: true);
        ScheduleSourcePoller poller = new(
            new FakeSnapshotAcquirer(snapshot),
            new FakeDriveDocumentAcquirer(snapshot),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            new FakeParserClient(),
            resultStore,
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery(),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero)));

        await poller.PollAsync(source, forceReparse: true, CancellationToken.None);

        // The salt replaces the fingerprint so the run's identity cannot match the already-parsed
        // one, and it stays within the fingerprint length limit.
        Assert.StartsWith("force:", resultStore.CompanionFingerprint, StringComparison.Ordinal);
        Assert.True(resultStore.CompanionFingerprint!.Length <= ParseRunCompanionFingerprint.MaxLength);
    }

    /// <summary>
    /// The Grade 2 Turkish annual as the catalog declares it: its dissection rows
    /// defer to the anatomy group lists for the dates those have published
    /// (ADR-126).
    /// </summary>
    private static ScheduleSource AnnualWithAnatomyRotation() => new(
        SourceId.Parse("G2-TR-ANNUAL"),
        "Grade 2 Turkish annual",
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://docs.google.com/spreadsheets/d/example-g2",
        "grade2_yearly_v1",
        "1.1.0",
        "2026-2027",
        2,
        DomainLanguage.Turkish,
        "Europe/Istanbul",
        "spreadsheet-g2",
        groupRotationSourceIds:
        [
            SourceId.Parse("G2-ANATOMY-AUTUMN"),
            SourceId.Parse("G2-ANATOMY-SPRING"),
        ]);

    /// <summary>
    /// The Grade 3 Turkish A annual as the catalog declares it: its parser reads
    /// the bedside document for the topic of each practice session (ADR-102).
    /// </summary>
    private static ScheduleSource AnnualWithBedsideCompanion() => new(
        SourceId.Parse("G3-TR-A-ANNUAL"),
        "Grade 3 Turkish A annual",
        ScheduleSourceTransport.GoogleDriveFile,
        ScheduleDocumentFormat.Docx,
        "https://example.invalid/annual",
        "grade3_yearly_v1",
        "1.0.0",
        "2026-2027",
        3,
        DomainLanguage.Turkish,
        "Europe/Istanbul",
        "drive-annual-1",
        companionSourceIds: [SourceId.Parse("G3-TR-A-BEDSIDE")]);

    private static SourceSnapshot CompanionSnapshot(ScheduleSource owner)
    {
        NormalizedSpreadsheetSnapshot document = new()
        {
            ContractVersion = SpreadsheetContractVersions.V1,
            SourceId = "G3-TR-A-BEDSIDE",
            SnapshotId = "bedside-snapshot-1",
            SpreadsheetId = "drive-bedside-1",
            AcquiredAtUtc = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
            ContentHash = "sha256:bedside",
            ContentHashAlgorithm = "SHA-256",
        };

        return new SourceSnapshot(
            Guid.CreateVersion7(),
            SourceId.Parse(document.SourceId),
            document.SnapshotId,
            document.SpreadsheetId,
            owner.AcademicYear,
            document.AcquiredAtUtc,
            document.ContentHash,
            document.ContractVersion,
            System.Text.Json.JsonSerializer.Serialize(
                document,
                Sirkadiyen.Contracts.Serialization.ContractJson.CreateOptions()),
            0,
            0,
            0);
    }

    private static NormalizedSpreadsheetSnapshot Snapshot(ScheduleSource source) => new()
    {
        ContractVersion = SpreadsheetContractVersions.V1,
        SourceId = source.SourceId.Value,
        SnapshotId = "snapshot-1",
        SpreadsheetId = source.ExternalId ?? "spreadsheet-1",
        AcquiredAtUtc = new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero),
        ContentHash = "sha256:content",
        ContentHashAlgorithm = "SHA-256",
    };

    private static SourceSnapshot StoredSnapshot(
        ScheduleSource source,
        NormalizedSpreadsheetSnapshot snapshot) => new(
            source.Id,
            source.SourceId,
            snapshot.SnapshotId,
            snapshot.SpreadsheetId,
            source.AcademicYear,
            snapshot.AcquiredAtUtc,
            snapshot.ContentHash,
            snapshot.ContractVersion,
            System.Text.Json.JsonSerializer.Serialize(
                snapshot,
                Sirkadiyen.Contracts.Serialization.ContractJson.CreateOptions()),
            0,
            0,
            0);

    /// <summary>
    /// Builds a validation service whose store reports that nothing is awaiting
    /// validation, so these tests exercise polling alone.
    /// </summary>
    private static ScheduleRevisionValidationService ValidationService() => new(
        new FakeValidationStore(),
        new ScheduleRevisionValidator(new RevisionValidationOptions()),
        TimeProvider.System);

    private sealed class FakeValidationStore : IScheduleRevisionValidationStore
    {
        public Task<RevisionValidationInput?> LoadAsync(
            Guid revisionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<RevisionValidationInput?>(null);

        public Task ApplyAsync(
            Guid revisionId,
            RevisionValidationResult result,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> ListPendingValidationAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    [Fact]
    public async Task TheDocumentDiscoveryResolvesIsWhatGetsAcquired()
    {
        // The weekly amphitheatre program is republished into a folder rather than
        // edited in place, so the file the catalog names is not necessarily the one
        // this cycle must read (ADR-133).
        ScheduleSource source = Source();
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);
        FakeSnapshotAcquirer acquirer = new(snapshot);

        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeDriveDocumentAcquirer(),
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: false),
            new FakeParserClient(),
            new FakeParseResultStore(shouldInvokeParser: false),
            new FakeGroupRotationCoverageStore(),
            ValidationService(),
            new FakeOperationalFreezeStore(),
            new FakeWeeklyDocumentDiscovery("this-weeks-workbook"),
            new ParseRunOptions(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero)));

        await poller.PollAsync(source, CancellationToken.None);

        Assert.NotNull(acquirer.LastRequest);
        Assert.Equal("this-weeks-workbook", acquirer.LastRequest!.SpreadsheetId);
        Assert.NotEqual(source.ExternalId, acquirer.LastRequest.SpreadsheetId);
    }

    private sealed class FakeSnapshotAcquirer(NormalizedSpreadsheetSnapshot snapshot)
        : ISpreadsheetSnapshotAcquirer
    {
        public int CallCount { get; private set; }

        public Action? OnAcquire { get; init; }

        public AcquireSpreadsheetSnapshotRequest? LastRequest { get; private set; }

        public Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
            AcquireSpreadsheetSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            OnAcquire?.Invoke();
            return Task.FromResult(snapshot with
            {
                SnapshotId = request.SnapshotId,
                AcquiredAtUtc = request.AcquiredAtUtc,
            });
        }
    }

    private sealed class FakeDriveDocumentAcquirer(NormalizedSpreadsheetSnapshot? snapshot = null)
        : IDriveDocumentAcquirer
    {
        public int CallCount { get; private set; }

        public AcquireSpreadsheetSnapshotRequest? LastRequest { get; private set; }

        public ScheduleDocumentFormat? LastFormat { get; private set; }

        /// <summary>Mirrors the real acquirer: DOCX is read, other formats are not.</summary>
        public bool CanAcquire(ScheduleDocumentFormat format) =>
            format is ScheduleDocumentFormat.Docx;

        public Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
            ScheduleDocumentFormat format,
            AcquireSpreadsheetSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastFormat = format;
            return Task.FromResult((snapshot
                ?? throw new InvalidOperationException("This acquirer was not expected to run.")) with
            {
                SnapshotId = request.SnapshotId,
                AcquiredAtUtc = request.AcquiredAtUtc,
            });
        }
    }

    private sealed class FakeSnapshotStore(SourceSnapshot snapshot, bool changed)
        : ISourceSnapshotStore
    {
        /// <summary>Whether the source already holds evidence, as an upload source may not.</summary>
        public bool HasLatest { get; init; } = true;

        /// <summary>
        /// Evidence held for sources other than the one under test, so a
        /// companion lookup answers for itself rather than for the primary
        /// source. A null value is a companion that has never been acquired.
        /// </summary>
        public IReadOnlyDictionary<string, SourceSnapshot?> OtherSources { get; init; } =
            new Dictionary<string, SourceSnapshot?>(StringComparer.Ordinal);

        public Task<StoreSnapshotResult> StoreIfChangedAsync(
            SourceId sourceId,
            NormalizedSpreadsheetSnapshot acquired,
            CancellationToken cancellationToken) => Task.FromResult(new StoreSnapshotResult
            {
                Outcome = changed ? StoreSnapshotOutcome.Stored : StoreSnapshotOutcome.Unchanged,
                Snapshot = snapshot,
            });

        public Task<SourceSnapshot?> GetLatestAsync(
            SourceId sourceId,
            CancellationToken cancellationToken) =>
            OtherSources.ContainsKey(sourceId.Value)
                ? Task.FromResult(OtherSources[sourceId.Value])
                : Task.FromResult<SourceSnapshot?>(HasLatest ? snapshot : null);
    }

    private sealed class FakeParserClient : IScheduleParserClient
    {
        public int CallCount { get; private set; }

        public ParseSnapshotRequest? LastRequest { get; private set; }

        public Exception? Exception { get; init; }

        public Task<ParseSnapshotResponse> ParseAsync(
            ParseSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (Exception is not null)
            {
                return Task.FromException<ParseSnapshotResponse>(Exception);
            }

            return Task.FromResult(new ParseSnapshotResponse
            {
                ContractVersion = request.ContractVersion,
                CorrelationId = request.CorrelationId,
                SourceId = request.Snapshot.SourceId,
                SnapshotId = request.Snapshot.SnapshotId,
                ParserProfile = request.ParserProfile,
                Status = ParserResultStatus.Completed,
            });
        }
    }

    private sealed class FakeGroupRotationCoverageStore(params DateOnly[] published)
        : IGroupRotationCoverageStore
    {
        public IReadOnlyCollection<SourceId>? RequestedSourceIds { get; private set; }

        public string? RequestedAcademicYear { get; private set; }

        public int? RequestedClassYear { get; private set; }

        public DomainLanguage? RequestedProgramLanguage { get; private set; }

        public Task<IReadOnlyList<DateOnly>> ListPublishedDatesAsync(
            IReadOnlyCollection<SourceId> rotationSourceIds,
            string academicYear,
            int classYear,
            DomainLanguage programLanguage,
            CancellationToken cancellationToken)
        {
            RequestedSourceIds = rotationSourceIds;
            RequestedAcademicYear = academicYear;
            RequestedClassYear = classYear;
            RequestedProgramLanguage = programLanguage;
            return Task.FromResult<IReadOnlyList<DateOnly>>([.. published]);
        }
    }

    private sealed class FakeParseResultStore(bool shouldInvokeParser)
        : IScheduleParseResultStore
    {
        private readonly Guid runId = Guid.CreateVersion7();

        public bool Completed { get; private set; }

        public bool Failed { get; private set; }

        public string? FailureReason { get; private set; }

        public int BeginCallCount { get; private set; }

        public TimeSpan? StaleRunTimeout { get; private set; }

        public string? CompanionFingerprint { get; private set; }

        public Task<BeginParseRunResult> BeginOrResumeAsync(
            SourceSnapshot snapshot,
            ScheduleSource source,
            string correlationId,
            DateTimeOffset startedAtUtc,
            TimeSpan staleRunTimeout,
            string companionFingerprint,
            CancellationToken cancellationToken)
        {
            BeginCallCount++;
            StaleRunTimeout = staleRunTimeout;
            CompanionFingerprint = companionFingerprint;
            return Task.FromResult(new BeginParseRunResult
            {
                ParseRunId = runId,
                Status = shouldInvokeParser ? ParseRunStatus.Running : ParseRunStatus.Completed,
                ShouldInvokeParser = shouldInvokeParser,
            });
        }

        public Task<ScheduleRevision?> CompleteAsync(
            Guid parseRunId,
            ParseSnapshotResponse response,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.FromResult<ScheduleRevision?>(null);
        }

        public Task FailAsync(
            Guid parseRunId,
            DateTimeOffset completedAtUtc,
            string failureReason,
            CancellationToken cancellationToken)
        {
            Failed = true;
            FailureReason = failureReason;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Resolves to whatever the source already names, which is what real discovery
    /// does for every source that declares no folder (ADR-133).
    /// </summary>
    private sealed class FakeWeeklyDocumentDiscovery(string? resolvedExternalId = null)
        : IWeeklyDocumentDiscovery
    {
        public ScheduleSource? LastSource { get; private set; }

        public Task<WeeklyDocumentResolution> ResolveAsync(
            ScheduleSource source,
            CancellationToken cancellationToken)
        {
            LastSource = source;
            return Task.FromResult(new WeeklyDocumentResolution
            {
                ExternalId = resolvedExternalId ?? source.ExternalId ?? string.Empty,
                Outcome = resolvedExternalId is null
                    ? WeeklyDocumentDiscoveryOutcome.NotConfigured
                    : WeeklyDocumentDiscoveryOutcome.ResolvedSingle,
            });
        }
    }

    private sealed class FakeOperationalFreezeStore(bool isFrozen = false)
        : IOperationalFreezeStore
    {
        public bool IsFrozen { get; set; } = isFrozen;

        public Exception? Exception { get; init; }

        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Exception is null
                ? Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = IsFrozen })
                : Task.FromException<OperationalFreezeSnapshot>(Exception);

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool requestedState,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken)
        {
            OperationalFreezeChangeOutcome outcome = IsFrozen == requestedState
                ? OperationalFreezeChangeOutcome.AlreadyInRequestedState
                : OperationalFreezeChangeOutcome.Changed;
            IsFrozen = requestedState;
            return Task.FromResult(new OperationalFreezeChangeResult
            {
                Outcome = outcome,
                State = new OperationalFreezeSnapshot { IsFrozen = IsFrozen },
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
