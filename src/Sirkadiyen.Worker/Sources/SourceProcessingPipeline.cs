using Sirkadiyen.Worker.Health;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourceProcessingPipeline(
    SourcePollingTask polling,
    RevisionValidationTask revisionValidation,
    RevisionPublicationTask publication,
    ScheduleDiffCalculationTask diffCalculation,
    WorkerHealthState healthState)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        healthState.MarkActivity("polling-sources");
        await polling.RunAsync(cancellationToken);

        // Before publication, so a revision an earlier cycle left in Parsed is validated and
        // published in this pass rather than in the next one (ADR-135).
        healthState.MarkActivity("validating-revisions");
        await revisionValidation.RunAsync(cancellationToken);
        healthState.MarkActivity("publishing-revisions");
        await publication.RunAsync(cancellationToken);
        healthState.MarkActivity("calculating-diffs");
        await diffCalculation.RunAsync(cancellationToken);
    }
}
