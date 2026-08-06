using System.ComponentModel.DataAnnotations;

namespace Sirkadiyen.Application.Scheduling.Publication;

/// <summary>
/// The thresholds revision validation applies (ADR-025, ADR-029).
/// </summary>
/// <remarks>
/// These are operational tuning values, not domain rules, so they live in
/// configuration and are validated on startup. Changing what counts as a
/// suspicious revision must not require changing validation code.
/// </remarks>
public sealed class RevisionValidationOptions
{
    public const string SectionName = "RevisionValidation";

    /// <summary>
    /// The share of the previously published records that may disappear before
    /// the deletion rule is considered. Exactly this share does not trigger it.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MaximumDeletionShare { get; set; } = 0.20;

    /// <summary>
    /// The number of disappeared records below which the deletion rule never
    /// triggers, whatever the share.
    /// </summary>
    /// <remarks>
    /// A small source makes the share alone useless: a twenty-record source
    /// losing five rows is 25 percent but is not evidence of a broken parse.
    /// Both conditions must hold (ADR-029).
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MinimumDeletionCount { get; set; } = 10;

    /// <summary>Below this confidence a record is reported for review.</summary>
    [Range(0.0, 1.0)]
    public decimal LowConfidenceThreshold { get; set; } = 0.50m;

    [Range(1, 1440)]
    public int MinimumLessonMinutes { get; set; } = 10;

    [Range(1, 1440)]
    public int MaximumLessonMinutes { get; set; } = 600;

    /// <summary>
    /// The number of distinct audience overlaps tolerated before the revision is
    /// quarantined.
    /// </summary>
    /// <remarks>
    /// ADR-025 quarantines on <em>multiple</em> impossible overlaps. One overlap
    /// is routinely a source typo that an operator should see but that should not
    /// stop a schedule reaching students, so a single overlap is a warning.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MaximumToleratedOverlaps { get; set; } = 1;

    /// <summary>
    /// How far outside its academic year a record may fall before it is
    /// reported.
    /// </summary>
    [Range(0, 365)]
    public int AcademicYearGraceDays { get; set; } = 30;

    public void Validate()
    {
        if (MinimumLessonMinutes >= MaximumLessonMinutes)
        {
            throw new InvalidOperationException(
                $"{nameof(MinimumLessonMinutes)} must be less than "
                + $"{nameof(MaximumLessonMinutes)}.");
        }
    }
}
