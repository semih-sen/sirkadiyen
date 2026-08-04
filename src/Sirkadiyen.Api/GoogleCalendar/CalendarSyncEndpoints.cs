using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Api.GoogleCalendar;

/// <summary>
/// Lets an authorized user start their one-time initial synchronization and read its progress
/// (ADR-058). The request only records intent; the worker does the work.
/// </summary>
public static class CalendarSyncEndpoints
{
    public static IEndpointRouteBuilder MapCalendarSyncEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder sync = builder
            .MapGroup("/api/calendar/sync")
            .RequireAuthorization()
            .WithTags("Calendar Sync");

        sync.MapGet("/", GetStatusAsync)
            .WithSummary("Returns the current user's initial-synchronization progress.");
        sync.MapGet("/progress", GetProgressAsync)
            .WithSummary("Returns ledger-derived synchronization progress counters for the user.");
        sync.MapPost("/", StartAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Starts the current user's one-time initial synchronization.");

        return builder;
    }

    private static async Task<IResult> GetStatusAsync(
        ClaimsPrincipal principal,
        IGoogleCalendarConnectionStore connectionStore,
        IUserCalendarEventMappingStore mappingStore,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        GoogleCalendarConnectionView? connection = await connectionStore.GetByUserIdAsync(
            userId,
            cancellationToken);
        if (connection is null)
        {
            return Results.NoContent();
        }

        int mappedEventCount = await mappingStore.CountForUserAsync(userId, cancellationToken);
        OnboardingSnapshot state = await onboarding.GetAsync(userId, cancellationToken);

        return Results.Ok(new CalendarSyncStatusResponse
        {
            InitialSyncState = connection.InitialSyncState,
            HasManagedCalendar = connection.ManagedCalendarId is { Length: > 0 },
            MappedEventCount = mappedEventCount,
            Onboarding = state,
        });
    }

    private static async Task<IResult> GetProgressAsync(
        ClaimsPrincipal principal,
        IGoogleCalendarConnectionStore connectionStore,
        IUserCalendarEventMappingStore mappingStore,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        GoogleCalendarConnectionView? connection = await connectionStore.GetByUserIdAsync(
            userId,
            cancellationToken);
        if (connection is null)
        {
            return Results.NoContent();
        }

        CalendarSyncProgressView progress = await mappingStore.GetProgressForUserAsync(
            userId,
            cancellationToken);
        OnboardingSnapshot state = await onboarding.GetAsync(userId, cancellationToken);

        return Results.Ok(new CalendarSyncProgressResponse
        {
            InitialSyncState = connection.InitialSyncState,
            HasManagedCalendar = connection.ManagedCalendarId is { Length: > 0 },
            MappedEventCount = progress.MappedEventCount,
            // Every mapped event was written once; the ledger can only distinguish those since
            // patched from those still at their first-written content. It cannot report
            // "unchanged", "failed", or an applicable-record total, so those are not invented.
            CreatedEventCount = progress.MappedEventCount - progress.UpdatedEventCount,
            UpdatedEventCount = progress.UpdatedEventCount,
            FirstWrittenAtUtc = progress.FirstWrittenAtUtc,
            LastWrittenAtUtc = progress.LastWrittenAtUtc,
            Onboarding = state,
        });
    }

    private static async Task<IResult> StartAsync(
        ClaimsPrincipal principal,
        IGoogleCalendarConnectionStore connectionStore,
        OnboardingStateService onboarding,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        RequestInitialSyncResult result = await connectionStore.RequestInitialSyncAsync(
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        switch (result.Outcome)
        {
            case RequestInitialSyncOutcome.Requested:
                return Results.Accepted(
                    "/api/calendar/sync",
                    await RespondAsync(result.Connection!, userId, onboarding, cancellationToken));

            case RequestInitialSyncOutcome.AlreadyInProgress:
            case RequestInitialSyncOutcome.AlreadyCompleted:
                // Repeating the request is not an error; the user simply sees the current state.
                return Results.Ok(
                    await RespondAsync(result.Connection!, userId, onboarding, cancellationToken));

            case RequestInitialSyncOutcome.NotAuthorized:
            case RequestInitialSyncOutcome.NotFound:
            default:
                return Results.Problem(
                    title: "Calendar synchronization is not available yet",
                    detail: "Grant Sirkadiyen permission to manage its own calendar first.",
                    statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<CalendarSyncResponse> RespondAsync(
        GoogleCalendarConnectionView connection,
        Guid userId,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken) => new()
        {
            Connection = connection,
            Onboarding = await onboarding.GetAsync(userId, cancellationToken),
        };
}

public sealed record CalendarSyncResponse
{
    public required GoogleCalendarConnectionView Connection { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

public sealed record CalendarSyncStatusResponse
{
    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    public required bool HasManagedCalendar { get; init; }

    public required int MappedEventCount { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

/// <summary>
/// Ledger-derived synchronization progress for the current user.
/// </summary>
/// <remarks>
/// These counts are projected from the durable event-mapping ledger, so they describe what is
/// actually on the calendar — not per-stage attempt outcomes. "Unchanged", "failed", and a total
/// applicable-record count are intentionally not reported because the ledger does not record them
/// (AI_GUIDELINE §9); surfacing them would require the sync services to persist per-run metrics.
/// </remarks>
public sealed record CalendarSyncProgressResponse
{
    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    public required bool HasManagedCalendar { get; init; }

    /// <summary>Total events currently on the user's managed calendar.</summary>
    public required int MappedEventCount { get; init; }

    /// <summary>Mapped events still at their first-written content (never patched since).</summary>
    public required int CreatedEventCount { get; init; }

    /// <summary>Mapped events patched at least once since they were first written.</summary>
    public required int UpdatedEventCount { get; init; }

    public DateTimeOffset? FirstWrittenAtUtc { get; init; }

    public DateTimeOffset? LastWrittenAtUtc { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}
