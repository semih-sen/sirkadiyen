namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// The roster catalog document as a file the API may read and replace (ADR-134).
/// </summary>
/// <remarks>
/// Kept behind an interface for the reason the schedule source catalog's is (ADR-114): the editing
/// service stays free of file-system details and can be unit tested without a disk, and the one
/// place that knows the path also owns the atomicity rule.
/// </remarks>
public interface IStudentRosterCatalogFile
{
    /// <summary>The configured absolute path, for display and diagnostics.</summary>
    string Path { get; }

    /// <summary>
    /// Reads the current document. Returns the raw text unparsed, so a document that no longer
    /// parses can still be shown and repaired.
    /// </summary>
    Task<StudentRosterCatalogFileContent> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Whether the process can replace the file, checked without writing to it.</summary>
    Task<bool> IsWritableAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the document atomically, so a reader never observes a half-written file.</summary>
    Task WriteAsync(string content, CancellationToken cancellationToken);
}

/// <summary>The raw roster catalog text with the metadata an edit needs to be safe.</summary>
public sealed record StudentRosterCatalogFileContent
{
    public required string Content { get; init; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Content"/>.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Whether the file exists at all; a missing catalog reads as empty content.</summary>
    public required bool Exists { get; init; }
}
