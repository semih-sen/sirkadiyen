using Sirkadiyen.Domain.StudentRosters;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// The roster catalog document as it is on disk right now, whether or not it parses.
/// </summary>
/// <remarks>
/// A document that does not parse is still returned, with the reason. The editor is the tool for
/// fixing a broken catalog, so refusing to show a broken one would leave the operator with a
/// server shell as their only repair path.
/// </remarks>
public sealed record StudentRosterCatalogDocument
{
    public required string Path { get; init; }

    public required string Content { get; init; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Content"/>; the concurrency token for an edit.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Whether the API process can write the file back.</summary>
    public required bool IsWritable { get; init; }

    public required bool IsValid { get; init; }

    /// <summary>Why the document does not parse or validate, when it does not.</summary>
    public string? ValidationError { get; init; }

    public string? CatalogVersion { get; init; }

    public int? RosterCount { get; init; }
}

/// <summary>
/// What applying a proposed roster catalog would change, and the hash that binds a confirmation
/// to it.
/// </summary>
public sealed record StudentRosterCatalogPlan
{
    /// <summary>
    /// SHA-256 over the base and proposed content hashes. A confirmation carrying a different
    /// value was computed against a different pair of documents and is refused.
    /// </summary>
    public required string PlanHash { get; init; }

    /// <summary>The on-disk hash this plan was computed against.</summary>
    public required string BaseContentHash { get; init; }

    /// <summary>The hash of the exact text that would be written.</summary>
    public required string ProposedContentHash { get; init; }

    /// <summary>The text that would be written: the submitted document, line-ending normalized.</summary>
    public required string NormalizedContent { get; init; }

    public required int RosterCount { get; init; }

    public required IReadOnlyList<StudentRosterCatalogRosterChange> Added { get; init; }

    public required IReadOnlyList<StudentRosterCatalogRosterChange> Removed { get; init; }

    public required IReadOnlyList<StudentRosterCatalogRosterChange> Modified { get; init; }

    public required int UnchangedCount { get; init; }

    /// <summary>Consequences the operator must read before confirming; never blocking on their own.</summary>
    public required IReadOnlyList<StudentRosterCatalogWarning> Warnings { get; init; }

    public bool HasHighRiskChange =>
        Added.Concat(Removed).Concat(Modified).Any(change => change.IsHighRisk);

    public bool HasChanges => Added.Count + Removed.Count + Modified.Count > 0;
}

public sealed record StudentRosterCatalogRosterChange
{
    public required string RosterId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>A human label for the cohort this list enrols.</summary>
    public required string Cohort { get; init; }

    public required StudentRosterCatalogChangeKind Kind { get; init; }

    /// <summary>The per-field before/after, empty for an addition or a removal.</summary>
    public required IReadOnlyList<StudentRosterCatalogFieldChange> Fields { get; init; }

    public required bool IsHighRisk { get; init; }
}

public sealed record StudentRosterCatalogFieldChange
{
    public required string Field { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    public required StudentRosterCatalogChangeRisk Risk { get; init; }
}

public sealed record StudentRosterCatalogWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required StudentRosterCatalogChangeRisk Risk { get; init; }
}

public enum StudentRosterCatalogChangeKind
{
    Added,
    Removed,
    Modified,
}

public enum StudentRosterCatalogChangeRisk
{
    /// <summary>Presentation or documentation only; nothing the lookup reads changes.</summary>
    Low,

    /// <summary>
    /// Changes which document is read for a cohort, or what its columns are taken to mean. Either
    /// can fill a student's profile with values nobody intended, without any lookup failing.
    /// </summary>
    High,
}

/// <summary>The outcome of an applied roster catalog edit.</summary>
public sealed record StudentRosterCatalogApplyResult
{
    public required Guid RevisionId { get; init; }

    public required string ContentHash { get; init; }

    public required DateTimeOffset AppliedAtUtc { get; init; }

    /// <summary>
    /// Whether the held reading of the lists was dropped, so the next lookup reads the documents
    /// the new catalog names instead of the ones the old one did.
    /// </summary>
    public required bool ReadingInvalidated { get; init; }

    public required StudentRosterCatalogPlan Plan { get; init; }
}

/// <summary>One stored roster catalog revision, without its content.</summary>
public sealed record StudentRosterCatalogRevisionSummary
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }

    public required string ContentHash { get; init; }

    public string? PreviousContentHash { get; init; }

    public required int RosterCount { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? Reason { get; init; }

    public string? ChangeSummary { get; init; }

    /// <summary>Whether this revision's content is what the file holds right now.</summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>One stored roster catalog revision with its full document, for review or restore.</summary>
public sealed record StudentRosterCatalogRevisionDetail
{
    public required StudentRosterCatalogRevisionSummary Summary { get; init; }

    public required string Content { get; init; }
}

/// <summary>Everything an applied roster catalog edit needs, so no call site can forget the actor.</summary>
public sealed record StudentRosterCatalogApplyCommand
{
    public required string Content { get; init; }

    public required string BaseContentHash { get; init; }

    public required string PlanHash { get; init; }

    public required string Reason { get; init; }

    public required Guid ActorUserId { get; init; }

    public required string ActorEmail { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>
/// One roster catalog commit: the revision, and the baseline it may have to record first.
/// </summary>
/// <remarks>
/// Narrower than the schedule source catalog's commit, which also has persisted source rows to
/// bring into step with the document. A roster configures no rows: the file is the whole state,
/// so the revision is the whole commit.
/// </remarks>
public sealed record StudentRosterCatalogCommit
{
    public required StudentRosterCatalogRevision Revision { get; init; }

    /// <summary>
    /// The content that was on disk before this edit, recorded as the first history entry when the
    /// history is still empty. Ignored once any revision exists.
    /// </summary>
    public required StudentRosterCatalogBaselineDraft? Baseline { get; init; }
}

public sealed record StudentRosterCatalogBaselineDraft
{
    public required string Content { get; init; }

    public required string ContentHash { get; init; }

    public required int RosterCount { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }
}

/// <summary>
/// Raised when the document on disk is not the one an edit was prepared against, or the
/// confirmation does not match the plan it claims.
/// </summary>
public sealed class StudentRosterCatalogConflictException(string message)
    : InvalidOperationException(message);
