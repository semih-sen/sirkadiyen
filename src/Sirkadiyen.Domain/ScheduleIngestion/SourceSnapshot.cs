using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.ScheduleIngestion;

/// <summary>
/// An immutable record of what a source contained at one moment.
/// </summary>
/// <remarks>
/// A snapshot is evidence (ADR-007). Nothing in the system may rewrite one: a
/// parser run is explained by the snapshot it read, and a rewritten snapshot
/// would make past decisions unexplainable. The type therefore exposes no
/// mutators at all.
/// </remarks>
public sealed class SourceSnapshot
{
    private SourceSnapshot()
    {
        // Materialization constructor.
        ExternalSnapshotId = string.Empty;
        SpreadsheetId = string.Empty;
        ContentHash = string.Empty;
        ContractVersion = string.Empty;
        Payload = string.Empty;
    }

    public SourceSnapshot(
        Guid scheduleSourceId,
        SourceId sourceId,
        string externalSnapshotId,
        string spreadsheetId,
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

    public DateTimeOffset AcquiredAtUtc { get; private set; }

    /// <summary>
    /// The hash of the normalized content, excluding acquisition metadata
    /// (ADR-014). Change detection compares this value.
    /// </summary>
    public string ContentHash { get; private set; }

    public string ContractVersion { get; private set; }

    /// <summary>The normalized snapshot document, exactly as it was acquired.</summary>
    public string Payload { get; private set; }

    public int WorksheetCount { get; private set; }

    public int CellCount { get; private set; }

    public int DiagnosticCount { get; private set; }
}
