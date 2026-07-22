using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.SchedulePublication;

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
        TimeOnly startLocalTime,
        TimeOnly endLocalTime,
        string timeZoneId,
        string stableIdentity,
        string contentHash,
        decimal confidence,
        string evidence,
        string? instructor = null,
        string? location = null,
        string? department = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        if (endLocalTime <= startLocalTime)
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
        TimeZoneId = timeZoneId;
        StableIdentity = stableIdentity;
        ContentHash = contentHash;
        Confidence = confidence;
        Evidence = evidence;
        Instructor = instructor;
        Location = location;
        Department = department;
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

    public TimeOnly StartLocalTime { get; private set; }

    public TimeOnly EndLocalTime { get; private set; }

    public string TimeZoneId { get; private set; }

    public string? Instructor { get; private set; }

    public string? Location { get; private set; }

    /// <summary>
    /// The academic department that owns the lesson, when the source states it.
    /// </summary>
    /// <remarks>
    /// This is deliberately nullable. A missing department is not inferred from
    /// the title or evidence; semantic secondary matching requires an explicit
    /// value before it can use this field.
    /// </remarks>
    public string? Department { get; private set; }

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
