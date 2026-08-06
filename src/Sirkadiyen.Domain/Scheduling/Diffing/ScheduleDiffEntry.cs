namespace Sirkadiyen.Domain.Scheduling.Diffing;

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
    /// <summary>Assigned when the entry is attached to a <see cref="ScheduleDiff"/>.</summary>
    /// <remarks>
    /// The differ is a pure function and produces entries with no identity at
    /// all; <see cref="ScheduleDiff.Create"/> stamps both this and
    /// <see cref="ScheduleDiffId"/>. An unattached entry therefore carries
    /// <see cref="Guid.Empty"/>, which is the state a unit test sees.
    /// </remarks>
    public Guid Id { get; init; }

    public Guid ScheduleDiffId { get; init; }

    public required ScheduleDiffChange Change { get; init; }

    public required ScheduleDiffMatch Match { get; init; }

    public Guid? PreviousRecordId { get; init; }

    public Guid? CurrentRecordId { get; init; }

    public decimal? MatchScore { get; init; }

    public decimal? TitleScore { get; init; }

    public decimal? InstructorScore { get; init; }

    /// <summary>
    /// How similar the two academic departments were, or <c>null</c> when the
    /// match was not made on one.
    /// </summary>
    /// <remarks>
    /// On a <see cref="ScheduleDiffMatch.SecondaryAttributes"/> entry, null is
    /// meaningful: it says the lesson was recognized from its title and
    /// instructor alone because the source named no single department for it.
    /// That is the weaker of the two matching bases (ADR-035 as amended), so an
    /// operator reviewing a held diff can tell the two apart.
    /// </remarks>
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
