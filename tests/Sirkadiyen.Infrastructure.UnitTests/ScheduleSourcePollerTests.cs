using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;
using ContractLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

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
            snapshotStore,
            parserClient,
            resultStore,
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
            new FakeSnapshotStore(StoredSnapshot(source, snapshot), changed: true),
            parserClient,
            resultStore,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => poller.PollAsync(source, CancellationToken.None));

        Assert.True(resultStore.Failed);
        Assert.Contains(nameof(HttpRequestException), resultStore.FailureReason);
    }

    [Fact]
    public async Task UnsupportedTransportNeverAcquiresOrParses()
    {
        ScheduleSource source = new(
            SourceId.Parse("G3-FILE"),
            "File source",
            ScheduleSourceTransport.GoogleDriveFile,
            ScheduleDocumentFormat.Xlsx,
            "https://example.invalid/source.xlsx",
            "grade3_yearly_v1",
            "1.0.0",
            "2025-2026",
            3,
            DomainLanguage.Turkish,
            "Europe/Istanbul");
        FakeSnapshotAcquirer acquirer = new(Snapshot(Source()));
        FakeParserClient parser = new();
        ScheduleSourcePoller poller = new(
            acquirer,
            new FakeSnapshotStore(StoredSnapshot(Source(), Snapshot(Source())), changed: true),
            parser,
            new FakeParseResultStore(shouldInvokeParser: true),
            TimeProvider.System);

        ScheduleSourcePollResult result = await poller.PollAsync(source, CancellationToken.None);

        Assert.Equal(ScheduleSourcePollOutcome.UnsupportedTransport, result.Outcome);
        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(0, parser.CallCount);
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
            snapshot.AcquiredAtUtc,
            snapshot.ContentHash,
            snapshot.ContractVersion,
            System.Text.Json.JsonSerializer.Serialize(
                snapshot,
                Sirkadiyen.Contracts.Serialization.ContractJson.CreateOptions()),
            0,
            0,
            0);

    private sealed class FakeSnapshotAcquirer(NormalizedSpreadsheetSnapshot snapshot)
        : ISpreadsheetSnapshotAcquirer
    {
        public int CallCount { get; private set; }

        public Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
            AcquireSpreadsheetSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(snapshot with
            {
                SnapshotId = request.SnapshotId,
                AcquiredAtUtc = request.AcquiredAtUtc,
            });
        }
    }

    private sealed class FakeSnapshotStore(SourceSnapshot snapshot, bool changed)
        : ISourceSnapshotStore
    {
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
            CancellationToken cancellationToken) => Task.FromResult<SourceSnapshot?>(snapshot);
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

    private sealed class FakeParseResultStore(bool shouldInvokeParser)
        : IScheduleParseResultStore
    {
        private readonly Guid runId = Guid.CreateVersion7();

        public bool Completed { get; private set; }

        public bool Failed { get; private set; }

        public string? FailureReason { get; private set; }

        public Task<BeginParseRunResult> BeginOrResumeAsync(
            SourceSnapshot snapshot,
            ScheduleSource source,
            string correlationId,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken) => Task.FromResult(new BeginParseRunResult
            {
                ParseRunId = runId,
                Status = shouldInvokeParser ? ParseRunStatus.Running : ParseRunStatus.Completed,
                ShouldInvokeParser = shouldInvokeParser,
            });

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

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
