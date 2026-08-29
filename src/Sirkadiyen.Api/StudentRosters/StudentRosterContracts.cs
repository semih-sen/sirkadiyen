using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.StudentRosters;

public sealed record StudentRosterLookupRequest
{
    public string? StudentNumber { get; init; }
}

/// <summary>
/// What the published lists say about a student number.
/// </summary>
/// <remarks>
/// The response separates what was suggested from what the student still has to
/// answer, because ADR-085 forbids implying that a successful lookup produced a
/// complete profile. It carries no roster identifiers or internal diagnostics:
/// which document a student appears in is not their business, and the operator
/// diagnostics belong in logs.
/// </remarks>
public sealed record StudentRosterLookupResponse
{
    public required string Outcome { get; init; }

    public required string StudentNumber { get; init; }

    /// <summary>
    /// The name the list states, for the student to recognize. It is returned and
    /// then forgotten: never stored, never logged. Two of the four lists publish
    /// it already masked.
    /// </summary>
    public string? GivenName { get; init; }

    public string? FamilyName { get; init; }

    public string? AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>Values to prefill the form with. Every one stays editable.</summary>
    public IReadOnlyDictionary<string, string> SuggestedSelectors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Required dimensions the lists said nothing usable about.</summary>
    public IReadOnlyList<string> DimensionsRequiringInput { get; init; } = [];

    public IReadOnlyList<StudentRosterLookupNoticeResponse> Notices { get; init; } = [];

    /// <summary>
    /// Whether at least one list could not be read when this lookup ran, so a
    /// miss may mean "unreadable" rather than "absent".
    /// </summary>
    public bool SomeListsUnreadable { get; init; }

    public static StudentRosterLookupResponse From(StudentRosterLookupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StudentRosterLookupResponse
        {
            Outcome = result.Outcome.ToString(),
            StudentNumber = result.StudentNumber,
            GivenName = result.GivenName,
            FamilyName = result.FamilyName,
            AcademicYear = result.AcademicYear,
            ClassYear = result.ClassYear,
            ProgramLanguage = result.ProgramLanguage,
            SuggestedSelectors = result.SuggestedSelectors,
            DimensionsRequiringInput = result.DimensionsRequiringInput,
            Notices =
            [
                .. result.Notices.Select(notice => new StudentRosterLookupNoticeResponse
                {
                    Code = notice.Code.ToString(),
                    Dimension = notice.Dimension,
                    Message = notice.Message,
                }),
            ],
            SomeListsUnreadable = result.UnreadableRosterIds.Count > 0,
        };
    }
}

public sealed record StudentRosterLookupNoticeResponse
{
    public required string Code { get; init; }

    public string? Dimension { get; init; }

    public required string Message { get; init; }
}
