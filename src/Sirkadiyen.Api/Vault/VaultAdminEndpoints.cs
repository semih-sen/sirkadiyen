using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// The personal vault's note jobs in the administration panel (ADR-169): the job history, and a form
/// that queues a note exactly as the shortcut does. SuperAdmin only; the submission is
/// antiforgery-protected like every other panel mutation.
/// </summary>
/// <remarks>
/// Mapped whether or not the vault is configured, so the history stays readable after the feature is
/// switched off; only submitting needs it on.
/// </remarks>
public static class VaultAdminEndpoints
{
    private const int MaximumListLimit = 200;

    public static IEndpointRouteBuilder MapVaultAdminEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder group = builder.MapGroup("/api/admin/vault")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Vault");

        group.MapGet("/jobs", ListAsync)
            .WithSummary("Lists vault note jobs, newest first.");

        group.MapGet("/jobs/{id:guid}", FindAsync)
            .WithSummary("Returns one vault note job with its request.");

        group.MapPost("/notes", CreateAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .RequireRateLimiting(RateLimitingPolicies.VaultNote)
            .WithSummary("Queues a new vault note on behalf of the signed-in administrator.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        IVaultJobStore store,
        IServiceProvider services,
        CancellationToken cancellationToken,
        int limit = 50)
    {
        if (limit is < 1 or > MaximumListLimit)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"'limit' must be between 1 and {MaximumListLimit}."],
            });
        }

        IReadOnlyList<VaultJobRecord> jobs = await store.ListRecentAsync(limit, cancellationToken);
        return Results.Ok(new VaultAdminJobListResponse
        {
            Enabled = services.GetService<VaultApiOptions>() is not null,
            MaxPromptLength = VaultNoteRequest.MaxPromptLength,
            Jobs = [.. jobs.Select(VaultAdminJobResponse.From)],
        });
    }

    private static async Task<IResult> FindAsync(Guid id, IVaultJobStore store, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { } job
            ? Results.Ok(VaultAdminJobResponse.From(job))
            : Results.NotFound();

    private static async Task<IResult> CreateAsync(
        CreateVaultNoteRequest request,
        ClaimsPrincipal principal,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Resolved here rather than injected: the registry exists only when the vault is configured.
        VaultJobRegistry? jobs = services.GetService<VaultApiOptions>() is null
            ? null
            : services.GetService<VaultJobRegistry>();
        if (jobs is null)
        {
            return Results.Problem(
                title: "Vault özelliği kapalı",
                detail: "Bu sunucuda SIRKADIYEN_VAULT__API_KEY tanımlı değil; not isteği çalıştırılamaz.",
                statusCode: StatusCodes.Status409Conflict);
        }

        VaultNoteRequest? accepted = VaultNoteRequest.Create(request.Prompt, request.Folder, request.Title, out string? error);
        if (accepted is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [error ?? "İstek geçersiz."],
            });
        }

        VaultJobOrigin origin = new(VaultJobSource.Admin, UserClaimsPrincipalFactory.GetRequiredEmail(principal));
        VaultJobView job = await jobs.SubmitAsync(accepted, origin, cancellationToken);
        VaultJobRecord record = new(job, accepted, origin);
        return Results.Accepted($"/api/admin/vault/jobs/{job.Id}", VaultAdminJobResponse.From(record));
    }
}
