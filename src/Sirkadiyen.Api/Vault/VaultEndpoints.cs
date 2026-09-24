using Microsoft.AspNetCore.Http.HttpResults;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// The personal vault's note generation (ADR-168). Mapped only when the feature is configured, so a
/// deployment without it exposes no route at all rather than one that always fails.
/// </summary>
public static class VaultEndpoints
{
    public static IEndpointRouteBuilder MapVaultEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ServiceProvider.GetService<VaultApiOptions>() is null)
        {
            return builder;
        }

        RouteGroupBuilder group = builder.MapGroup("/api/vault")
            .AddEndpointFilter<VaultApiKeyFilter>()
            .WithTags("Vault");

        group.MapPost("/notes", CreateAsync)
            .RequireRateLimiting(RateLimitingPolicies.VaultNote)
            .WithSummary("Queues a new vault note; returns the job to poll.");

        group.MapGet("/jobs/{id:guid}", GetAsync)
            .WithSummary("Returns a vault note job's progress and outcome.");

        return builder;
    }

    private static async Task<Results<Accepted<VaultJobResponse>, ValidationProblem>> CreateAsync(
        CreateVaultNoteRequest request,
        VaultJobRegistry jobs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        VaultNoteRequest? accepted = VaultNoteRequest.Create(request.Prompt, request.Folder, request.Title, out string? error);
        if (accepted is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [error ?? "İstek geçersiz."],
            });
        }

        VaultJobView job = await jobs.SubmitAsync(accepted, VaultJobOrigin.Shortcut, cancellationToken);
        return TypedResults.Accepted($"/api/vault/jobs/{job.Id}", VaultJobResponse.From(job));
    }

    private static async Task<Results<Ok<VaultJobResponse>, NotFound>> GetAsync(
        Guid id,
        VaultJobRegistry jobs,
        CancellationToken cancellationToken) =>
        await jobs.FindAsync(id, cancellationToken) is { } job
            ? TypedResults.Ok(VaultJobResponse.From(job))
            : TypedResults.NotFound();
}
