using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Sirkadiyen.Worker.Calendars;
using Sirkadiyen.Worker.Configuration;
using Sirkadiyen.Worker.Health;
using Sirkadiyen.Worker.Scheduling;
using Sirkadiyen.Worker.Sources;

namespace Sirkadiyen.Worker;

internal sealed class Worker(
    SourceCatalogInitializer catalogInitializer,
    SourceProcessingPipeline sourcePipeline,
    ManualSourcePollTask manualSourcePoll,
    FencedCalendarMaintenanceTask calendarMaintenance,
    WorkerHeartbeatTask heartbeat,
    SnapshotRetentionTask snapshotRetention,
    WorkerOptions options,
    AdaptivePollingIntervalPolicy intervalPolicy,
    TimeProvider timeProvider,
    WorkerHealthState healthState,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sirkadiyen worker started.");
        try
        {
            await InitializeAsync(stoppingToken);
            await RunCyclesAsync(stoppingToken);
        }
        finally
        {
            healthState.MarkStopped();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        healthState.MarkActivity("seeding-source-catalog");
        await catalogInitializer.InitializeAsync(cancellationToken);
        healthState.MarkReady("ready");
    }

    private async Task RunCyclesAsync(CancellationToken cancellationToken)
    {
        bool pollScheduleSources = true;
        DateTimeOffset nextSourcePollAt = timeProvider.GetUtcNow();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Publish this instance's liveness and current stage each iteration (ADR-124) so the
            // admin monitor can see every running instance — and that more than one is running.
            await heartbeat.RunAsync(cancellationToken);

            // Operator-requested polls run every cycle, independently of the adaptive schedule, so a
            // manual "poll now" takes effect within the idle-check interval (ADR-127).
            healthState.MarkActivity("draining-manual-polls");
            await manualSourcePoll.RunAsync(cancellationToken);

            if (pollScheduleSources)
            {
                await sourcePipeline.RunAsync(cancellationToken);
                DateTimeOffset intervalSelectedAt = timeProvider.GetUtcNow();
                nextSourcePollAt = intervalSelectedAt + intervalPolicy.GetInterval(intervalSelectedAt);
            }

            bool calendarCatchUpRequired = await RunCalendarWorkAsync(cancellationToken);

            if (pollScheduleSources)
            {
                healthState.MarkActivity("snapshot-retention");
                await snapshotRetention.RunAsync(cancellationToken);
            }

            WorkerCycleSchedule next = WorkerCycleScheduler.GetNext(
                calendarCatchUpRequired,
                nextSourcePollAt,
                options,
                timeProvider.GetUtcNow());
            pollScheduleSources = next.PollScheduleSources;
            LogNextCycle(next, calendarCatchUpRequired, nextSourcePollAt);

            // Publish the next intended source poll so the admin monitor can show it (ADR-127).
            healthState.SetNextSourcePollAt(nextSourcePollAt);

            healthState.MarkActivity("waiting");
            await heartbeat.RunAsync(cancellationToken);
            await Task.Delay(next.Delay, timeProvider, cancellationToken);
        }
    }

    private async Task<bool> RunCalendarWorkAsync(CancellationToken cancellationToken)
    {
        // Initial sync, dispatch, replay, resync, announcements and inventory now all run inside one
        // shared cross-instance advisory lease (ADR-122), so only one worker performs calendar work at
        // a time. Initial sync used to run here unfenced, which let two instances race on a user's
        // non-idempotent calendar creation.
        healthState.MarkActivity("calendar-maintenance");
        return await calendarMaintenance.RunAsync(cancellationToken);
    }

    private void LogNextCycle(
        WorkerCycleSchedule next,
        bool calendarCatchUpRequired,
        DateTimeOffset nextSourcePollAt)
    {
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
    }
}
