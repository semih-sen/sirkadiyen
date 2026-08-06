using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Api.GoogleCalendar;

public static class DepartmentColorEndpoints
{
    public static IEndpointRouteBuilder MapDepartmentColorEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder user = builder.MapGroup("/api/calendar/colors")
            .RequireAuthorization()
            .WithTags("Calendar Colors");
        user.MapGet("/", GetUserColorsAsync)
            .WithSummary("Returns the current user's effective calendar presentation colors.");
        user.MapPut("/{departmentKey}", SetUserColorAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Sets one personal calendar presentation color override.");
        user.MapDelete("/{departmentKey}", ResetUserColorAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Resets one personal calendar presentation color to its default.");

        RouteGroupBuilder admin = builder.MapGroup("/api/admin/calendar-colors")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Administration");
        admin.MapGet("/", GetAdminColorsAsync)
            .WithSummary("Returns system and administrator calendar presentation colors.");
        admin.MapPut("/{departmentKey}", SetAdminColorAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Sets one audited administrator calendar presentation color.");
        admin.MapPost("/{departmentKey}/reset", ResetAdminColorAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Resets one audited administrator color to the system default.");

        return builder;
    }

    private static Task<IReadOnlyList<DepartmentColorView>> GetUserColorsAsync(
        ClaimsPrincipal principal,
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        service.GetForUserAsync(
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            cancellationToken);

    private static Task<IReadOnlyList<DepartmentColorView>> GetAdminColorsAsync(
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        service.GetForAdminAsync(cancellationToken);

    private static async Task<IResult> SetUserColorAsync(
        string departmentKey,
        SetDepartmentColorRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        await UserMutationAsync(
            departmentKey,
            request.Color,
            principal,
            context,
            service,
            cancellationToken);

    private static async Task<IResult> ResetUserColorAsync(
        string departmentKey,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        await UserMutationAsync(
            departmentKey,
            null,
            principal,
            context,
            service,
            cancellationToken);

    private static async Task<IResult> UserMutationAsync(
        string departmentKey,
        string? color,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken)
    {
        try
        {
            bool changed = await service.SetUserColorAsync(
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                departmentKey,
                color,
                CorrelationId(context),
                cancellationToken);
            return Results.Ok(new DepartmentColorMutationResponse
            {
                Changed = changed,
                CalendarRefreshQueued = changed,
            });
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> SetAdminColorAsync(
        string departmentKey,
        SetAdminDepartmentColorRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        await AdminMutationAsync(
            departmentKey,
            request.Color,
            request.Reason,
            principal,
            context,
            service,
            cancellationToken);

    private static async Task<IResult> ResetAdminColorAsync(
        string departmentKey,
        ResetAdminDepartmentColorRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken) =>
        await AdminMutationAsync(
            departmentKey,
            null,
            request.Reason,
            principal,
            context,
            service,
            cancellationToken);

    private static async Task<IResult> AdminMutationAsync(
        string departmentKey,
        string? color,
        string reason,
        ClaimsPrincipal principal,
        HttpContext context,
        DepartmentColorService service,
        CancellationToken cancellationToken)
    {
        try
        {
            bool changed = await service.SetAdminColorAsync(
                departmentKey,
                color,
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                reason,
                CorrelationId(context),
                cancellationToken);
            return Results.Ok(new DepartmentColorMutationResponse
            {
                Changed = changed,
                CalendarRefreshQueued = changed,
            });
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static IResult Validation(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message],
        });

    private static string CorrelationId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}
