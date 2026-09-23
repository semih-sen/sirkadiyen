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

        group.MapPost("/notes", Create)
            .RequireRateLimiting(RateLimitingPolicies.VaultNote)
            .WithSummary("Queues a new vault note; returns the job to poll.");

        group.MapGet("/jobs/{id:guid}", Get)
            .WithSummary("Returns a vault note job's progress and outcome.");

        return builder;
    }

    private static Results<Accepted<VaultJobResponse>, ValidationProblem> Create(
        CreateVaultNoteRequest request,
        VaultJobRegistry jobs)
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

        VaultJobView job = jobs.Submit(accepted);
        return TypedResults.Accepted($"/api/vault/jobs/{job.Id}", VaultJobResponse.From(job));
    }

    private static Results<Ok<VaultJobResponse>, NotFound> Get(Guid id, VaultJobRegistry jobs) =>
        jobs.Find(id) is { } job
            ? TypedResults.Ok(VaultJobResponse.From(job))
            : TypedResults.NotFound();
}
