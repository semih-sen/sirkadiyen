using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Scheduling.Publication;

/// <summary>
/// One lesson in the canonical schedule model, belonging to one revision.
/// </summary>
/// <remarks>
/// Identity and content are separate concepts (ADR-018).
/// <see cref="StableIdentity"/> answers "which logical lesson is this?" and
/// survives a room or instructor change; <see cref="ContentHash"/> answers "did
/// anything a student would see change?". The semantic diff compares identity
/// first and content second, which is what keeps synchronization from deleting
/// and recreating events that merely moved room.
/// </remarks>
public sealed class CanonicalScheduleRecord
{
    private CanonicalScheduleRecord()
    {
        // Materialization constructor.
        CandidateId = string.Empty;
        AcademicYear = string.Empty;
        DisplayTitle = string.Empty;
        TimeZoneId = string.Empty;
        StableIdentity = string.Empty;
        ContentHash = string.Empty;
        AudienceSelectors = string.Empty;
        Evidence = string.Empty;
    }

    public CanonicalScheduleRecord(
        Guid scheduleRevisionId,
        SourceId sourceId,
        string candidateId,
        CanonicalRecordStatus recordStatus,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        ScheduleEventType eventType,
        AudienceScope audienceScope,
        string audienceSelectors,
        string displayTitle,
        string? normalizedCourseIdentity,
        DateOnly localDate,
        TimeOnly? startLocalTime,
        TimeOnly? endLocalTime,
        bool isAllDay,
        string timeZoneId,
        string stableIdentity,
        string contentHash,
        decimal confidence,
        string evidence,
        string? instructor = null,
        string? location = null,
        string? curriculumBlock = null,
        IReadOnlyList<string>? departments = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // A record is either timed or all-day, never half of each (ADR-046). A
        // half-stated shape would reach Google Calendar as an event with no end,
        // so it is refused here rather than corrected downstream.
        if (isAllDay)
        {
            if (startLocalTime is not null || endLocalTime is not null)
            {
                throw new ArgumentException(
                    "An all-day canonical record must state no local time.",
                    nameof(isAllDay));
            }
        }
        else if (startLocalTime is null || endLocalTime is null)
        {
            throw new ArgumentException(
                "A timed canonical record must state both local times.",
                nameof(startLocalTime));
        }
        else if (endLocalTime <= startLocalTime)
        {
            throw new ArgumentException(
                "A canonical record must end after it starts.",
                nameof(endLocalTime));
        }

        Id = Guid.CreateVersion7();
        ScheduleRevisionId = scheduleRevisionId;
        SourceId = sourceId;
        CandidateId = candidateId;
        RecordStatus = recordStatus;
        AcademicYear = academicYear;
        ClassYear = classYear;
        ProgramLanguage = programLanguage;
        EventType = eventType;
        AudienceScope = audienceScope;
        AudienceSelectors = audienceSelectors;
        DisplayTitle = displayTitle;
        NormalizedCourseIdentity = normalizedCourseIdentity;
        LocalDate = localDate;
        StartLocalTime = startLocalTime;
        EndLocalTime = endLocalTime;
        IsAllDay = isAllDay;
        TimeZoneId = timeZoneId;
        StableIdentity = stableIdentity;
        ContentHash = contentHash;
        Confidence = confidence;
        Evidence = evidence;
        Instructor = instructor;
        Location = location;
        CurriculumBlock = curriculumBlock;
        Departments = departments is null ? [] : [.. departments];
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid Id { get; private set; }

    public Guid ScheduleRevisionId { get; private set; }

    public SourceId SourceId { get; private set; }

    /// <summary>The parser candidate identifier retained for traceability.</summary>
    public string CandidateId { get; private set; }

    public CanonicalRecordStatus RecordStatus { get; private set; }

    public string AcademicYear { get; private set; }

    public int ClassYear { get; private set; }

    public ProgramLanguage ProgramLanguage { get; private set; }

    public ScheduleEventType EventType { get; private set; }

    public AudienceScope AudienceScope { get; private set; }

    /// <summary>The audience selectors as the parser stated them.</summary>
    public string AudienceSelectors { get; private set; }

    public string DisplayTitle { get; private set; }

    public string? NormalizedCourseIdentity { get; private set; }

    public DateOnly LocalDate { get; private set; }

    /// <summary>The local start time, or <c>null</c> for an all-day item.</summary>
    public TimeOnly? StartLocalTime { get; private set; }

    /// <summary>The local end time, or <c>null</c> for an all-day item.</summary>
    public TimeOnly? EndLocalTime { get; private set; }

    /// <summary>
    /// Whether this item occupies the whole local date instead of a time range
    /// (ADR-046).
    /// </summary>
    /// <remarks>
    /// Holidays and semester breaks are stated by the sources as a dated row with
    /// no times, one row per closed day, so an all-day record covers exactly one
    /// local date. Google Calendar wants an exclusive end date, which is the
    /// calendar adapter's conversion to make and not a second stored field.
    /// </remarks>
    public bool IsAllDay { get; private set; }

    public string TimeZoneId { get; private set; }

    public string? Instructor { get; private set; }

    public string? Location { get; private set; }

    /// <summary>
    /// The curriculum block ("dilim") the lesson belongs to, when the source
    /// states it (ADR-047).
    /// </summary>
    /// <remarks>
    /// This is lesson content, not an audience selector. A corrected block label
    /// updates the existing logical lesson rather than changing its identity,
    /// which is why it is absent from <see cref="StableIdentity"/>.
    /// </remarks>
    public string? CurriculumBlock { get; private set; }

    /// <summary>
    /// Every academic department the source explicitly named for this lesson, in
    /// source order.
    /// </summary>
    /// <remarks>
    /// Empty means the source named none; a department is never inferred from
    /// the title or the evidence. An integrated session ("entegre oturum") names
    /// several, and all of them are kept because a student has to see who
    /// teaches the session. Semantic matching uses this field only when exactly
    /// one department is named on both sides (ADR-035).
    /// </remarks>
    public IReadOnlyList<string> Departments { get; private set; } = [];

    /// <summary>
    /// The single department this record can be matched on, or <c>null</c> when
    /// the source named none or several.
    /// </summary>
    /// <remarks>
    /// A list of departments is not a comparable value: two integrated sessions
    /// that share one department out of three are not the same lesson, and
    /// comparing the joined text would score them as nearly identical.
    /// </remarks>
    public string? ComparableDepartment =>
        Departments.Count == 1 ? Departments[0] : null;

    /// <summary>
    /// Free text a source states about this session that has no field of its
    /// own — today, the topic a Grade 3 bedside session is about (ADR-101).
    /// </summary>
    /// <remarks>
    /// It is content rather than identity: it is part of the parser's content
    /// hash and of no stable identity, so a corrected topic moves the event a
    /// student already has instead of creating a second one beside it.
    /// </remarks>
    public string? Notes { get; private set; }

    public string StableIdentity { get; private set; }

    public string ContentHash { get; private set; }

    public decimal Confidence { get; private set; }

    /// <summary>The source evidence the parser cited for this record.</summary>
    public string Evidence { get; private set; }
}

public enum ScheduleEventType
{
    Theory,
    Practice,
    FreeStudy,
    AnatomyPractice,
    BedsidePractice,
    FacultyPractice,
    VerticalCorridor,
    IntegratedSession,
    Exam,
    Other,
}

public enum AudienceScope
{
    AllStudentsInProgram,
    SelectedGroups,
}

public enum CanonicalRecordStatus
{
    Scheduled,
    Cancelled,
}
