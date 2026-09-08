using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

/// <summary>
/// Advances the pending initial synchronizations of one cycle, several connections at a time
/// (ADR-157).
/// </summary>
/// <remarks>
/// The fan-out lives here rather than in the service because each connection needs its own DI
/// scope: the stores the service writes through are scoped and a <c>DbContext</c> rejects
/// concurrent use. This is still one worker's work — the whole stage runs inside the shared
/// calendar fence (ADR-122), so a connection is never advanced by two instances at once.
/// </remarks>
internal sealed class InitialCalendarSyncTask(
    IServiceScopeFactory scopeFactory,
    InitialSyncOptions options,
    ILogger<InitialCalendarSyncTask> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            InitialCalendarSyncBatch batch;
            await using (AsyncServiceScope listing = scopeFactory.CreateAsyncScope())
            {
                batch = await listing.ServiceProvider
                    .GetRequiredService<InitialCalendarSyncService>()
                    .ListPendingAsync(cancellationToken);
            }

            if (batch.Frozen)
            {
                logger.LogInformation(
                    "Initial calendar synchronization skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            if (batch.Connections.Count == 0)
            {
                return false;
            }

            ConcurrentBag<InitialCalendarSyncResult> results = [];
            await Parallel.ForEachAsync(
                batch.Connections,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.UserConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (connection, token) =>
                {
                    // A scope per connection, not per cycle: this is what makes the pass safe to
                    // run concurrently at all. One connection's failure is already reported as a
                    // result rather than thrown, so it never abandons the others.
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    InitialCalendarSyncService sync = scope.ServiceProvider
                        .GetRequiredService<InitialCalendarSyncService>();
                    results.Add(await sync.SyncOneAsync(connection, token));
                });

            foreach (InitialCalendarSyncResult user in results)
            {
                LogResult(user);
            }

            return results.Any(
                static user => user.Outcome is InitialCalendarSyncOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending initial calendar syncs failed.");
            return false;
        }
    }

    private void LogResult(InitialCalendarSyncResult user)
    {
        switch (user.Outcome)
        {
            case InitialCalendarSyncOutcome.Frozen:
                logger.LogInformation(
                    "Initial calendar sync remains pending for user {UserId} because their "
                    + "class/program pipeline is frozen.", user.UserId);
                break;
            case InitialCalendarSyncOutcome.Completed:
                logger.LogInformation(
                    "Initial calendar sync completed for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle of {ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsWritten, user.ApplicableRecordCount);
                break;
            case InitialCalendarSyncOutcome.InProgress:
                logger.LogInformation(
                    "Initial calendar sync advanced for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle, more remain of {ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsWritten, user.ApplicableRecordCount);
                break;
            case InitialCalendarSyncOutcome.ProfileMissing:
                logger.LogWarning(
                    "Initial calendar sync could not run for user {UserId}: no student profile "
                    + "was found, so nothing could be resolved.", user.UserId);
                break;
            case InitialCalendarSyncOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Initial calendar sync stopped for user {UserId}: the Calendar grant is "
                    + "missing a required scope or was revoked, so re-authorization is required.",
                    user.UserId);
                break;
            case InitialCalendarSyncOutcome.Failed:
            default:
                logger.LogError(
                    "Initial calendar sync failed for user {UserId} and will retry next cycle: "
                    + "{FailureReason}", user.UserId, user.FailureReason);
                break;
        }
    }
}
