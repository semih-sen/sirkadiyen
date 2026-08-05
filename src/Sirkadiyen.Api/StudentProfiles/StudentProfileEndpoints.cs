using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Api.StudentProfiles;

public static class StudentProfileEndpoints
{
    public static IEndpointRouteBuilder MapStudentProfileEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder profile = builder
            .MapGroup("/api/profile")
            .RequireAuthorization()
            .WithTags("Student Profile");

        profile.MapGet("/options", GetOptions)
            .WithSummary("Returns the supported academic profile options for the current year.");
        profile.MapGet("/", GetAsync)
            .WithSummary("Returns the current user's academic profile, if set.");
        profile.MapPut("/", SaveAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Creates or replaces the current user's academic profile.");

        return builder;
    }

    private static IResult GetOptions(StudentProfileService profileService) =>
        Results.Ok(SupportedProfileOptionsResponse.From(profileService.Schema));

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal,
        StudentProfileService profileService,
        CancellationToken cancellationToken)
    {
        StudentProfileView? profile = await profileService.GetAsync(
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            cancellationToken);

        return profile is null ? Results.NoContent() : Results.Ok(profile);
    }

    private static async Task<IResult> SaveAsync(
        SaveStudentProfileRequest request,
        ClaimsPrincipal principal,
        StudentProfileService profileService,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ClassYear is not { } classYear)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["classYear"] = ["'classYear' is required."],
            });
        }

        if (request.ProgramLanguage is not { } programLanguage)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["programLanguage"] = ["'programLanguage' is required."],
            });
        }

        SubmittedStudentProfile submitted = new()
        {
            ClassYear = classYear,
            ProgramLanguage = programLanguage,
            StudentNumber = request.StudentNumber ?? string.Empty,
            Selectors = request.Selectors is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(request.Selectors, StringComparer.Ordinal),
        };

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        SaveStudentProfileResult result = await profileService.SaveAsync(
            userId,
            submitted,
            cancellationToken);

        switch (result.Outcome)
        {
            case SaveStudentProfileOutcome.Saved:
                OnboardingSnapshot state = await onboarding.GetAsync(userId, cancellationToken);
                return Results.Ok(new SaveStudentProfileResponse
                {
                    Profile = result.Profile!,
                    Onboarding = state,
                    CalendarResyncRequested = result.CalendarResyncRequested,
                });

            case SaveStudentProfileOutcome.ActivationRequired:
                return Results.Problem(
                    title: "Account not activated",
                    detail: "Redeem a license or request activation before setting a profile.",
                    statusCode: StatusCodes.Status409Conflict);

            case SaveStudentProfileOutcome.Invalid:
            default:
                return Results.ValidationProblem(ToProblemErrors(result.ValidationErrors));
        }
    }

    private static IDictionary<string, string[]> ToProblemErrors(
        IReadOnlyList<StudentProfileValidationError> errors)
    {
        Dictionary<string, List<string>> grouped = new(StringComparer.Ordinal);
        foreach (StudentProfileValidationError error in errors)
        {
            // Program-level failures have no selector key; group them under a
            // stable field name the client can render.
            string field = error.Key ?? "profile";
            if (!grouped.TryGetValue(field, out List<string>? messages))
            {
                messages = [];
                grouped[field] = messages;
            }

            messages.Add(error.Message);
        }

        return grouped.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToArray(),
            StringComparer.Ordinal);
    }
}

public sealed record SaveStudentProfileRequest
{
    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    public string? StudentNumber { get; init; }

    public IReadOnlyDictionary<string, string>? Selectors { get; init; }
}

public sealed record SaveStudentProfileResponse
{
    public required StudentProfileView Profile { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }

    /// <summary>
    /// Whether the change altered the audience the profile resolves and therefore queued a
    /// calendar re-synchronization (ADR-096).
    /// </summary>
    /// <remarks>
    /// It says the work was <em>requested</em>, not that it has happened: the worker converges the
    /// calendar on its next cycle. It is false for a first profile and for a change confined to
    /// fields the audience rule does not read.
    /// </remarks>
    public required bool CalendarResyncRequested { get; init; }
}

/// <summary>The supported profile options the frontend renders its form from.</summary>
public sealed record SupportedProfileOptionsResponse
{
    public required string AcademicYear { get; init; }

    public required string SchemaVersion { get; init; }

    public required IReadOnlyList<SupportedProfileProgram> Programs { get; init; }

    public static SupportedProfileOptionsResponse From(SupportedProfileSchema schema) => new()
    {
        AcademicYear = schema.AcademicYear,
        SchemaVersion = schema.SchemaVersion,
        Programs = schema.Programs,
    };
}
