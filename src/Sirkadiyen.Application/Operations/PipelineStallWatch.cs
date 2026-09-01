using System.ComponentModel.DataAnnotations;

namespace Sirkadiyen.Application.Operations;

/// <summary>
/// Answers the one question the pipeline could not answer about itself: is
/// anything stuck, and for how long.
/// </summary>
/// <remarks>
/// Every stage of this system is careful never to lose work — a failed dispatch
/// waits for an operator, a held diff waits for a release, a quarantined revision
/// waits for approval. Each of those is correct on its own, and together they
/// produce a pipeline that can sit silently blocked for weeks while every
/// component reports itself healthy. A schedule nobody publishes is
/// indistinguishable from a schedule nobody changed, from the inside.
/// <para>
/// This is what makes the difference visible. It reads state that already exists
/// and reports only what has been waiting longer than it should, so a quiet
/// system stays quiet and a stuck one says so — without asking anyone to open the
/// panel and go looking.
/// </para>
/// <para>
/// It decides nothing and changes nothing. Deciding what a stall means is the
/// operator's, and every one of these conditions is deliberately a state a human
/// is meant to resolve; the fault is only that nothing said so.
/// </para>
/// </remarks>
public sealed class PipelineStallWatch(
    IPipelineStallReadStore store,
    PipelineStallOptions options,
    TimeProvider timeProvider)
{
    public async Task<PipelineStallReport> InspectAsync(CancellationToken cancellationToken)
    {
        options.Validate();

        DateTimeOffset now = timeProvider.GetUtcNow();

        return new PipelineStallReport
        {
            ObservedAtUtc = now,

            // A revision held for review is the queue an operator is supposed to
            // work. Age is what separates "working it" from "nobody knows it is
            // there".
            RevisionsAwaitingReview = await store.CountRevisionsAwaitingReviewAsync(
                now - options.ReviewAge,
                cancellationToken),

            // Parsed means a cycle created a revision and never validated it.
            // Validation recovery picks these up every cycle, so one that is still
            // here after the grace period is one recovery could not process.
            RevisionsStuckBeforeValidation = await store.CountRevisionsStuckBeforeValidationAsync(
                now - options.UnvalidatedAge,
                cancellationToken),

            // Held diffs and failed dispatches both wait for a named operator by
            // design (ADR-042, ADR-097). Neither ever times out on its own.
            DiffsAwaitingRelease = await store.CountDiffsAwaitingReleaseAsync(
                now - options.DiffHoldAge,
                cancellationToken),

            FailedDispatches = await store.CountFailedDispatchesAsync(cancellationToken),

            // A source that has not been read in this long is either unreachable
            // or quietly frozen on a document that no longer exists — the failure
            // mode a discovery fallback produces (ADR-133).
            SourcesNotPolled = await store.CountSourcesNotPolledSinceAsync(
                now - options.PollSilence,
                cancellationToken),
        };
    }
}

/// <summary>What is waiting, and has been waiting too long.</summary>
public sealed record PipelineStallReport
{
    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required StalledWork RevisionsAwaitingReview { get; init; }

    public required StalledWork RevisionsStuckBeforeValidation { get; init; }

    public required StalledWork DiffsAwaitingRelease { get; init; }

    public required StalledWork FailedDispatches { get; init; }

    public required StalledWork SourcesNotPolled { get; init; }

    /// <summary>Whether anything at all is stuck.</summary>
    public bool IsStalled =>
        RevisionsAwaitingReview.Count > 0
        || RevisionsStuckBeforeValidation.Count > 0
        || DiffsAwaitingRelease.Count > 0
        || FailedDispatches.Count > 0
        || SourcesNotPolled.Count > 0;
}

/// <summary>
/// One kind of stuck work: how much of it, and the oldest example.
/// </summary>
/// <remarks>
/// The oldest item is carried because a count alone cannot be acted on. "Three
/// revisions are held" says nothing; "three are held, the oldest since Monday, on
/// G2-EN-ANNUAL" names where to look.
/// </remarks>
public sealed record StalledWork
{
    public static readonly StalledWork None = new() { Count = 0 };

    public required int Count { get; init; }

    /// <summary>When the oldest item entered this state, if there is one.</summary>
    public DateTimeOffset? OldestSinceUtc { get; init; }

    /// <summary>The source the oldest item belongs to, when it belongs to one.</summary>
    public string? OldestSourceId { get; init; }
}

/// <summary>How long each kind of waiting is allowed to last before it is said out loud.</summary>
/// <remarks>
/// These are not deadlines the pipeline enforces; nothing is cancelled or retried
/// when one passes. They are the point at which silence stops being accurate.
/// </remarks>
public sealed class PipelineStallOptions
{
    public const string SectionName = "PipelineStall";

    /// <summary>
    /// How long a revision may wait for review before it is reported. Two working
    /// days: a schedule change nobody has looked at by then is one students are
    /// already living without.
    /// </summary>
    public TimeSpan ReviewAge { get; set; } = TimeSpan.FromHours(48);

    /// <summary>
    /// How long a revision may sit unvalidated. Generous next to the cycle that is
    /// supposed to clear it within minutes, because anything here is a fault, not
    /// a backlog.
    /// </summary>
    public TimeSpan UnvalidatedAge { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How long a held diff may wait for an operator to release or discard it.</summary>
    public TimeSpan DiffHoldAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a source may go unread before it is reported as no longer tracked.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than the slowest adaptive polling interval, so an
    /// ordinary quiet night is never mistaken for a source that stopped being
    /// acquired.
    /// </remarks>
    public TimeSpan PollSilence { get; set; } = TimeSpan.FromHours(12);

    public void Validate()
    {
        foreach ((string name, TimeSpan value) in new[]
        {
            (nameof(ReviewAge), ReviewAge),
            (nameof(UnvalidatedAge), UnvalidatedAge),
            (nameof(DiffHoldAge), DiffHoldAge),
            (nameof(PollSilence), PollSilence),
        })
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ValidationException($"{name} must be a positive duration.");
            }
        }
    }
}
