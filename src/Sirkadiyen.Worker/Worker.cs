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

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAllSourcesAsync(stoppingToken);
            await PublishValidatedRevisionsAsync(stoppingToken);
            await CalculatePendingDiffsAsync(stoppingToken);
            await RunPendingInitialSyncsAsync(stoppingToken);
            await PruneExpiredSnapshotPayloadsAsync(stoppingToken);

            TimeSpan interval = intervalPolicy.GetInterval(timeProvider.GetUtcNow());
            logger.LogInformation(
                "Schedule polling cycle completed. Next cycle starts in {PollingInterval}.",
                interval);
            await Task.Delay(interval, timeProvider, stoppingToken);
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
    /// Advances the one-time initial synchronization for users who asked to start it (ADR-058):
    /// creates their dedicated calendar and writes the events that apply to them.
    /// </summary>
    /// <remarks>
    /// Like publication and diffing, this is driven by connection state rather than by this
    /// cycle's poll results, so a worker killed mid-sync resumes here from what is not yet
    /// written. It is gated by the same operational freeze as every other calendar job.
    /// </remarks>
    private async Task RunPendingInitialSyncsAsync(CancellationToken cancellationToken)
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
                return;
            }

            foreach (InitialCalendarSyncResult user in result.Users)
            {
                LogInitialSyncResult(user);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending initial calendar syncs failed.");
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
