namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// The catalog document as a file the API may read and replace (ADR-114).
/// </summary>
/// <remarks>
/// Kept behind an interface so the editing service stays free of file-system details and can be
/// unit tested without a disk, and so the one place that knows the path also owns the atomicity
/// rule: a partially written catalog would be a worker that refuses to start.
/// </remarks>
public interface IScheduleSourceCatalogFile
{
    /// <summary>The configured absolute path, for display and diagnostics.</summary>
    string Path { get; }

    /// <summary>
    /// Reads the current document. Returns the raw text unparsed, so a document that no longer
    /// parses can still be shown and repaired.
    /// </summary>
    Task<ScheduleSourceCatalogFileContent> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Whether the process can replace the file, checked without writing to it.</summary>
    Task<bool> IsWritableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the document atomically: the new text is written to a temporary file in the same
    /// directory, flushed, and moved over the target, so a reader never observes a half-written
    /// catalog.
    /// </summary>
    Task WriteAsync(string content, CancellationToken cancellationToken);
}

/// <summary>The raw catalog text with the metadata an edit needs to be safe.</summary>
public sealed record ScheduleSourceCatalogFileContent
{
    public required string Content { get; init; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Content"/>.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Whether the file exists at all; a missing catalog reads as empty content.</summary>
    public required bool Exists { get; init; }
}
