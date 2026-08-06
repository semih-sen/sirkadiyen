using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Scheduling.Ingestion;

/// <summary>
/// An immutable record of what a source contained at one moment.
/// </summary>
/// <remarks>
/// A snapshot is evidence (ADR-007). Nothing in the system may rewrite one: a
/// parser run is explained by the snapshot it read, and a rewritten snapshot
/// would make past decisions unexplainable. The only post-acquisition mutation
/// is the explicit ADR-044 retention deletion of an expired payload; metadata
/// and every downstream decision remain.
/// </remarks>
public sealed class SourceSnapshot
{
    private SourceSnapshot()
    {
        // Materialization constructor.
        ExternalSnapshotId = string.Empty;
        SpreadsheetId = string.Empty;
        AcademicYear = string.Empty;
        ContentHash = string.Empty;
        ContractVersion = string.Empty;
        Payload = string.Empty;
    }

    public SourceSnapshot(
        Guid scheduleSourceId,
        SourceId sourceId,
        string externalSnapshotId,
        string spreadsheetId,
        string academicYear,
        DateTimeOffset acquiredAtUtc,
        string contentHash,
        string contractVersion,
        string payload,
        int worksheetCount,
        int cellCount,
        int diagnosticCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (acquiredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A snapshot acquisition time must be expressed in UTC.",
                nameof(acquiredAtUtc));
        }

        Id = Guid.CreateVersion7();
        ScheduleSourceId = scheduleSourceId;
        SourceId = sourceId;
        ExternalSnapshotId = externalSnapshotId;
        SpreadsheetId = spreadsheetId;
        AcademicYear = academicYear;
        AcquiredAtUtc = acquiredAtUtc;
        ContentHash = contentHash;
        ContractVersion = contractVersion;
        Payload = payload;
        WorksheetCount = worksheetCount;
        CellCount = cellCount;
        DiagnosticCount = diagnosticCount;
    }

    public Guid Id { get; private set; }

    public Guid ScheduleSourceId { get; private set; }

    public SourceId SourceId { get; private set; }

    /// <summary>The snapshot identifier the acquisition layer assigned.</summary>
    public string ExternalSnapshotId { get; private set; }

    public string SpreadsheetId { get; private set; }

    /// <summary>
    /// The source's configured academic year when this evidence was acquired.
    /// </summary>
    /// <remarks>
    /// The source row is updated when a new academic year begins. Copying the
    /// value here lets retention preserve the correct first snapshot of each
    /// active year instead of reclassifying historical evidence.
    /// </remarks>
    public string AcademicYear { get; private set; }

    public DateTimeOffset AcquiredAtUtc { get; private set; }

    /// <summary>
    /// The hash of the normalized content, excluding acquisition metadata
    /// (ADR-014). Change detection compares this value.
    /// </summary>
    public string ContentHash { get; private set; }

    public string ContractVersion { get; private set; }

    /// <summary>The normalized snapshot document, exactly as it was acquired.</summary>
    public string? Payload { get; private set; }

    /// <summary>
    /// When the normalized document was removed under the retention policy.
    /// Metadata, hashes and downstream parse/revision evidence remain.
    /// </summary>
    public DateTimeOffset? PayloadPrunedAtUtc { get; private set; }

    public int WorksheetCount { get; private set; }

    public int CellCount { get; private set; }

    public int DiagnosticCount { get; private set; }

    /// <summary>
    /// Returns the normalized document or fails explicitly when retention has
    /// removed it.
    /// </summary>
    public string RequirePayload() =>
        Payload
        ?? throw new InvalidOperationException(
            $"Snapshot '{Id}' no longer has a retained payload.");

    /// <summary>
    /// Removes the large normalized document without changing any evidence
    /// metadata. This is the sole allowed post-acquisition mutation.
    /// </summary>
    public void PrunePayload(DateTimeOffset prunedAtUtc)
    {
        if (prunedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A payload prune time must be expressed in UTC.",
                nameof(prunedAtUtc));
        }

        if (Payload is null)
        {
            return;
        }

        Payload = null;
        PayloadPrunedAtUtc = prunedAtUtc;
    }
}
