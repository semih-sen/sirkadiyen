using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Worker.Notifications;

namespace Sirkadiyen.Worker.Sources;

internal sealed class RevisionPublicationTask(
    IServiceScopeFactory scopeFactory,
    IOperatorAlertNotifier alerts,
    ILogger<RevisionPublicationTask> logger)
{
    private const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleRevisionPublicationService publication = scope.ServiceProvider
                .GetRequiredService<ScheduleRevisionPublicationService>();
            IReadOnlyList<RevisionPublicationResult> results =
                await publication.PublishPendingAsync(BatchSize, cancellationToken);

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
            await alerts.SendAsync(
                WorkerAlerts.StageFailed("revizyon yayınlama", exception),
                cancellationToken);
        }
    }
}
