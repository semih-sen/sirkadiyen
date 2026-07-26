using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;

namespace Sirkadiyen.Worker;

internal sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ScheduleSourceCatalogLoader catalogLoader,
    WorkerOptions options,
    AdaptivePollingIntervalPolicy intervalPolicy,
    TimeProvider timeProvider,
    ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// How many revisions one cycle publishes. There are 18 sources, so a batch
    /// this size drains a full backlog in one pass while still bounding the work
    /// a single cycle can take on.
    /// </summary>
    private const int PublicationBatchSize = 50;

    /// <summary>
    /// How many diffs one cycle calculates. A diff loads two whole revisions, so
    /// this stays below the publication batch: the backlog is drained over a few
    /// cycles rather than in one pass that holds thousands of records in memory.
    /// </summary>
    private const int DiffBatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sirkadiyen worker started.");

        await SeedSourceCatalogAsync(stoppingToken);

        bool pollScheduleSources = true;
        DateTimeOffset nextSourcePollAt = timeProvider.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (pollScheduleSources)
            {
                await PollAllSourcesAsync(stoppingToken);
                await PublishValidatedRevisionsAsync(stoppingToken);
                await CalculatePendingDiffsAsync(stoppingToken);

                DateTimeOffset intervalSelectedAt = timeProvider.GetUtcNow();
                nextSourcePollAt =
                    intervalSelectedAt + intervalPolicy.GetInterval(intervalSelectedAt);
            }

            bool calendarCatchUpRequired =
                await RunPendingInitialSyncsAsync(stoppingToken);
            calendarCatchUpRequired |=
                await RunFencedCalendarMaintenanceAsync(stoppingToken);

            if (pollScheduleSources)
            {
                await PruneExpiredSnapshotPayloadsAsync(stoppingToken);
            }

            WorkerCycleSchedule next = WorkerCycleScheduler.GetNext(
                calendarCatchUpRequired,
                nextSourcePollAt,
                options,
                timeProvider.GetUtcNow());
            pollScheduleSources = next.PollScheduleSources;

            if (calendarCatchUpRequired && !next.PollScheduleSources)
            {
                logger.LogInformation(
                    "Calendar work remains after an ordinary mutation-budget yield. "
                    + "Catch-up resumes in {CatchUpInterval} without polling schedule sources.",
                    next.Delay);
            }
            else if (next.PollScheduleSources)
            {
                logger.LogInformation(
                    "Next source polling cycle starts in {PollingInterval}.",
                    next.Delay);
            }
            else
            {
                logger.LogDebug(
                    "Calendar queue is idle. It will be checked again in {IdleCheckInterval}; "
                    + "the next source poll remains due at {NextSourcePollAt}.",
                    next.Delay,
                    nextSourcePollAt);
            }

            await Task.Delay(next.Delay, timeProvider, stoppingToken);
        }
    }

    /// <summary>
    /// Holds one PostgreSQL-backed fence continuously across global dispatch, semantic replay,
    /// and inventory. An empty replay scan is therefore conclusive even with several workers:
    /// no other worker can still be dispatching a diff that skipped the same user.
    /// </summary>
    private async Task<bool> RunFencedCalendarMaintenanceAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ICalendarDispatchReconciliationFence fence = scope.ServiceProvider
                .GetRequiredService<ICalendarDispatchReconciliationFence>();
            await using IAsyncDisposable? lease =
                await fence.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                logger.LogInformation(
                    "Calendar dispatch/reconciliation stage yielded because another worker "
                    + "holds the shared fence.");
                return false;
            }

            bool catchUpRequired = await DispatchPendingDiffsAsync(cancellationToken);
            catchUpRequired |= await RunPendingCalendarReconciliationsAsync(cancellationToken);
            await RunDueCalendarInventoriesAsync(cancellationToken);
            return catchUpRequired;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The fenced Calendar dispatch/reconciliation stage failed.");
            return false;
        }
    }

    /// <summary>
    /// Bounds the large normalized snapshot documents while retaining the
    /// active academic year's first snapshot, the latest source content and the
    /// recent change window.
    /// </summary>
    private async Task PruneExpiredSnapshotPayloadsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            SnapshotRetentionService retention = scope.ServiceProvider
                .GetRequiredService<SnapshotRetentionService>();
            SnapshotRetentionResult result = await retention.RunAsync(cancellationToken);

            if (result.Outcome is SnapshotRetentionOutcome.Frozen)
            {
                logger.LogInformation(
                    "Snapshot retention skipped because the global operational freeze is active.");
                return;
            }

            if (result.Pruned.Count > 0)
            {
                logger.LogInformation(
                    "Snapshot retention pruned {PrunedCount} payloads older than {CutoffUtc}; "
                    + "snapshot metadata and downstream evidence remain.",
                    result.Pruned.Count,
                    result.CutoffUtc);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Snapshot retention failed.");
        }
    }

    private async Task SeedSourceCatalogAsync(CancellationToken cancellationToken)
    {
        ScheduleSourceCatalog catalog = await catalogLoader.LoadAsync(
            options.SourceCatalogPath,
            cancellationToken);
        IReadOnlyCollection<ScheduleSource> sources =
            [.. catalog.Sources.Select(static source => source.ToScheduleSource())];

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IScheduleSourceStore store = scope.ServiceProvider
            .GetRequiredService<IScheduleSourceStore>();
        int changed = await store.UpsertAsync(sources, cancellationToken);
        logger.LogInformation(
            "Schedule source catalog loaded with {SourceCount} sources; {ChangedCount} rows changed.",
            sources.Count,
            changed);
    }

    /// <summary>
    /// Publishes every revision validation cleared, including any an earlier
    /// cycle or the approval endpoint left behind.
    /// </summary>
    /// <remarks>
    /// This is driven by revision state rather than by the poll results of this
    /// cycle, so it is also the recovery path: a worker killed between
    /// validation and publication resumes here, and a revision approved through
    /// the API goes live even if that request failed after approving it.
    /// </remarks>
    private async Task PublishValidatedRevisionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleRevisionPublicationService publication = scope.ServiceProvider
                .GetRequiredService<ScheduleRevisionPublicationService>();

            IReadOnlyList<RevisionPublicationResult> results =
                await publication.PublishPendingAsync(PublicationBatchSize, cancellationToken);

            foreach (RevisionPublicationResult result in results)
            {
                logger.LogInformation(
                    "Revision {RevisionId} publication finished with {Outcome}; "
                    + "superseded revision: {SupersededRevisionId}.",
                    result.RevisionId,
                    result.Outcome,
                    result.SupersededRevisionId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Publishing validated revisions failed.");
        }
    }

    /// <summary>
    /// Records what every published revision actually changed.
    /// </summary>
    /// <remarks>
    /// This runs after publication rather than inside it. A revision is live the
    /// moment publication commits, and a diff that failed to calculate must not
    /// be able to take that back. Like publication it is driven by state, so a
    /// worker killed between the two steps calculates the missing diff here on
    /// its next cycle instead of losing it.
    /// </remarks>
    private async Task CalculatePendingDiffsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleDiffService diffs = scope.ServiceProvider
                .GetRequiredService<ScheduleDiffService>();

            IReadOnlyList<ScheduleDiffCalculationResult> results =
                await diffs.CalculatePendingAsync(DiffBatchSize, cancellationToken);

            foreach (ScheduleDiffCalculationResult result in results)
            {
                logger.LogInformation(
                    "Revision {RevisionId} diff {Outcome} against {PreviousRevisionId}: "
                    + "{CreatedCount} created, {UpdatedCount} updated, {DeletedCount} deleted, "
                    + "{UnchangedCount} unchanged, {AmbiguousCount} ambiguous; state {DiffState}.",
                    result.RevisionId,
                    result.Outcome,
                    result.Diff.PreviousRevisionId,
                    result.Diff.CreatedCount,
                    result.Diff.UpdatedCount,
                    result.Diff.DeletedCount,
                    result.Diff.UnchangedCount,
                    result.Diff.AmbiguousCount,
                    result.Diff.State);

                if (result.Diff.State is ScheduleDiffState.Held)
                {
                    // The diff identifier is logged because it is what an
                    // operator needs to review the hold and, when the source
                    // really did drop those lessons, release it (ADR-042).
                    logger.LogWarning(
                        "Diff {ScheduleDiffId} for revision {RevisionId} is held and will not "
                        + "reach any calendar: {HoldReason} "
                        + "{ReleasableHint}",
                        result.Diff.Id,
                        result.RevisionId,
                        result.Diff.HoldReason,
                        result.Diff.IsReleasable
                            ? "Review it at GET /api/diffs/{id} and release it if the source is right."
                            : "It is ambiguous, so it can only be fixed at the source.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Calculating pending schedule diffs failed.");
        }
    }

    /// <summary>
    /// Fans every dispatchable schedule diff out onto the calendars it affects (ADR-059): inserting
    /// newly applicable lessons, patching changed ones, and deleting those that no longer apply.
    /// </summary>
    /// <remarks>
    /// This runs after diff calculation and, like it, is driven by diff state rather than this
    /// cycle's poll results, so a worker killed mid-fan-out re-runs the whole diff idempotently. It
    /// is gated by the same operational freeze as every other calendar job.
    /// </remarks>
    private async Task<bool> DispatchPendingDiffsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IncrementalCalendarSyncService sync = scope.ServiceProvider
                .GetRequiredService<IncrementalCalendarSyncService>();

            IncrementalCalendarSyncRunResult result = await sync.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Incremental calendar dispatch skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            foreach (IncrementalCalendarSyncDiffResult diff in result.Diffs)
            {
                LogIncrementalSyncResult(diff);
            }

            return result.Diffs.Any(
                static diff => diff.Outcome is IncrementalDispatchOutcome.PartiallyDispatched);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dispatching pending schedule diffs to calendars failed.");
            return false;
        }
    }

    private void LogIncrementalSyncResult(IncrementalCalendarSyncDiffResult diff)
    {
        switch (diff.Outcome)
        {
            case IncrementalDispatchOutcome.Dispatched:
                logger.LogInformation(
                    "Diff {DiffId} dispatched to calendars in {CalendarOperationsAttempted} "
                    + "mutation attempts: {Inserted} inserted, {Patched} patched, "
                    + "{Deleted} deleted; {ReauthFlagged} users flagged for re-authorization.",
                    diff.DiffId,
                    diff.CalendarOperationsAttempted,
                    diff.Inserted,
                    diff.Patched,
                    diff.Deleted,
                    diff.ReauthFlagged);
                break;

            case IncrementalDispatchOutcome.PartiallyDispatched:
                logger.LogInformation(
                    "Diff {DiffId} yielded after {CalendarOperationsAttempted} Calendar mutation "
                    + "attempts: {Inserted} inserted, {Patched} patched, {Deleted} deleted; "
                    + "{ReauthFlagged} users flagged for re-authorization. It remains pending.",
                    diff.DiffId,
                    diff.CalendarOperationsAttempted,
                    diff.Inserted,
                    diff.Patched,
                    diff.Deleted,
                    diff.ReauthFlagged);
                break;

            case IncrementalDispatchOutcome.Deferred:
                logger.LogWarning(
                    "Diff {DiffId} dispatch deferred after a transient failure and will retry: "
                    + "{FailureReason}",
                    diff.DiffId,
                    diff.FailureReason);
                break;

            case IncrementalDispatchOutcome.NoLongerPending:
                // Another pass handled it between the scan and the load; nothing to report.
                break;

            case IncrementalDispatchOutcome.Failed:
            default:
                logger.LogError(
                    "Diff {DiffId} dispatch failed after too many attempts and needs an operator: "
                    + "{FailureReason}",
                    diff.DiffId,
                    diff.FailureReason);
                break;
        }
    }

    /// <summary>
    /// Advances the one-time initial synchronization for users who asked to start it (ADR-058):
    /// creates their dedicated calendar and writes the events that apply to them.
    /// </summary>
    /// <remarks>
    /// Like publication and diffing, this is driven by connection state rather than by this
    /// cycle's poll results, so a worker killed mid-sync resumes here from what is not yet
    /// written. It is gated by the same operational freeze as every other calendar job.
    /// </remarks>
    private async Task<bool> RunPendingInitialSyncsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            InitialCalendarSyncService sync = scope.ServiceProvider
                .GetRequiredService<InitialCalendarSyncService>();

            InitialCalendarSyncRunResult result = await sync.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Initial calendar synchronization skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            foreach (InitialCalendarSyncResult user in result.Users)
            {
                LogInitialSyncResult(user);
            }

            return result.Users.Any(
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

    private void LogInitialSyncResult(InitialCalendarSyncResult user)
    {
        switch (user.Outcome)
        {
            case InitialCalendarSyncOutcome.Completed:
                logger.LogInformation(
                    "Initial calendar sync completed for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle of {ApplicableRecordCount} applicable.",
                    user.UserId,
                    user.EventsWritten,
                    user.ApplicableRecordCount);
                break;

            case InitialCalendarSyncOutcome.InProgress:
                logger.LogInformation(
                    "Initial calendar sync advanced for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle, more remain of {ApplicableRecordCount} applicable.",
                    user.UserId,
                    user.EventsWritten,
                    user.ApplicableRecordCount);
                break;

            case InitialCalendarSyncOutcome.ProfileMissing:
                logger.LogWarning(
                    "Initial calendar sync could not run for user {UserId}: no student profile "
                    + "was found, so nothing could be resolved.",
                    user.UserId);
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
                    + "{FailureReason}",
                    user.UserId,
                    user.FailureReason);
                break;
        }
    }

    private async Task RunDueCalendarInventoriesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CalendarInventoryReconciliationService inventory = scope.ServiceProvider
                .GetRequiredService<CalendarInventoryReconciliationService>();
            CalendarInventoryRunResult result =
                await inventory.RunDueAsync(cancellationToken);
            if (result.Frozen)
            {
                logger.LogInformation(
                    "Calendar inventory reconciliation skipped because the global operational "
                    + "freeze is active.");
                return;
            }

            foreach (CalendarInventoryUserResult user in result.Users)
            {
                LogCalendarInventoryResult(user);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running Calendar inventory reconciliation failed.");
        }
    }

    private void LogCalendarInventoryResult(CalendarInventoryUserResult user)
    {
        if (user.Outcome is CalendarInventoryOutcome.Completed
            or CalendarInventoryOutcome.CompletedWithConflicts)
        {
            LogLevel level = user.Outcome is CalendarInventoryOutcome.Completed
                ? LogLevel.Information
                : LogLevel.Warning;
            logger.Log(
                level,
                "Calendar inventory completed for user {UserId}: {Inserted} inserted, "
                + "{Patched} patched, {MappingsRecovered} mappings recovered, "
                + "{LedgerRowsUpdated} ledger rows updated; {Conflicts} conflicts, "
                + "{UnexpectedMappings} unexpected mappings and {UnexpectedEvents} "
                + "unexpected events were preserved without deletion.",
                user.UserId,
                user.Inserted,
                user.Patched,
                user.MappingsRecovered,
                user.LedgerRowsUpdated,
                user.Conflicts,
                user.UnexpectedMappings,
                user.UnexpectedEvents);
            return;
        }

        switch (user.Outcome)
        {
            case CalendarInventoryOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Calendar inventory stopped for user {UserId}; re-authorization is required.",
                    user.UserId);
                break;
            case CalendarInventoryOutcome.CalendarRepairRequired:
                logger.LogError(
                    "The managed calendar for user {UserId} is unavailable; normal writes are "
                    + "blocked and explicit repair is required.",
                    user.UserId);
                break;
            case CalendarInventoryOutcome.Deferred:
                logger.LogWarning(
                    "Calendar inventory deferred for user {UserId}: {FailureReason}",
                    user.UserId,
                    user.FailureReason);
                break;
            case CalendarInventoryOutcome.Failed:
            default:
                logger.LogError(
                    "Calendar inventory failed for user {UserId}: {FailureReason}",
                    user.UserId,
                    user.FailureReason);
                break;
        }
    }

    /// <summary>
    /// Replays semantic diffs missed while a completed user's Calendar credential was unavailable
    /// (ADR-060). It runs after global dispatch, follows each user's ordered cursor, and never derives
    /// deletion from current-state absence.
    /// </summary>
    private async Task<bool> RunPendingCalendarReconciliationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CalendarReconciliationService reconciliation = scope.ServiceProvider
                .GetRequiredService<CalendarReconciliationService>();

            CalendarReconciliationRunResult result =
                await reconciliation.RunPendingAsync(cancellationToken);
            if (result.Frozen)
            {
                logger.LogInformation(
                    "Calendar reconciliation skipped because the global operational freeze "
                    + "is active.");
                return false;
            }

            foreach (CalendarReconciliationUserResult user in result.Users)
            {
                LogCalendarReconciliationResult(user);
            }

            return result.Users.Any(
                static user => user.Outcome is CalendarReconciliationOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending Calendar reconciliations failed.");
            return false;
        }
    }

    private void LogCalendarReconciliationResult(CalendarReconciliationUserResult user)
    {
        switch (user.Outcome)
        {
            case CalendarReconciliationOutcome.Completed:
                logger.LogInformation(
                    "Calendar reconciliation completed for user {UserId}; no dispatched diff "
                    + "remains after the durable cursor.",
                    user.UserId);
                break;

            case CalendarReconciliationOutcome.InProgress:
                logger.LogInformation(
                    "Calendar reconciliation advanced for user {UserId}: {DiffsReplayed} diffs, "
                    + "{Inserted} inserted, {Patched} patched, {Deleted} deleted.",
                    user.UserId,
                    user.DiffsReplayed,
                    user.Inserted,
                    user.Patched,
                    user.Deleted);
                break;

            case CalendarReconciliationOutcome.Deferred:
                logger.LogWarning(
                    "Calendar reconciliation deferred for user {UserId}; the cursor was preserved: "
                    + "{FailureReason}",
                    user.UserId,
                    user.FailureReason);
                break;

            case CalendarReconciliationOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Calendar reconciliation stopped for user {UserId} because the refreshed "
                    + "credential was rejected again; the cursor was preserved.",
                    user.UserId);
                break;

            case CalendarReconciliationOutcome.Failed:
            default:
                logger.LogError(
                    "Calendar reconciliation failed for user {UserId}; the cursor was preserved: "
                    + "{FailureReason}",
                    user.UserId,
                    user.FailureReason);
                break;
        }
    }

    private async Task PollAllSourcesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduleSource> sources;
        await using (AsyncServiceScope listScope = scopeFactory.CreateAsyncScope())
        {
            IScheduleSourceStore store = listScope.ServiceProvider
                .GetRequiredService<IScheduleSourceStore>();
            sources = await store.ListAsync(onlyPollingEnabled: true, cancellationToken);
        }

        foreach (ScheduleSource source in sources)
        {
            await using AsyncServiceScope sourceScope = scopeFactory.CreateAsyncScope();

            try
            {
                IOperationalFreezeStore freeze = sourceScope.ServiceProvider
                    .GetRequiredService<IOperationalFreezeStore>();
                OperationalFreezeSnapshot state = await freeze.GetAsync(cancellationToken);
                if (state.IsFrozen)
                {
                    logger.LogWarning(
                        "Source polling is frozen. No acquisition will start. "
                        + "Last change at {FreezeChangedAtUtc} by {FreezeChangedBy}: {FreezeReason}",
                        state.ChangedAtUtc,
                        state.ChangedBy,
                        state.Reason);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Reading the authoritative switch failed. Stop the whole source
                // loop: trying the next source would turn an unavailable safety
                // control into an implicit "unfrozen" state.
                logger.LogError(
                    exception,
                    "The global operational freeze state could not be read. "
                    + "Source polling is stopped for this cycle.");
                return;
            }

            try
            {
                ScheduleSourcePoller poller = sourceScope.ServiceProvider
                    .GetRequiredService<ScheduleSourcePoller>();
                ScheduleSourcePollResult result = await poller.PollAsync(
                    source,
                    cancellationToken);
                logger.LogInformation(
                    "Source {SourceId} poll completed with {Outcome}; snapshot changed: {Changed}; "
                    + "parse run: {ParseRunId}; revision: {RevisionId}.",
                    result.SourceId,
                    result.Outcome,
                    result.SnapshotChanged,
                    result.ParseRunId,
                    result.RevisionId);

                if (result.ParseRunStartKind is ParseRunStartKind.RecoveredStale)
                {
                    // Not an error for this cycle, but it means an earlier worker
                    // died mid-parse. Repeated occurrences point at the host, not
                    // at the source.
                    logger.LogWarning(
                        "Parse run {ParseRunId} for source {SourceId} was recovered after being "
                        + "left running by a worker that stopped mid-parse.",
                        result.ParseRunId,
                        result.SourceId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Polling source {SourceId} failed.", source.SourceId);
            }
        }
    }
}
