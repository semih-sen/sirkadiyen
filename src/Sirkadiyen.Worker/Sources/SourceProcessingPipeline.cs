using Sirkadiyen.Worker.Health;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourceProcessingPipeline(
    SourcePollingTask polling,
    RevisionValidationTask revisionValidation,
    RevisionPublicationTask publication,
    ScheduleDiffCalculationTask diffCalculation,
    WorkerHealthState healthState)
{
    /// <summary>
    /// Acquires the polling-enabled sources. This is the one stage that is genuinely
    /// cadenced: it talks to Google on an adaptive interval, so it runs only on a poll cycle.
    /// </summary>
    public async Task PollSourcesAsync(CancellationToken cancellationToken)
    {
        healthState.MarkActivity("polling-sources");
        await polling.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Moves whatever revisions already exist one step towards a student's calendar:
    /// validate what is parsed, publish what is validated, diff what is published.
    /// </summary>
    /// <remarks>
    /// None of these three stages reads a source, so none of them needs a poll to run
    /// (ADR-151). They are queue-driven database reads that no-op when their queue is
    /// empty, and they must run every cycle — an administratively uploaded revision is
    /// validated the moment it is parsed but is published by nothing until this runs, so
    /// coupling publication to the adaptive source-poll interval left an upload sitting in
    /// <c>Validated</c> for up to a full interval before it could reach a calendar.
    /// <para>
    /// Ordered validate → publish → diff so a revision an earlier cycle left in
    /// <c>Parsed</c> is validated, published and diffed in this one pass rather than one
    /// stage per cycle (ADR-135).
    /// </para>
    /// </remarks>
    public async Task AdvanceRevisionsAsync(CancellationToken cancellationToken)
    {
        healthState.MarkActivity("validating-revisions");
        await revisionValidation.RunAsync(cancellationToken);
        healthState.MarkActivity("publishing-revisions");
        await publication.RunAsync(cancellationToken);
        healthState.MarkActivity("calculating-diffs");
        await diffCalculation.RunAsync(cancellationToken);
    }
}
