using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Access;

/// <summary>The cohort and week an operator asks the simulation about.</summary>
public sealed record CohortSimulationQuery
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    /// <summary>Any local date inside the wanted week; the service snaps it to Monday.</summary>
    public required DateOnly AnchorLocalDate { get; init; }
}

/// <summary>One week of the live published schedule, as one cohort would receive it.</summary>
public sealed record CohortSimulationWeek
{
    /// <summary>
    /// The program's own academic year (ADR-103). It is derived, never chosen: a year that
    /// disagrees with the one on a record silently resolves to nothing at all.
    /// </summary>
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    /// <summary>Monday of the resolved week, in <see cref="TimeZoneId"/>.</summary>
    public required DateOnly WeekStartLocalDate { get; init; }

    /// <summary>Sunday of the resolved week, inclusive.</summary>
    public required DateOnly WeekEndLocalDate { get; init; }

    public required string TimeZoneId { get; init; }

    /// <summary>
    /// Dimensions the program requires that this query left unstated.
    /// </summary>
    /// <remarks>
    /// Not an error: a partial cohort is a legitimate question, and the audience rule answers it
    /// by withholding every lesson addressed to a dimension the cohort has not declared
    /// (ADR-109). It is reported because that withholding is the reason the week looks emptier
    /// than the operator may expect, and nothing else on the screen would say so.
    /// </remarks>
    public required IReadOnlyList<string> MissingRequiredSelectors { get; init; }

    /// <summary>
    /// How many live lessons resolve to this cohort across the whole year, before the week
    /// filter. It is what separates "this week is quiet" from "this cohort receives nothing".
    /// </summary>
    public required int CohortYearEventCount { get; init; }

    /// <summary>
    /// The sources that actually contributed a live lesson to this cohort this year. A source
    /// catalogued but not yet captured is simply absent, which is what makes an incomplete
    /// program legible instead of looking broken.
    /// </summary>
    public required IReadOnlyList<string> PublishedSourceIds { get; init; }

    public required IReadOnlyList<CohortSimulationEvent> Events { get; init; }
}

/// <summary>One lesson, both as a calendar would show it and as the record states it.</summary>
public sealed record CohortSimulationEvent
{
    public required string Summary { get; init; }

    public string? Description { get; init; }

    /// <summary>The location a student would see, which the presentation policy may withhold.</summary>
    public string? Location { get; init; }

    public required ManagedCalendarEventLabel Label { get; init; }

    public required DateOnly LocalDate { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public required bool IsAllDay { get; init; }

    public required string TimeZoneId { get; init; }

    public required CohortSimulationRawRecord Raw { get; init; }
}

/// <summary>The canonical record behind one simulated event, for the operator who asks why.</summary>
public sealed record CohortSimulationRawRecord
{
    public required Guid CanonicalRecordId { get; init; }

    public required Guid ScheduleRevisionId { get; init; }

    public required string SourceId { get; init; }

    public required string CandidateId { get; init; }

    public required string StableIdentity { get; init; }

    public required string ContentHash { get; init; }

    public required string RecordStatus { get; init; }

    public required string EventType { get; init; }

    public required string AudienceScope { get; init; }

    /// <summary>
    /// The stored selector JSON re-projected as typed pairs, so the browser never parses a
    /// canonical string of its own (AI_GUIDELINE §5).
    /// </summary>
    public required IReadOnlyList<AudienceSelectorView> AudienceSelectors { get; init; }

    public required string DisplayTitle { get; init; }

    public string? NormalizedCourseIdentity { get; init; }

    public string? Instructor { get; init; }

    /// <summary>
    /// The location the source stated, before the presentation policy judged it. It differs
    /// from <see cref="CohortSimulationEvent.Location"/> exactly where the policy withholds a
    /// pointer such as "amfi programına bakınız", and seeing that difference is the point.
    /// </summary>
    public string? RawLocation { get; init; }

    public string? CurriculumBlock { get; init; }

    public required IReadOnlyList<string> Departments { get; init; }

    public string? ComparableDepartment { get; init; }

    public string? Notes { get; init; }

    public required decimal Confidence { get; init; }

    public required string Evidence { get; init; }

    /// <summary>
    /// Whether another event in this week carries the same stable identity. That is a real
    /// collision rather than a duplicate lesson: the managed event id is derived from the
    /// identity alone, so the two would collapse into one event on a calendar.
    /// </summary>
    public required bool SharesStableIdentity { get; init; }
}

public sealed record AudienceSelectorView(string Dimension, string Value);

/// <summary>
/// A simulation asked about a program or a cohort the schema does not support. It carries the
/// validator's own errors so the endpoint reports them per field, exactly as a profile save does.
/// </summary>
public sealed class CohortSimulationValidationException(
    IReadOnlyList<StudentProfileValidationError> errors)
    : Exception("The requested cohort is not one the supported profile schema defines.")
{
    public IReadOnlyList<StudentProfileValidationError> Errors { get; } = errors;
}
