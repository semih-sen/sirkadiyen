using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.StudentProfiles;

public static class StudentProfileEndpoints
{
    private static readonly JsonSerializerOptions AuditMetadataOptions = ContractJson.CreateOptions();

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
        AuditEventRecorder audit,
        HttpContext context,
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
                // A profile change can retire calendar events the previous audience received
                // (ADR-096), so it is auditable in its own right (AI_GUIDELINE §19). Without this,
                // "why did these lessons disappear" is answerable only from the mapping ledger and
                // the worker log. The student number is deliberately not recorded: it identifies
                // the person and answers nothing about the audience.
                await audit.RecordAsync(
                    new AuditEventDraft
                    {
                        Category = AuditEventCategory.ProfileUpdated,
                        ActorUserId = userId,
                        ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                        SubjectType = "StudentProfile",
                        SubjectId = userId.ToString(),
                        CorrelationId = context.CorrelationId(),
                        ClientIp = context.ClientIp(),
                        UserAgent = context.ClientUserAgent(),
                        Metadata = JsonSerializer.Serialize(
                            new ProfileUpdatedAuditMetadata
                            {
                                AcademicYear = result.Profile!.AcademicYear,
                                ClassYear = result.Profile.ClassYear,
                                ProgramLanguage = result.Profile.ProgramLanguage.ToString(),
                                Selectors = result.Profile.Selectors,
                                AudienceChanged = result.AudienceChanged,
                                CalendarResyncRequested = result.CalendarResyncRequested,
                            },
                            AuditMetadataOptions),
                    },
                    cancellationToken);

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
