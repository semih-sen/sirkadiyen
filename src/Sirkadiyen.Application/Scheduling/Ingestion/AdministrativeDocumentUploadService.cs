using System.Security.Cryptography;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Acquires a source whose document is handed out rather than published, by
/// accepting an administrator's upload and storing it as immutable evidence
/// (ADR-079, ADR-080).
/// </summary>
/// <remarks>
/// This is the acquisition half of the pipeline for an upload source, and nothing
/// more. It converts and stores; it does not parse, validate or publish. The
/// worker owns those, exactly as it does for a polled source, so an uploaded
/// document goes through the same parse run, the same validation thresholds and
/// the same publication rules as one that was fetched.
/// <para>
/// One upload can serve several sources. The Grade 2 anatomy group list is handed
/// to the Turkish and the English program as a single document, and each program
/// needs its own revision because a canonical record reaches a student only when
/// its program language matches theirs. Asking an administrator to upload the
/// identical file once per program would make double-uploading the normal case and
/// a half-finished pair the routine failure.
/// </para>
/// </remarks>
public sealed class AdministrativeDocumentUploadService(
    IScheduleSourceStore sourceStore,
    ISourceSnapshotStore snapshotStore,
    ISourceDocumentUploadAuditStore auditStore,
    IUploadedDocumentConverter converter,
    IOperationalFreezeStore operationalFreeze,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The largest document accepted. The real ones are tens of kilobytes; this
    /// bounds what one request can make the host read into memory.
    /// </summary>
    public const long MaximumDocumentBytes = 8 * 1024 * 1024;

    public async Task<DocumentUploadResult> UploadAsync(
        DocumentUploadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SourceId.TryParse(request.SourceId, out SourceId sourceId))
        {
            return Rejected(DocumentUploadOutcome.SourceNotFound, $"'{request.SourceId}' is not a valid source identifier.");
        }

        ScheduleSource? source = await sourceStore.FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return Rejected(
                DocumentUploadOutcome.SourceNotFound,
                $"Source '{sourceId}' is not configured.");
        }

        // Uploading over a fetched source would replace evidence the pipeline is
        // supposed to acquire from the source's own location, and the next poll
        // would silently overwrite it again.
        if (source.Transport is not ScheduleSourceTransport.AdministrativeUpload)
        {
            return Rejected(
                DocumentUploadOutcome.SourceIsNotUploadable,
                $"Source '{sourceId}' is acquired by {source.Transport}, not by upload.");
        }

        if (request.Content.Length == 0)
        {
            return Rejected(DocumentUploadOutcome.EmptyDocument, "The uploaded document is empty.");
        }

        if (request.Content.Length > MaximumDocumentBytes)
        {
            return Rejected(
                DocumentUploadOutcome.DocumentTooLarge,
                $"The uploaded document is larger than {MaximumDocumentBytes} bytes.");
        }

        // The global emergency stop still rejects the whole acquisition. Scoped controls below
        // skip only their own targets when one shared document serves several pipelines.
        if ((await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return Rejected(
                DocumentUploadOutcome.Frozen,
                "The global pipeline freeze is active, so no source may be acquired.");
        }

        IReadOnlyList<ScheduleSource> targets = await sourceStore.ListSharingDocumentAsync(
            sourceId,
            cancellationToken);

        if (!converter.CanConvert(source.DocumentFormat, request.FileName))
        {
            return Rejected(
                DocumentUploadOutcome.UnsupportedDocumentFormat,
                $"Source '{sourceId}' expects a {source.DocumentFormat} document and the uploaded "
                + $"file '{request.FileName}' is not one.");
        }

        string digest = Convert.ToHexStringLower(SHA256.HashData(request.Content.Span));
        DateTimeOffset uploadedAtUtc = timeProvider.GetUtcNow();

        // Every target acquires its own snapshot from the same bytes. They are
        // stored one at a time rather than in one transaction: each source's
        // evidence is independently valid, and a retry of the same upload is a
        // no-op for whichever targets already have it.
        List<DocumentUploadTargetResult> results = [];
        foreach (ScheduleSource target in targets)
        {
            OperationalFreezeScope scope = new()
            {
                ClassYear = target.ClassYear,
                ProgramLanguage = target.ProgramLanguage,
            };
            if (await operationalFreeze.IsFrozenAsync(scope, cancellationToken))
            {
                results.Add(new DocumentUploadTargetResult
                {
                    SourceId = target.SourceId.Value,
                    ClassYear = target.ClassYear,
                    ProgramLanguage = target.ProgramLanguage,
                    Outcome = SourceDocumentUploadOutcome.Frozen,
                });
                continue;
            }

            NormalizedSpreadsheetSnapshot snapshot = converter.Convert(
                target.DocumentFormat,
                request.Content,
                new AcquireSpreadsheetSnapshotRequest
                {
                    SourceId = target.SourceId.Value,

                    // Deterministic in the bytes, so re-uploading the same file
                    // cannot look like a different acquisition.
                    SnapshotId = $"upload:sha256:{digest}",
                    SpreadsheetId = $"upload:sha256:{digest}",
                    AcquiredAtUtc = uploadedAtUtc,
                });

            StoreSnapshotResult stored = await snapshotStore.StoreIfChangedAsync(
                target.SourceId,
                snapshot,
                cancellationToken);

            SourceDocumentUploadOutcome outcome = stored.Changed
                ? SourceDocumentUploadOutcome.Stored
                : SourceDocumentUploadOutcome.Unchanged;

            await auditStore.AppendAsync(
                new SourceDocumentUpload(
                    target.SourceId,
                    target.Id,
                    request.UploadedBy,
                    request.FileName,
                    request.Content.Length,
                    digest,
                    outcome,
                    stored.Snapshot.Id,
                    request.CorrelationId,
                    uploadedAtUtc),
                cancellationToken);

            results.Add(new DocumentUploadTargetResult
            {
                SourceId = target.SourceId.Value,
                ClassYear = target.ClassYear,
                ProgramLanguage = target.ProgramLanguage,
                Outcome = outcome,
                SnapshotId = stored.Snapshot.Id,
            });
        }

        return new DocumentUploadResult
        {
            Outcome = DocumentUploadOutcome.Accepted,
            ContentSha256 = digest,
            Targets = results,
        };
    }

    private static DocumentUploadResult Rejected(DocumentUploadOutcome outcome, string detail) =>
        new()
        {
            Outcome = outcome,
            Detail = detail,
            Targets = [],
        };
}
