using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Worker.Health;

/// <summary>
/// Watches the cycle loop from outside it and says so when it stops advancing.
/// </summary>
/// <remarks>
/// The worker publishes its heartbeat from inside the loop, at cycle boundaries, so a loop that
/// stops mid-cycle stops publishing too: the last row keeps saying what the worker was doing
/// when it was last healthy, and nothing says that it stopped. That is how a stalled Calendar
/// stage went unnoticed for a day, with the only recovery being someone noticing and restarting
/// the service by hand. This runs as its own hosted service, on its own timer, precisely so that
/// a loop wedged inside a stage cannot silence it.
/// <para>
/// It reports; ending the worker is opt-in (<see cref="WorkerStallWatchdogOptions.RestartAfter"/>).
/// </para>
/// </remarks>
internal sealed class WorkerStallWatchdog(
    WorkerHealthState healthState,
    WorkerStallWatchdogOptions options,
    IOperatorAlertNotifier alerts,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider,
    ILogger<WorkerStallWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CheckInterval, timeProvider, stoppingToken);
                await InspectAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The watchdog must outlive anything it fails at; a watchdog that dies of its
                // own exception is worse than none, because the silence then looks deliberate.
                logger.LogError(exception, "The worker stall watchdog check failed; continuing.");
            }
        }
    }

    private async Task InspectAsync(CancellationToken cancellationToken)
    {
        WorkerHealthSnapshot snapshot = healthState.GetSnapshot();
        WorkerStallVerdict verdict = WorkerStallWatchdogPolicy.Decide(
            snapshot,
            options,
            timeProvider.GetUtcNow());
        if (!verdict.Report)
        {
            return;
        }

        logger.LogError(
            "Worker instance {InstanceId} has not advanced past stage {Stage} for {StalledFor}. "
            + "The cycle loop is not progressing, so calendar work is not being dispatched.",
            snapshot.InstanceId,
            snapshot.CurrentStage,
            verdict.StalledFor);

        // Keyed by stage rather than by instance or by moment: a standing stall repeats on the
        // notifier's cooldown, which is what tells an operator it is still standing, while a
        // stall that has moved to another stage is a different thing to be told about.
        await alerts.SendAsync(
            new OperatorAlert
            {
                Title = "Worker cycle stopped advancing",
                Severity = OperatorAlertSeverity.Error,
                DedupeKey = $"worker-stall:{snapshot.CurrentStage}",
                Detail =
                    "The worker is running but its cycle has not reached the next stage. "
                    + "Calendar work, including new students' initial synchronization, waits "
                    + "behind it.",
                Fields =
                [
                    new OperatorAlertField("Instance", snapshot.InstanceId),
                    new OperatorAlertField("Stage", snapshot.CurrentStage),
                    new OperatorAlertField(
                        "Stalled for",
                        verdict.StalledFor.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),
                ],
            },
            cancellationToken);

        if (!verdict.EndWorker)
        {
            return;
        }

        logger.LogCritical(
            "Worker instance {InstanceId} has been stalled in stage {Stage} for {StalledFor}, "
            + "past the configured restart threshold. Ending this worker so the service manager "
            + "starts a fresh one; every calendar stage resumes from what is durably recorded.",
            snapshot.InstanceId,
            snapshot.CurrentStage,
            verdict.StalledFor);
        lifetime.StopApplication();
    }
}
