using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Worker.Notifications;

namespace Sirkadiyen.Worker.Sources;

/// <summary>
/// Validates revisions a previous cycle left in <c>Parsed</c> (ADR-135).
/// </summary>
/// <remarks>
/// The poller validates the revision it has just created, inside the same call. Everything about
/// that is fine until it does not happen: the parse persists in its own transaction, so a crash,
/// a cancelled cycle, or an exception raised between persistence and validation leaves a revision
/// in <c>Parsed</c>. <see cref="ScheduleRevisionValidationService.ValidatePendingAsync"/> was
/// written for exactly that case and documented as the safety net, and nothing ever called it —
/// so a stranded revision stayed stranded for good. It is never retried, never published, never
/// rejected, and appears on the sources page as a source whose newest revision simply sits in
/// <c>Parsed</c> with nothing explaining why.
/// <para>
/// This runs before publication in the cycle, so a revision recovered here reaches the publication
/// step in the same pass rather than one later.
/// </para>
/// </remarks>
internal sealed class RevisionValidationTask(
    IServiceScopeFactory scopeFactory,
    IOperatorAlertNotifier alerts,
    ILogger<RevisionValidationTask> logger)
{
    private const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleRevisionValidationService validation = scope.ServiceProvider
                .GetRequiredService<ScheduleRevisionValidationService>();
            IReadOnlyList<RevisionValidationOutcome> outcomes =
                await validation.ValidatePendingAsync(BatchSize, cancellationToken);

            foreach (RevisionValidationOutcome outcome in outcomes)
            {
                if (outcome.Result is null)
                {
                    // The rest of the batch was validated; this one was skipped and stays in
                    // Parsed for the next pass. Logged as an error because a revision that
                    // cannot be validated at all will otherwise be retried silently forever.
                    logger.LogError(
                        outcome.Failure,
                        "Revision {RevisionId} could not be validated and was skipped; the rest "
                        + "of the batch continued.",
                        outcome.RevisionId);
                    await alerts.SendAsync(
                        WorkerAlerts.RevisionValidationFailed(
                            outcome.RevisionId,
                            outcome.Failure),
                        cancellationToken);
                    continue;
                }

                // Logged at warning: a revision reaching this task is one the poller was supposed
                // to have validated, so it is evidence that a cycle did not finish, even though
                // the revision itself is now recovered.
                logger.LogWarning(
                    "Revision {RevisionId} was left unvalidated by an earlier cycle and has now "
                    + "been validated as {Outcome} with {FindingCount} finding(s).",
                    outcome.RevisionId,
                    outcome.Result.Outcome,
                    outcome.Result.Findings.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Validating revisions left behind by an earlier cycle failed.");
            await alerts.SendAsync(
                WorkerAlerts.StageFailed("revizyon doğrulama", exception),
                cancellationToken);
        }
    }
}
