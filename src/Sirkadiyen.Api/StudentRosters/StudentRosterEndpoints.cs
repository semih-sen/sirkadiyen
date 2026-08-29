using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Api.StudentRosters;

/// <summary>
/// The student-number-first step of academic profile onboarding (ADR-085).
/// </summary>
public static class StudentRosterEndpoints
{
    public static IEndpointRouteBuilder MapStudentRosterEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder rosters = builder
            .MapGroup("/api/profile")
            .RequireAuthorization()
            .WithTags("Student Profile");

        // A POST, although it reads: the request body carries a student number,
        // and a student number does not belong in a URL or in an access log.
        rosters.MapPost("/roster-lookup", LookUpAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .RequireRateLimiting(RateLimitingPolicies.RosterLookup)
            .WithSummary("Suggests profile values from the published faculty student lists.");

        return builder;
    }

    private static async Task<IResult> LookUpAsync(
        StudentRosterLookupRequest request,
        StudentRosterLookupService lookup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string number = (request.StudentNumber ?? string.Empty).Trim();
        if (number.Length != StudentProfile.StudentNumberLength || !number.All(char.IsAsciiDigit))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["studentNumber"] =
                [
                    $"A student number must be exactly {StudentProfile.StudentNumberLength} digits.",
                ],
            });
        }

        StudentRosterLookupResult result = await lookup.LookUpAsync(number, cancellationToken);
        return Results.Ok(StudentRosterLookupResponse.From(result));
    }
}
