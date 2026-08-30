namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// The catalog document as it is on disk right now, whether or not it parses.
/// </summary>
/// <remarks>
/// A document that does not parse is still returned, with the reason. The editor is the tool for
/// fixing a broken catalog, so refusing to show a broken one would leave the operator with a
/// server shell as their only repair path.
/// </remarks>
public sealed record ScheduleSourceCatalogDocument
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

    public int? SourceCount { get; init; }
}

/// <summary>
/// What applying a proposed catalog would change, and the hash that binds a confirmation to it.
/// </summary>
public sealed record ScheduleSourceCatalogPlan
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

    public required int SourceCount { get; init; }

    public required IReadOnlyList<ScheduleSourceCatalogSourceChange> Added { get; init; }

    public required IReadOnlyList<ScheduleSourceCatalogSourceChange> Removed { get; init; }

    public required IReadOnlyList<ScheduleSourceCatalogSourceChange> Modified { get; init; }

    public required int UnchangedCount { get; init; }

    /// <summary>Consequences the operator must read before confirming; never blocking on their own.</summary>
    public required IReadOnlyList<ScheduleSourceCatalogWarning> Warnings { get; init; }

    /// <summary>Whether any single field change is classified as high risk.</summary>
    public bool HasHighRiskChange =>
        Added.Concat(Removed).Concat(Modified).Any(change => change.IsHighRisk);

    public bool HasChanges => Added.Count + Removed.Count + Modified.Count > 0;
}

public sealed record ScheduleSourceCatalogSourceChange
{
    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>A human label for the audience, for example <c>Dönem 3 · Turkish</c>.</summary>
    public required string Program { get; init; }

    public required ScheduleSourceCatalogChangeKind Kind { get; init; }

    /// <summary>The per-field before/after, empty for an addition or a removal.</summary>
    public required IReadOnlyList<ScheduleSourceCatalogFieldChange> Fields { get; init; }

    public required bool IsHighRisk { get; init; }
}

public sealed record ScheduleSourceCatalogFieldChange
{
    public required string Field { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    public required ScheduleSourceCatalogChangeRisk Risk { get; init; }
}

public sealed record ScheduleSourceCatalogWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required ScheduleSourceCatalogChangeRisk Risk { get; init; }
}

public enum ScheduleSourceCatalogChangeKind
{
    Added,
    Removed,
    Modified,
}

public enum ScheduleSourceCatalogChangeRisk
{
    /// <summary>Presentation or documentation only; nothing the pipeline reads changes.</summary>
    Low,

    /// <summary>
    /// Changes what is acquired, how it is interpreted, or who receives it. Every one of these can
    /// move published lessons between students without any parse being wrong.
    /// </summary>
    High,
}

/// <summary>The outcome of an applied catalog edit.</summary>
public sealed record ScheduleSourceCatalogApplyResult
{
    public required Guid RevisionId { get; init; }

    public required string ContentHash { get; init; }

    public required DateTimeOffset AppliedAtUtc { get; init; }

    /// <summary>How many persisted source rows the edit inserted or updated.</summary>
    public required int SourceRowsChanged { get; init; }

    /// <summary>
    /// Sources the document no longer declares. Their rows are kept and their polling is
    /// disabled; nothing published from them is deleted.
    /// </summary>
    public required IReadOnlyList<string> PollingDisabledSourceIds { get; init; }

    public required ScheduleSourceCatalogPlan Plan { get; init; }
}

/// <summary>
/// What a deployment did to the catalog (ADR-138).
/// </summary>
/// <remarks>
/// <see cref="Applied"/> is false for the ordinary case: the shipped document is already the
/// document on the server, so nothing was written and no revision was cut. A history entry per
/// deployment would bury the operator edits the history exists to show.
/// </remarks>
public sealed record ScheduleSourceCatalogDeploymentResult
{
    public required bool Applied { get; init; }

    public Guid? RevisionId { get; init; }

    public required string ContentHash { get; init; }

    public DateTimeOffset? AppliedAtUtc { get; init; }

    public IReadOnlyList<string> PollingDisabledSourceIds { get; init; } = [];

    public required ScheduleSourceCatalogPlan Plan { get; init; }
}

/// <summary>One stored catalog revision, without its content.</summary>
public sealed record ScheduleSourceCatalogRevisionSummary
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }

    public required string ContentHash { get; init; }

    public string? PreviousContentHash { get; init; }

    public required int SourceCount { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? Reason { get; init; }

    public string? ChangeSummary { get; init; }

    /// <summary>Whether this revision's content is what the file holds right now.</summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>One stored catalog revision with its full document, for review or restore.</summary>
public sealed record ScheduleSourceCatalogRevisionDetail
{
    public required ScheduleSourceCatalogRevisionSummary Summary { get; init; }

    public required string Content { get; init; }
}

/// <summary>
/// Raised when the document on disk is not the one an edit was prepared against, or the
/// confirmation does not match the plan it claims.
/// </summary>
public sealed class ScheduleSourceCatalogConflictException(string message)
    : InvalidOperationException(message);

/// <summary>Raised when a submitted document is not a valid catalog.</summary>
public sealed class ScheduleSourceCatalogValidationException(string message)
    : InvalidOperationException(message);
