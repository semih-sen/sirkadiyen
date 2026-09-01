using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Worker.Sources;

/// <summary>
/// Says out loud what the pipeline is waiting for, and stays silent when it is
/// waiting for nothing.
/// </summary>
/// <remarks>
/// The report is written as one log line per stuck kind, at warning, naming the
/// count, the age and the source to look at. One line rather than a summary
/// object because this is what an operator greps for, and warning rather than
/// error because none of these are failures — they are decisions nobody has been
/// asked to make yet.
/// <para>
/// Repetition is the point. Each cycle that a stall survives says so again, so
/// the last thing in the journal is always the current state rather than the
/// moment it started. A clean pipeline writes nothing at all.
/// </para>
/// </remarks>
internal sealed class PipelineStallWatchTask(
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineStallWatchTask> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            PipelineStallWatch watch = scope.ServiceProvider
                .GetRequiredService<PipelineStallWatch>();
            PipelineStallReport report = await watch.InspectAsync(cancellationToken);

            if (!report.IsStalled)
            {
                return;
            }

            Report(
                report.RevisionsAwaitingReview,
                "{Count} revision(s) have been waiting for review since {OldestSinceUtc}, the "
                + "oldest on {SourceId}. Nothing they contain reaches a student calendar until "
                + "someone approves or rejects them.");

            Report(
                report.RevisionsStuckBeforeValidation,
                "{Count} revision(s) created before {OldestSinceUtc} have still not been "
                + "validated, the oldest on {SourceId}. Validation recovery runs every cycle, so "
                + "these are revisions it cannot process rather than a backlog.");

            Report(
                report.DiffsAwaitingRelease,
                "{Count} diff(s) have been held since {OldestSinceUtc}, the oldest on {SourceId}. "
                + "A held diff is never dispatched on its own; it waits for a named operator to "
                + "release or discard it.");

            Report(
                report.FailedDispatches,
                "{Count} diff(s) have given up on calendar dispatch, the oldest from "
                + "{OldestSinceUtc} on {SourceId}. Their changes are missing from student "
                + "calendars until an operator retries them.");

            Report(
                report.SourcesNotPolled,
                "{Count} source(s) that used to be acquired have not been read since "
                + "{OldestSinceUtc}, the oldest {SourceId}. A source can keep polling successfully "
                + "while quietly tracking a document that no longer changes.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The watch is the thing that reports trouble, so it must never become
            // trouble: a failure here is logged and the cycle continues.
            logger.LogError(exception, "Inspecting the pipeline for stalled work failed.");
        }
    }

    private void Report(StalledWork work, string message)
    {
        if (work.Count == 0)
        {
            return;
        }

#pragma warning disable CA2254 // The template is a constant chosen by the caller above.
        logger.LogWarning(message, work.Count, work.OldestSinceUtc, work.OldestSourceId);
#pragma warning restore CA2254
    }
}
