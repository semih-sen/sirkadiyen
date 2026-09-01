using System.Text.Json;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using ContractProgramLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Acquires one source, applies the unchanged-snapshot short circuit, invokes
/// the parser when necessary, and persists the resulting candidate revision.
/// </summary>
public sealed class ScheduleSourcePoller(
    ISpreadsheetSnapshotAcquirer snapshotAcquirer,
    IDriveDocumentAcquirer driveDocumentAcquirer,
    ISourceSnapshotStore snapshotStore,
    IScheduleParserClient parserClient,
    IScheduleParseResultStore parseResultStore,
    IGroupRotationCoverageStore rotationCoverageStore,
    IScheduleSourceDateCorrectionStore dateCorrectionStore,
    ScheduleRevisionValidationService revisionValidation,
    IOperationalFreezeStore operationalFreeze,
    IWeeklyDocumentDiscovery weeklyDocumentDiscovery,
    ParseRunOptions parseRunOptions,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public Task<ScheduleSourcePollResult> PollAsync(
        ScheduleSource source,
        CancellationToken cancellationToken) =>
        PollAsync(source, forceReparse: false, cancellationToken);

    /// <param name="forceReparse">
    /// When <see langword="true"/>, a new parse run is opened even if the stored snapshot has
    /// already been parsed by this profile and version (ADR-127). Used by an operator-triggered
    /// re-poll so an unchanged document can be re-run after a profile or configuration change; the
    /// fingerprint is salted so the run's identity differs from the already-parsed one.
    /// </param>
    public async Task<ScheduleSourcePollResult> PollAsync(
        ScheduleSource source,
        bool forceReparse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // An uploaded source is not fetchable: it has no location, and its
        // evidence arrives through the administration flow instead (ADR-079). What
        // remains is the rest of the pipeline, which is the same as for any other
        // source, so this cycle continues from the snapshot already stored.
        if (source.Transport is ScheduleSourceTransport.AdministrativeUpload)
        {
            return await PollUploadedSourceAsync(source, forceReparse, cancellationToken);
        }

        if (DescribeUnreadable(source) is ScheduleSourcePollOutcome unreadable)
        {
            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = unreadable,
                SnapshotChanged = false,
            };
        }

        if (string.IsNullOrWhiteSpace(source.ExternalId))
        {
            // Both fetched transports address their document by identifier rather
            // than by the catalog's human-facing URL, so neither can be read
            // without one.
            throw new InvalidOperationException(
                $"{source.Transport} source '{source.SourceId}' has no external document ID.");
        }

        // This read happens immediately before the external acquisition. If the
        // authoritative row cannot be read, the exception escapes and no read is
        // started: failure is closed, never treated as "probably unfrozen".
        if (await operationalFreeze.IsFrozenAsync(Scope(source), cancellationToken))
        {
            return Frozen(source.SourceId, snapshotChanged: false);
        }

        // Which document this source publishes can change between cycles: the
        // weekly amphitheatre program is republished into a Drive folder rather
        // than edited in place, so the folder is the stable address and the file
        // is not (ADR-133). A source that declares no folder resolves to exactly
        // the catalogued document, which is every other source.
        WeeklyDocumentResolution document = await weeklyDocumentDiscovery.ResolveAsync(
            source,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(document.ExternalId))
        {
            throw new InvalidOperationException(
                $"Discovery for source '{source.SourceId}' resolved no document to acquire.");
        }

        DateTimeOffset acquiredAtUtc = timeProvider.GetUtcNow();
        AcquireSpreadsheetSnapshotRequest acquisition = new()
        {
            SourceId = source.SourceId.Value,
            SnapshotId = Guid.CreateVersion7().ToString("N"),
            SpreadsheetId = document.ExternalId,
            AcquiredAtUtc = acquiredAtUtc,
        };
        NormalizedSpreadsheetSnapshot snapshot = source.Transport switch
        {
            ScheduleSourceTransport.GoogleSheets =>
                await snapshotAcquirer.AcquireAsync(acquisition, cancellationToken),

            // A Drive document is downloaded and converted, and joins the pipeline
            // as the same normalized snapshot a sheet produces (ADR-083).
            ScheduleSourceTransport.GoogleDriveFile =>
                await driveDocumentAcquirer.AcquireAsync(
                    source.DocumentFormat,
                    acquisition,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Transport '{source.Transport}' reached acquisition without an adapter."),
        };

        StoreSnapshotResult stored = await snapshotStore.StoreIfChangedAsync(
            source.SourceId,
            snapshot,
            cancellationToken);

        // A freeze may be enabled while the external read is in flight. ADR-034
        // permits the immutable evidence to finish storing, but no parse run may
        // start or resume after that point.
        if (await operationalFreeze.IsFrozenAsync(Scope(source), cancellationToken))
        {
            return Describe(Frozen(source.SourceId, stored.Changed), source, document);
        }

        // The freshly acquired document is reused only when it is the one that was
        // just stored. When the content was unchanged, the parse must read the
        // stored snapshot, because that is the evidence the parse run is keyed to.
        ScheduleSourcePollResult result = await ParseStoredSnapshotAsync(
            source,
            stored.Snapshot,
            stored.Changed,
            acquired: stored.Changed ? snapshot : null,
            forceReparse,
            cancellationToken);

        return Describe(result, source, document);
    }

    /// <summary>
    /// Why this source cannot be acquired at all, or <see langword="null"/> when
    /// it can be.
    /// </summary>
    /// <remarks>
    /// A missing transport and a missing document reader are separate answers,
    /// because they need different work. `SHARED-AMPHI` waits on an HTTP adapter
    /// that does not exist; the Drive transport now reads both Office formats, so
    /// a document format it refuses is one no converter has been written for.
    /// </remarks>
    private ScheduleSourcePollOutcome? DescribeUnreadable(ScheduleSource source) =>
        source.Transport switch
        {
            ScheduleSourceTransport.GoogleSheets =>
                source.DocumentFormat is ScheduleDocumentFormat.GoogleSheet
                    ? null
                    : ScheduleSourcePollOutcome.UnsupportedDocumentFormat,

            ScheduleSourceTransport.GoogleDriveFile =>
                driveDocumentAcquirer.CanAcquire(source.DocumentFormat)
                    ? null
                    : ScheduleSourcePollOutcome.UnsupportedDocumentFormat,

            _ => ScheduleSourcePollOutcome.UnsupportedTransport,
        };

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
        bool forceReparse,
        CancellationToken cancellationToken)
    {
        if (await operationalFreeze.IsFrozenAsync(Scope(source), cancellationToken))
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
            forceReparse,
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
        bool forceReparse,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.CreateVersion7().ToString("N");

        // Resolved before the run is opened, because the companion evidence is
        // part of what identifies the run: an edited bedside document must open a
        // new run rather than be short-circuited as already parsed (ADR-102).
        IReadOnlyList<SourceSnapshot> companions = await ResolveCompanionsAsync(
            source,
            cancellationToken);

        // Resolved before the run is opened for the same reason, and it changes
        // more often than a companion does: uploading the anatomy group list has
        // to reparse the annual program, or every dissection hour it published as
        // a fallback would stay on the calendar beside the real one (ADR-126).
        IReadOnlyList<DateOnly> rotationCoverage = await ResolveRotationCoverageAsync(
            source,
            cancellationToken);

        // Read before the run is opened for the same reason again: accepting a
        // correction changes what this parse reads out of a document that has not
        // moved, so it has to be in the run's key or the correction would never
        // be applied to anything (ADR-139).
        IReadOnlyList<ScheduleSourceDateCorrection> dateCorrections =
            await dateCorrectionStore.ListForSourceAsync(source.SourceId, cancellationToken);

        string companionFingerprint = ParseRunCompanionFingerprint.Compute(
            [.. companions.Select(static companion =>
                new CompanionEvidence(companion.SourceId, companion.ContentHash))],
            rotationCoverage,
            [.. dateCorrections.Select(static correction =>
                new DateCorrectionEvidence(correction.Original, correction.Corrected))]);

        // A forced re-poll replaces the fingerprint with a unique token so the run's identity
        // differs from the already-parsed one, which is what makes BeginOrResumeAsync open a fresh
        // run for an unchanged snapshot and profile rather than short-circuit as already parsed
        // (ADR-127). A one-off forced run needs no future matching, so encoding the companion
        // evidence into it is unnecessary; the token stays well within the fingerprint length limit.
        if (forceReparse)
        {
            companionFingerprint = $"force:{Guid.CreateVersion7():N}";
        }

        BeginParseRunResult parseRun = await parseResultStore.BeginOrResumeAsync(
            stored,
            source,
            correlationId,
            timeProvider.GetUtcNow(),
            parseRunOptions.StaleRunTimeout,
            companionFingerprint,
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
            correlationId,
            [.. companions.Select(Deserialize)],
            rotationCoverage,
            dateCorrections);

        try
        {
            ParseSnapshotResponse response = await parserClient.ParseAsync(
                request,
                cancellationToken);
            ParseCompletion completion = await parseResultStore.CompleteAsync(
                parseRun.ParseRunId,
                response,
                timeProvider.GetUtcNow(),
                cancellationToken);

            // Validation runs in its own transaction. A revision that survives
            // parse persistence but not validation stays in Parsed and is picked
            // up by the next pass, rather than being lost or silently published.
            RevisionValidationResult? validation = completion.Revision is null
                ? null
                : await revisionValidation.ValidateAsync(completion.Revision.Id, cancellationToken);

            return new ScheduleSourcePollResult
            {
                SourceId = source.SourceId,
                Outcome = completion.Outcome switch
                {
                    ParseCompletionOutcome.ParserRejected =>
                        ScheduleSourcePollOutcome.ParserRejected,

                    // A cycle that read the document, parsed it, and found the
                    // schedule unchanged. It is reported as its own outcome rather
                    // than as a plain parse, because "parsed and produced nothing"
                    // is the answer an operator is looking for when they ask why a
                    // source shows no new revision.
                    ParseCompletionOutcome.UnchangedRecordSet =>
                        ScheduleSourcePollOutcome.ParsedUnchanged,

                    _ => ScheduleSourcePollOutcome.Parsed,
                },
                SnapshotChanged = snapshotChanged,
                ParseRunId = parseRun.ParseRunId,
                ParseRunStartKind = parseRun.StartKind,
                RevisionId = completion.Revision?.Id,
                UnchangedFromRevisionId = completion.UnchangedFromRevisionId,
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

    /// <summary>
    /// The latest usable snapshot of each companion this source's parser reads
    /// alongside its own document, in the order the source names them (ADR-102).
    /// </summary>
    /// <remarks>
    /// A companion that has never been acquired, or whose payload retention has
    /// removed the document, is simply left out. The parser is required to
    /// degrade — the Grade 3 annual publishes its bedside sessions with no topic
    /// line rather than not at all — so a companion that cannot be read must
    /// never hold up the schedule it merely annotates.
    /// <para>
    /// The same test decides both what is sent and what the fingerprint covers,
    /// so the run's identity always describes exactly the evidence it read.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<SourceSnapshot>> ResolveCompanionsAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        if (source.CompanionSourceIds.Count == 0)
        {
            return [];
        }

        List<SourceSnapshot> companions = new(source.CompanionSourceIds.Count);
        foreach (SourceId companionId in source.CompanionSourceIds)
        {
            SourceSnapshot? companion = await snapshotStore.GetLatestAsync(
                companionId,
                cancellationToken);
            if (companion?.Payload is not null)
            {
                companions.Add(companion);
            }
        }

        return companions;
    }

    /// <summary>
    /// The dates the sources owning this source's group rotation have published
    /// for this source's own program (ADR-126).
    /// </summary>
    /// <remarks>
    /// A source that names no rotation owner asks nothing and sends nothing, so
    /// it parses exactly as it did before the fallback existed. The read is over
    /// published records rather than over snapshots: a group list that has been
    /// acquired but not published says nothing to a student yet, and must not
    /// silence the hours the annual program is publishing in its place.
    /// </remarks>
    private async Task<IReadOnlyList<DateOnly>> ResolveRotationCoverageAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        if (source.GroupRotationSourceIds.Count == 0)
        {
            return [];
        }

        return await rotationCoverageStore.ListPublishedDatesAsync(
            source.GroupRotationSourceIds,
            source.AcademicYear,
            source.ClassYear,
            source.ProgramLanguage,
            cancellationToken);
    }

    private static NormalizedSpreadsheetSnapshot Deserialize(SourceSnapshot companion) =>
        JsonSerializer.Deserialize<NormalizedSpreadsheetSnapshot>(
            companion.RequirePayload(),
            JsonOptions)
            ?? throw new InvalidDataException(
                $"The stored snapshot payload for companion '{companion.SourceId}' is empty.");

    private static ParseSnapshotRequest CreateParseRequest(
        ScheduleSource source,
        NormalizedSpreadsheetSnapshot snapshot,
        string correlationId,
        IReadOnlyList<NormalizedSpreadsheetSnapshot> auxiliarySnapshots,
        IReadOnlyList<DateOnly> rotationCoverage,
        IReadOnlyList<ScheduleSourceDateCorrection> dateCorrections) => new()
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
                    Sirkadiyen.Domain.Scheduling.Sources.ProgramLanguage.Turkish =>
                        ContractProgramLanguage.Turkish,
                    Sirkadiyen.Domain.Scheduling.Sources.ProgramLanguage.English =>
                        ContractProgramLanguage.English,
                    _ => throw new InvalidOperationException(
                        $"Unsupported program language '{source.ProgramLanguage}'."),
                },
                TimeZoneId = source.TimeZoneId,
                // Which half of a shared session this source publishes is configuration the
                // workbook does not state, so it travels with the rest of the source context
                // (ADR-017, ADR-110).
                AuthoritativeAudienceSelectors =
                    source.AuthoritativeAudienceSelectors
                    ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),

                // Whether another document has already published a rotation date is
                // orchestration knowledge the workbook cannot hold, so it travels
                // with the rest of the source context too (ADR-017, ADR-126).
                GroupRotationCoveredDates = rotationCoverage,

                // A typo in the document is a fact about the document that the
                // document cannot state, so an operator's decision about one
                // travels as source context rather than as an edit to a parsed
                // record (ADR-017, ADR-139). That is what keeps re-parsing the
                // same snapshot produce the same records.
                DateCorrections = [.. dateCorrections.Select(static correction =>
                    new SourceDateCorrection
                    {
                        Original = correction.Original,
                        Corrected = correction.Corrected,
                        DecidedBy = correction.DecidedBy,
                        DecidedAt = correction.DecidedAtUtc.ToString("O"),
                    })],
            },
            Snapshot = snapshot,
            AuxiliarySnapshots = auxiliarySnapshots,
        };

    private static string FormatFailure(Exception exception)
    {
        const int maximumLength = 1900;
        string failure = $"{exception.GetType().Name}: {exception.Message}";
        return failure.Length <= maximumLength ? failure : failure[..maximumLength];
    }

    /// <summary>
    /// Records on the result how this cycle chose its document, for a source that
    /// declares a discovery folder (ADR-133).
    /// </summary>
    /// <remarks>
    /// Reported even when the poll succeeded, because a discovery fallback is a
    /// success that quietly stops tracking the source. Nothing is attached for a
    /// source that declares no folder, which is every source but one.
    /// </remarks>
    private static ScheduleSourcePollResult Describe(
        ScheduleSourcePollResult result,
        ScheduleSource source,
        WeeklyDocumentResolution document) =>
        string.IsNullOrWhiteSpace(source.DiscoveryFolderId)
            ? result
            : result with
            {
                DiscoveryOutcome = document.Outcome,
                DiscoveryFailure = document.Failure,
                DiscoveredDocumentName = document.DocumentName,
            };

    private static ScheduleSourcePollResult Frozen(SourceId sourceId, bool snapshotChanged) =>
        new()
        {
            SourceId = sourceId,
            Outcome = ScheduleSourcePollOutcome.Frozen,
            SnapshotChanged = snapshotChanged,
        };
    private static OperationalFreezeScope Scope(ScheduleSource source) => new()
    {
        ClassYear = source.ClassYear,
        ProgramLanguage = source.ProgramLanguage,
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

    /// <summary>
    /// The revision this cycle's parse turned out to repeat, when it repeated one
    /// and therefore created none.
    /// </summary>
    public Guid? UnchangedFromRevisionId { get; init; }

    /// <summary>The state validation moved the revision to, when it ran.</summary>
    public RevisionState? RevisionState { get; init; }

    public int? ValidationFindingCount { get; init; }

    /// <summary>
    /// How this cycle decided which document to acquire, for a source whose
    /// document is republished into a folder rather than edited in place
    /// (ADR-133). Null for every source that declares no discovery folder.
    /// </summary>
    /// <remarks>
    /// This is the only place a silent degradation becomes visible. Discovery
    /// deliberately never fails a cycle: a folder it cannot list falls back to the
    /// catalogued document, which keeps acquiring successfully while quietly
    /// freezing on last week's file. An operator has to be able to see that
    /// happening, so the outcome is reported even though the poll succeeded.
    /// </remarks>
    public WeeklyDocumentDiscoveryOutcome? DiscoveryOutcome { get; init; }

    /// <summary>Why the discovery folder could not be listed, when that is why it fell back.</summary>
    public DriveDocumentFailure? DiscoveryFailure { get; init; }

    /// <summary>The document discovery resolved, when it resolved one.</summary>
    public string? DiscoveredDocumentName { get; init; }
}

public enum ScheduleSourcePollOutcome
{
    Frozen,

    /// <summary>Nothing can fetch this source's document from where it is published.</summary>
    UnsupportedTransport,

    /// <summary>
    /// The document can be fetched, but nothing can read the format it is
    /// published in.
    /// </summary>
    UnsupportedDocumentFormat,

    /// <summary>The source is uploaded by an administrator and has nothing to poll.</summary>
    AwaitingAdministrativeUpload,

    ParseAlreadyRunning,

    /// <summary>The stored snapshot was already parsed under this run identity.</summary>
    AlreadyParsed,

    Parsed,

    /// <summary>
    /// The document was parsed and said exactly what the source's most recent
    /// revision already says, so no revision was created.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="AlreadyParsed"/>, which means no parse happened at
    /// all. This one did the work and found nothing to publish — the ordinary
    /// outcome for a source re-parsed because a companion document moved.
    /// </remarks>
    ParsedUnchanged,

    ParserRejected,
}
