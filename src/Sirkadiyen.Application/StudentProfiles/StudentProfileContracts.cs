using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>The stored profile, and what the write implied for the user's calendar (ADR-096).</summary>
public sealed record StudentProfileUpsertResult
{
    public required StudentProfileView Profile { get; init; }

    /// <summary>
    /// Whether the write changed the audience the profile resolves. False for a first profile, for
    /// an identical re-save, and for a change confined to the student number.
    /// </summary>
    public required bool AudienceChanged { get; init; }

    /// <summary>
    /// Whether a re-synchronization was actually queued. It is false when the audience changed but
    /// the user has no completed calendar connection yet, because initial sync will resolve the
    /// new audience when it runs.
    /// </summary>
    public required bool CalendarResyncRequested { get; init; }
}

/// <summary>A read projection of a stored student profile.</summary>
public sealed record StudentProfileView
{
    public required Guid UserId { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string StudentNumber { get; init; }

    public required string SelectorSchemaVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record SaveStudentProfileResult
{
    public required SaveStudentProfileOutcome Outcome { get; init; }

    public StudentProfileView? Profile { get; init; }

    public IReadOnlyList<StudentProfileValidationError> ValidationErrors { get; init; } = [];

    /// <summary>
    /// Whether the save queued a calendar re-synchronization because the audience changed
    /// (ADR-096). The worker performs it; this only reports that it was requested.
    /// </summary>
    public bool CalendarResyncRequested { get; init; }
}

public enum SaveStudentProfileOutcome
{
    Saved,
    Invalid,
    ActivationRequired,
}
