using Sirkadiyen.Worker.Health;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourceProcessingPipeline(
    SourcePollingTask polling,
    RevisionPublicationTask publication,
    ScheduleDiffCalculationTask diffCalculation,
    WorkerHealthState healthState)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        healthState.MarkActivity("polling-sources");
        await polling.RunAsync(cancellationToken);
        healthState.MarkActivity("publishing-revisions");
        await publication.RunAsync(cancellationToken);
        healthState.MarkActivity("calculating-diffs");
        await diffCalculation.RunAsync(cancellationToken);
    }
}
