using Sirkadiyen.Application.Operations;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The watch that lets the pipeline say it is stuck.
/// </summary>
/// <remarks>
/// What matters here is the arithmetic of the cutoffs — each kind of waiting is
/// measured against its own threshold, from one clock — and that a healthy
/// pipeline reports nothing at all. A watch that cries every cycle is a watch
/// nobody reads.
/// </remarks>
public sealed class PipelineStallWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AQuietPipelineReportsNothing()
    {
        FakeStallReadStore store = new();
        PipelineStallWatch watch = new(store, new PipelineStallOptions(), Clock());

        PipelineStallReport report = await watch.InspectAsync(CancellationToken.None);

        Assert.False(report.IsStalled);
        Assert.Equal(Now, report.ObservedAtUtc);
    }

    [Fact]
    public async Task EachKindOfWaitingIsMeasuredAgainstItsOwnThreshold()
    {
        FakeStallReadStore store = new();
        PipelineStallOptions options = new()
        {
            ReviewAge = TimeSpan.FromHours(48),
            UnvalidatedAge = TimeSpan.FromHours(2),
            DiffHoldAge = TimeSpan.FromHours(24),
            PollSilence = TimeSpan.FromHours(12),
        };

        await new PipelineStallWatch(store, options, Clock())
            .InspectAsync(CancellationToken.None);

        Assert.Equal(Now.AddHours(-48), store.ReviewCutoff);
        Assert.Equal(Now.AddHours(-2), store.UnvalidatedCutoff);
        Assert.Equal(Now.AddHours(-24), store.DiffHoldCutoff);
        Assert.Equal(Now.AddHours(-12), store.PollSilenceCutoff);
    }

    [Fact]
    public async Task OneStuckKindIsEnoughToReportAStall()
    {
        // Held revisions are the queue an operator works, so this is the ordinary
        // case: nothing is broken, and something is nonetheless waiting.
        FakeStallReadStore store = new()
        {
            AwaitingReview = new StalledWork
            {
                Count = 13,
                OldestSinceUtc = Now.AddDays(-3),
                OldestSourceId = "G2-EN-ANNUAL",
            },
        };

        PipelineStallReport report = await new PipelineStallWatch(
            store,
            new PipelineStallOptions(),
            Clock()).InspectAsync(CancellationToken.None);

        Assert.True(report.IsStalled);
        Assert.Equal(13, report.RevisionsAwaitingReview.Count);
        Assert.Equal("G2-EN-ANNUAL", report.RevisionsAwaitingReview.OldestSourceId);
        Assert.Equal(0, report.FailedDispatches.Count);
    }

    [Fact]
    public void AThresholdOfZeroIsRefused()
    {
        // A zero threshold would report every revision the moment it was created,
        // which is the same as reporting nothing.
        PipelineStallOptions options = new() { ReviewAge = TimeSpan.Zero };

        Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(
            options.Validate);
    }

    private static TimeProvider Clock() => new FixedTimeProvider(Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStallReadStore : IPipelineStallReadStore
    {
        public StalledWork AwaitingReview { get; init; } = StalledWork.None;

        public DateTimeOffset? ReviewCutoff { get; private set; }

        public DateTimeOffset? UnvalidatedCutoff { get; private set; }

        public DateTimeOffset? DiffHoldCutoff { get; private set; }

        public DateTimeOffset? PollSilenceCutoff { get; private set; }

        public Task<StalledWork> CountRevisionsAwaitingReviewAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            ReviewCutoff = cutoffUtc;
            return Task.FromResult(AwaitingReview);
        }

        public Task<StalledWork> CountRevisionsStuckBeforeValidationAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            UnvalidatedCutoff = cutoffUtc;
            return Task.FromResult(StalledWork.None);
        }

        public Task<StalledWork> CountDiffsAwaitingReleaseAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            DiffHoldCutoff = cutoffUtc;
            return Task.FromResult(StalledWork.None);
        }

        public Task<StalledWork> CountFailedDispatchesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StalledWork.None);

        public Task<StalledWork> CountSourcesNotPolledSinceAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            PollSilenceCutoff = cutoffUtc;
            return Task.FromResult(StalledWork.None);
        }
    }
}
