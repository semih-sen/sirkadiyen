namespace Sirkadiyen.Application.StudentRosters;

/// <summary>The append-only history of the student roster catalog document (ADR-134).</summary>
/// <remarks>
/// Narrower than the schedule source catalog's store, and deliberately so: a roster configures no
/// persisted rows, so there is nothing to bring into step with the document and the commit is the
/// revision alone. Rows are never updated or deleted.
/// </remarks>
public interface IStudentRosterCatalogRevisionStore
{
    /// <summary>
    /// Persists one revision, writing the pre-edit baseline first when the history is still empty.
    /// </summary>
    Task CommitAsync(StudentRosterCatalogCommit commit, CancellationToken cancellationToken);

    /// <summary>Returns the newest revisions first, without their content.</summary>
    Task<IReadOnlyList<StudentRosterCatalogRevisionSummary>> ListAsync(
        int limit,
        string currentContentHash,
        CancellationToken cancellationToken);

    /// <summary>Returns one revision with its full document.</summary>
    Task<StudentRosterCatalogRevisionDetail?> FindAsync(
        Guid id,
        string currentContentHash,
        CancellationToken cancellationToken);
}
