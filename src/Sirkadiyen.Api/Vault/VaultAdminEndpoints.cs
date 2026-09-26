using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// The personal vault in the administration panel: the job history and a form that queues a note
/// exactly as the shortcut does (ADR-169), and the vault's notes with a button that queues flashcards
/// for one of them (ADR-170). SuperAdmin only; every submission is antiforgery-protected like every
/// other panel mutation.
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

        group.MapGet("/notes", ListNotesAsync)
            .WithSummary("Lists the vault's notes with the latest job that wrote or converted each.");

        group.MapGet("/notes/content", GetNoteContentAsync)
            .WithSummary("Returns one note's current text and the flashcards the plugin will find in it.");

        // The agent runs on the same subscription as a new note, so the same per-IP budget covers both.
        group.MapPost("/flashcards", CreateFlashcardsAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .RequireRateLimiting(RateLimitingPolicies.VaultNote)
            .WithSummary("Queues adding Spaced Repetition flashcards to an existing note.");

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
        if (Registry(services) is not { } jobs)
        {
            return VaultDisabled();
        }

        VaultNoteRequest? accepted = VaultNoteRequest.Create(request.Prompt, request.Folder, request.Title, out string? error);
        if (accepted is null)
        {
            return Invalid(error);
        }

        return await SubmitAsync(jobs, accepted, principal, cancellationToken);
    }

    private static async Task<IResult> ListNotesAsync(
        IVaultJobStore jobStore,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (Store(services) is not { } store)
        {
            return Results.Ok(new VaultAdminNoteListResponse { Enabled = false, Notes = [] });
        }

        IReadOnlyList<VaultObjectInfo> objects = await store.ListObjectsAsync(cancellationToken);
        IReadOnlyList<VaultJobRecord> jobs = await jobStore.ListForNotesAsync(cancellationToken);
        return Results.Ok(new VaultAdminNoteListResponse { Enabled = true, Notes = BuildNoteList(objects, jobs) });
    }

    /// <summary>
    /// The vault's notes in path order, each with the newest job that names it and the cards the newest
    /// successful job recorded. Objects that are not notes (attachments, hidden folders) are left out.
    /// </summary>
    /// <param name="jobs">Jobs that name a note, newest first, as <see cref="IVaultJobStore.ListForNotesAsync"/> returns them.</param>
    internal static IReadOnlyList<VaultAdminNoteResponse> BuildNoteList(
        IReadOnlyList<VaultObjectInfo> objects,
        IReadOnlyList<VaultJobRecord> jobs)
    {
        Dictionary<string, VaultJobRecord> latest = new(StringComparer.Ordinal);
        Dictionary<string, VaultFlashcardSummary> flashcards = new(StringComparer.Ordinal);
        foreach (VaultJobRecord job in jobs)
        {
            if (job.NotePath is not { } path)
            {
                continue;
            }

            latest.TryAdd(path, job);
            if (job.View is { Status: VaultJobStatus.Succeeded, Flashcards: { } summary })
            {
                flashcards.TryAdd(path, summary);
            }
        }

        return objects
            .Where(static item => VaultPathPolicy.IsNote(item.Path))
            .OrderBy(static item => item.Path, StringComparer.Ordinal)
            .Select(item => new VaultAdminNoteResponse
            {
                Path = item.Path,
                Title = VaultPathPolicy.TitleOf(item.Path),
                Folder = VaultPathPolicy.FolderOf(item.Path),
                SizeBytes = item.Size,
                LastModifiedUtc = item.LastModifiedUtc,
                LatestJob = latest.TryGetValue(item.Path, out VaultJobRecord? job)
                    ? new VaultAdminNoteJobResponse
                    {
                        Id = job.View.Id,
                        Kind = job.Request.Kind,
                        Status = job.View.Status,
                        CreatedAtUtc = job.View.CreatedAtUtc,
                        Error = job.View.Error,
                    }
                    : null,
                Flashcards = VaultFlashcardSummaryResponse.From(flashcards.GetValueOrDefault(item.Path)),
            })
            .ToList();
    }

    private static async Task<IResult> GetNoteContentAsync(
        string? path,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (Store(services) is not { } store)
        {
            return VaultDisabled();
        }

        if (VaultPathPolicy.NormalizeNotePath(path, out string? error) is not { } notePath)
        {
            return Invalid(error, "path");
        }

        if (await store.GetAsync(notePath, cancellationToken) is not { } document)
        {
            return Results.NotFound();
        }

        VaultFlashcardSummary summary = VaultFlashcards.Summarize(document.Content);
        return Results.Ok(new VaultAdminNoteContentResponse
        {
            Path = notePath,
            Title = VaultPathPolicy.TitleOf(notePath),
            Content = document.Content,
            Flashcards = VaultFlashcardSummaryResponse.From(summary)!,

            // A note without a deck is simply a note; its highlights are not misfiled cards.
            FlashcardProblems = summary.Deck is null ? [] : VaultFlashcards.Problems(document.Content),
        });
    }

    private static async Task<IResult> CreateFlashcardsAsync(
        CreateVaultFlashcardsRequest request,
        ClaimsPrincipal principal,
        IVaultJobStore jobStore,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Registry(services) is not { } jobs)
        {
            return VaultDisabled();
        }

        VaultFlashcardRequest? accepted = VaultFlashcardRequest.Create(request.Path, out string? error);
        if (accepted is null)
        {
            return Invalid(error, "path");
        }

        // A second click while the first job waits would convert the note twice; the job itself also
        // refuses a note that already has a deck, but only once the first has written it.
        bool pending = (await jobStore.ListUnfinishedAsync(cancellationToken))
            .Any(job => job.Request is VaultFlashcardRequest other
                && string.Equals(other.NotePath, accepted.NotePath, StringComparison.Ordinal));
        if (pending)
        {
            return Results.Problem(
                title: "Bekleyen iş var",
                detail: $"'{accepted.NotePath}' için flashcard işi zaten sırada ya da çalışıyor.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await SubmitAsync(jobs, accepted, principal, cancellationToken);
    }

    private static async Task<IResult> SubmitAsync(
        VaultJobRegistry jobs,
        VaultJobRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        VaultJobOrigin origin = new(VaultJobSource.Admin, UserClaimsPrincipalFactory.GetRequiredEmail(principal));
        VaultJobView job = await jobs.SubmitAsync(request, origin, cancellationToken);
        VaultJobRecord record = new(job, request, origin);
        return Results.Accepted($"/api/admin/vault/jobs/{job.Id}", VaultAdminJobResponse.From(record));
    }

    private static VaultJobRegistry? Registry(IServiceProvider services) =>
        services.GetService<VaultApiOptions>() is null ? null : services.GetService<VaultJobRegistry>();

    private static IVaultStore? Store(IServiceProvider services) =>
        services.GetService<VaultApiOptions>() is null ? null : services.GetService<IVaultStore>();

    private static IResult VaultDisabled() =>
        Results.Problem(
            title: "Vault özelliği kapalı",
            detail: "Bu sunucuda SIRKADIYEN_VAULT__API_KEY tanımlı değil; vault'a erişilemez.",
            statusCode: StatusCodes.Status409Conflict);

    private static IResult Invalid(string? error, string field = "request") =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [error ?? "İstek geçersiz."],
        });
}
