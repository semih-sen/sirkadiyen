namespace Sirkadiyen.Domain.ScheduleDiffing;

/// <summary>
/// One semantic change between two published schedule revisions.
/// </summary>
/// <remarks>
/// Ambiguous entries are evidence, not calendar commands. A consumer may act on
/// Created, Updated and Deleted only after the containing diff has no ambiguity
/// and has passed the publication safety rules.
/// </remarks>
public sealed record ScheduleDiffEntry
{
    public required ScheduleDiffChange Change { get; init; }

    public required ScheduleDiffMatch Match { get; init; }

    public Guid? PreviousRecordId { get; init; }

    public Guid? CurrentRecordId { get; init; }

    public decimal? MatchScore { get; init; }

    public decimal? TitleScore { get; init; }

    public decimal? InstructorScore { get; init; }

    public decimal? DepartmentScore { get; init; }
}

public enum ScheduleDiffChange
{
    Created,
    Updated,
    Deleted,
    Unchanged,
    Ambiguous,
}

public enum ScheduleDiffMatch
{
    None,
    ExactStableIdentity,
    SecondaryAttributes,
}
