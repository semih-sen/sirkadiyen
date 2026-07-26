using System.Text.Json;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using ContractProgramLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;

namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>
/// Acquires one source, applies the unchanged-snapshot short circuit, invokes
/// the parser when necessary, and persists the resulting candidate revision.
/// </summary>
public sealed class ScheduleSourcePoller(
    ISpreadsheetSnapshotAcquirer snapshotAcquirer,
    ISourceSnapshotStore snapshotStore,
    IScheduleParserClient parserClient,
    IScheduleParseResultStore parseResultStore,
    ScheduleRevisionValidationService revisionValidation,
    IOperationalFreezeStore operationalFreeze,
    ParseRunOptions parseRunOptions,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public async Task<ScheduleSourcePollResult> PollAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // An uploaded source is not fetchable: it has no location, and its
        // evidence arrives through the administration flow instead (ADR-079). What
        // remains is the rest of the pipeline, which is the same as for any other
        // source, so this cycle continues from the snapshot already stored.
        if (source.Transport is ScheduleSourceTransport.AdministrativeUpload)
        {
            return await PollUploadedSourceAsync(source, cancellationToken);
        }

        if (source.Transport is not ScheduleSourceTransport.GoogleSheets
            || source.DocumentFormat is not ScheduleDocumentFormat.GoogleSheet)
        {
            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = ScheduleSourcePollOutcome.UnsupportedTransport,
                SnapshotChanged = false,
            };
        }

        if (string.IsNullOrWhiteSpace(source.ExternalId))
        {
            throw new InvalidOperationException(
                $"Google Sheets source '{source.SourceId}' has no spreadsheet ID.");
        }

        // This read happens immediately before the external acquisition. If the
        // authoritative row cannot be read, the exception escapes and no read is
        // started: failure is closed, never treated as "probably unfrozen".
        if ((await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return Frozen(source.SourceId, snapshotChanged: false);
        }

        DateTimeOffset acquiredAtUtc = timeProvider.GetUtcNow();
        NormalizedSpreadsheetSnapshot snapshot = await snapshotAcquirer.AcquireAsync(
            new AcquireSpreadsheetSnapshotRequest
            {
                SourceId = source.SourceId.Value,
                SnapshotId = Guid.CreateVersion7().ToString("N"),
                SpreadsheetId = source.ExternalId,
                AcquiredAtUtc = acquiredAtUtc,
            },
            cancellationToken);

        StoreSnapshotResult stored = await snapshotStore.StoreIfChangedAsync(
            source.SourceId,
            snapshot,
            cancellationToken);

        // A freeze may be enabled while the external read is in flight. ADR-034
        // permits the immutable evidence to finish storing, but no parse run may
        // start or resume after that point.
        if ((await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return Frozen(source.SourceId, stored.Changed);
        }

        // The freshly acquired document is reused only when it is the one that was
        // just stored. When the content was unchanged, the parse must read the
        // stored snapshot, because that is the evidence the parse run is keyed to.
        return await ParseStoredSnapshotAsync(
            source,
            stored.Snapshot,
            stored.Changed,
            acquired: stored.Changed ? snapshot : null,
            cancellationToken);
    }

    /// <summary>
    /// Continues an administratively uploaded source from the evidence already
    /// stored for it, or reports that none has been supplied yet (ADR-080).
    /// </summary>
    /// <remarks>
    /// The upload endpoint stores the snapshot and stops there, so this is where
    /// an uploaded document meets the same parse run, validation thresholds and
    /// publication rules as a fetched one. Re-entering every cycle is safe: a
    /// parse run is keyed by snapshot, profile and profile version, so the second
    /// pass reports <see cref="ScheduleSourcePollOutcome.AlreadyParsed"/> instead
    /// of parsing again.
    /// </remarks>
    private async Task<ScheduleSourcePollResult> PollUploadedSourceAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        if ((await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return Frozen(source.SourceId, snapshotChanged: false);
        }

        SourceSnapshot? stored = await snapshotStore.GetLatestAsync(
            source.SourceId,
            cancellationToken);
        if (stored is null)
        {
            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = ScheduleSourcePollOutcome.AwaitingAdministrativeUpload,
                SnapshotChanged = false,
            };
        }

        // Nothing was acquired in this cycle; the administrator acquired it when
        // they uploaded, which is what SnapshotChanged reports about a poll.
        return await ParseStoredSnapshotAsync(
            source,
            stored,
            snapshotChanged: false,
            acquired: null,
            cancellationToken);
    }

    /// <summary>
    /// Parses one stored snapshot and validates whatever revision it produces,
    /// whichever way the snapshot was acquired.
    /// </summary>
    private async Task<ScheduleSourcePollResult> ParseStoredSnapshotAsync(
        ScheduleSource source,
        SourceSnapshot stored,
        bool snapshotChanged,
        NormalizedSpreadsheetSnapshot? acquired,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.CreateVersion7().ToString("N");
        BeginParseRunResult parseRun = await parseResultStore.BeginOrResumeAsync(
            stored,
            source,
            correlationId,
            timeProvider.GetUtcNow(),
            parseRunOptions.StaleRunTimeout,
            cancellationToken);

        if (!parseRun.ShouldInvokeParser)
        {
            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = parseRun.Status is ParseRunStatus.Running
                    ? ScheduleSourcePollOutcome.ParseAlreadyRunning
                    : ScheduleSourcePollOutcome.AlreadyParsed,
                SnapshotChanged = snapshotChanged,
                ParseRunId = parseRun.ParseRunId,
            };
        }

        // Read after the parse-run decision, so a snapshot that needs no parse is
        // never blocked by a payload retention already pruned (ADR-044).
        NormalizedSpreadsheetSnapshot snapshotForParsing = acquired
            ?? JsonSerializer.Deserialize<NormalizedSpreadsheetSnapshot>(
                stored.RequirePayload(),
                JsonOptions)
                ?? throw new InvalidDataException(
                    "The stored immutable snapshot payload is empty.");

        ParseSnapshotRequest request = CreateParseRequest(
            source,
            snapshotForParsing,
            correlationId);

        try
        {
            ParseSnapshotResponse response = await parserClient.ParseAsync(
                request,
                cancellationToken);
            ScheduleRevision? revision = await parseResultStore.CompleteAsync(
                parseRun.ParseRunId,
                response,
                timeProvider.GetUtcNow(),
                cancellationToken);

            // Validation runs in its own transaction. A revision that survives
            // parse persistence but not validation stays in Parsed and is picked
            // up by the next pass, rather than being lost or silently published.
            RevisionValidationResult? validation = revision is null
                ? null
                : await revisionValidation.ValidateAsync(revision.Id, cancellationToken);

            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = response.Status is ParserResultStatus.Rejected
                    ? ScheduleSourcePollOutcome.ParserRejected
                    : ScheduleSourcePollOutcome.Parsed,
                SnapshotChanged = snapshotChanged,
                ParseRunId = parseRun.ParseRunId,
                ParseRunStartKind = parseRun.StartKind,
                RevisionId = revision?.Id,
                RevisionState = validation?.Outcome,
                ValidationFindingCount = validation?.Findings.Count,
            };
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await parseResultStore.FailAsync(
                parseRun.ParseRunId,
                timeProvider.GetUtcNow(),
                FormatFailure(exception),
                cancellationToken);
            throw;
        }
    }

    private static ParseSnapshotRequest CreateParseRequest(
        ScheduleSource source,
        NormalizedSpreadsheetSnapshot snapshot,
        string correlationId) => new()
        {
            ContractVersion = ParserContractVersions.V1,
            CorrelationId = correlationId,
            ParserProfile = new ParserProfileDescriptor
            {
                Name = source.ParserProfile,
                Version = source.ParserProfileVersion,
            },
            SourceContext = new ParseSourceContext
            {
                AcademicYear = source.AcademicYear,
                ClassYear = source.ClassYear,
                ProgramLanguage = source.ProgramLanguage switch
                {
                    Sirkadiyen.Domain.ScheduleSources.ProgramLanguage.Turkish =>
                        ContractProgramLanguage.Turkish,
                    Sirkadiyen.Domain.ScheduleSources.ProgramLanguage.English =>
                        ContractProgramLanguage.English,
                    _ => throw new InvalidOperationException(
                        $"Unsupported program language '{source.ProgramLanguage}'."),
                },
                TimeZoneId = source.TimeZoneId,
            },
            Snapshot = snapshot,
        };

    private static string FormatFailure(Exception exception)
    {
        const int maximumLength = 1900;
        string failure = $"{exception.GetType().Name}: {exception.Message}";
        return failure.Length <= maximumLength ? failure : failure[..maximumLength];
    }

    private static ScheduleSourcePollResult Frozen(SourceId sourceId, bool snapshotChanged) =>
        new()
        {
            SourceId = sourceId,
            Outcome = ScheduleSourcePollOutcome.Frozen,
            SnapshotChanged = snapshotChanged,
        };
}

public sealed record ScheduleSourcePollResult
{
    public required SourceId SourceId { get; init; }

    public required ScheduleSourcePollOutcome Outcome { get; init; }

    public required bool SnapshotChanged { get; init; }

    public Guid? ParseRunId { get; init; }

    /// <summary>
    /// How the parse run was opened, when one was. A recovered run means a
    /// previous worker died mid-parse and is worth an operator's attention.
    /// </summary>
    public ParseRunStartKind? ParseRunStartKind { get; init; }

    public Guid? RevisionId { get; init; }

    /// <summary>The state validation moved the revision to, when it ran.</summary>
    public RevisionState? RevisionState { get; init; }

    public int? ValidationFindingCount { get; init; }
}

public enum ScheduleSourcePollOutcome
{
    Frozen,
    UnsupportedTransport,

    /// <summary>The source is uploaded by an administrator and has nothing to poll.</summary>
    AwaitingAdministrativeUpload,

    ParseAlreadyRunning,
    AlreadyParsed,
    Parsed,
    ParserRejected,
}
