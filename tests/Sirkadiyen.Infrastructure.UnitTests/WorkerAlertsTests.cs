using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Worker.Notifications;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// What each worker event is said to be, and how loudly (ADR-144).
/// </summary>
public sealed class WorkerAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly SourceId Source = SourceId.Parse("G1-TR-ANNUAL");

    [Fact]
    public void AValidatedRevisionIsInformationBecauseNobodyHasToDoAnything()
    {
        OperatorAlert alert = WorkerAlerts.RevisionCreated(
            Source,
            Guid.CreateVersion7(),
            RevisionState.Validated,
            findingCount: 0);

        Assert.Equal(OperatorAlertSeverity.Info, alert.Severity);
    }

    [Theory]
    [InlineData(RevisionState.ReviewRequired)]
    [InlineData(RevisionState.Rejected)]
    [InlineData(RevisionState.Parsed)]
    public void ARevisionThatWillNotReachACalendarOnItsOwnIsAWarning(RevisionState state)
    {
        // Each of these is a revision that exists and stops there: quarantined, refused, or never
        // validated. The severity is the difference between "a schedule changed" and "a schedule
        // changed and is going nowhere".
        OperatorAlert alert = WorkerAlerts.RevisionCreated(
            Source,
            Guid.CreateVersion7(),
            state,
            findingCount: 3);

        Assert.Equal(OperatorAlertSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void EachRevisionIsAnnouncedOnceBecauseItsKeyNamesIt()
    {
        Guid revisionId = Guid.CreateVersion7();

        Assert.Equal(
            $"revision-created:{revisionId}",
            WorkerAlerts.RevisionCreated(Source, revisionId, RevisionState.Validated, 0).DedupeKey);
        Assert.NotEqual(
            WorkerAlerts.RevisionCreated(Source, Guid.CreateVersion7(), null, null).DedupeKey,
            WorkerAlerts.RevisionCreated(Source, Guid.CreateVersion7(), null, null).DedupeKey);
    }

    [Fact]
    public void AnUnreadableSourceIsKeyedBySourceSoItIsNotRepeatedEveryCycle()
    {
        // It fails again on the next poll, and the one after that, until somebody fixes the
        // sharing permission. A per-occurrence key would be a message every fifteen minutes.
        OperatorAlert first = WorkerAlerts.SourcePollFailed(Source, new IOException("403"));
        OperatorAlert second = WorkerAlerts.SourcePollFailed(Source, new IOException("403"));

        Assert.Equal(first.DedupeKey, second.DedupeKey);
        Assert.Equal("source-poll-failed:G1-TR-ANNUAL", first.DedupeKey);
        Assert.Equal(OperatorAlertSeverity.Error, first.Severity);
    }

    [Fact]
    public void AFailedValidationSurfacesTheInnerCauseNotTheGenericWrapper()
    {
        // The exact fault flooding the channel: a DbUpdateException whose own message names no cause.
        // The detail an operator reads has to carry the inner exception, or every alert says only
        // "See the inner exception for details" and nothing to act on.
        var failure = new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            new InvalidOperationException(
                "23505: duplicate key value violates unique constraint \"ix_records_identity\""));

        OperatorAlert alert = WorkerAlerts.RevisionValidationFailed(Guid.CreateVersion7(), failure);

        string detail = alert.Fields.Single(field => field.Label == "Ayrıntı").Value;
        Assert.Contains("duplicate key value violates unique constraint", detail);
    }

    [Fact]
    public void AnOrdinaryDiffReportsTheCountsAsInformation()
    {
        OperatorAlert alert = WorkerAlerts.DiffCalculated(Diff(ScheduleDiffChange.Created, 12));

        Assert.Equal(OperatorAlertSeverity.Info, alert.Severity);
        Assert.Contains(
            alert.Fields,
            field => field.Value.Contains("12 yeni", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeldDiffIsAWarningThatCarriesWhyItWasHeld()
    {
        // A held diff reaches no calendar at all until an operator releases it, so the hold
        // reason is the whole content of the message.
        ScheduleDiff diff = Diff(ScheduleDiffChange.Deleted, 300);
        Assert.Equal(ScheduleDiffState.Held, diff.State);

        OperatorAlert alert = WorkerAlerts.DiffCalculated(diff);

        Assert.Equal(OperatorAlertSeverity.Warning, alert.Severity);
        Assert.Contains(alert.Fields, field => field.Label == "Tutulma sebebi");
        Assert.Equal(diff.HoldReason, alert.Fields.Single(f => f.Label == "Tutulma sebebi").Value);
    }

    [Fact]
    public void AStallIsOneMessageListingOnlyTheKindsThatAreActuallyStuck()
    {
        OperatorAlert alert = WorkerAlerts.PipelineStalled(new PipelineStallReport
        {
            ObservedAtUtc = Now,
            RevisionsAwaitingReview = new StalledWork
            {
                Count = 24,
                OldestSinceUtc = Now.AddDays(-14),
                OldestSourceId = "G3-TR-A",
            },
            RevisionsStuckBeforeValidation = StalledWork.None,
            DiffsAwaitingRelease = StalledWork.None,
            FailedDispatches = new StalledWork { Count = 2 },
            SourcesNotPolled = StalledWork.None,
        });

        Assert.Equal(OperatorAlertSeverity.Warning, alert.Severity);
        Assert.Equal("pipeline-stalled", alert.DedupeKey);
        Assert.Equal(2, alert.Fields.Count);
        Assert.Contains("24 adet", alert.Fields[0].Value, StringComparison.Ordinal);
        Assert.Contains("2026-08-18", alert.Fields[0].Value, StringComparison.Ordinal);
        Assert.Contains("G3-TR-A", alert.Fields[0].Value, StringComparison.Ordinal);

        // The failed dispatches carry no oldest entry, and the line still has to render.
        Assert.Contains("bilinmiyor", alert.Fields[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingStageIsKeyedByStageSoEachOneIsHeardOnce()
    {
        Assert.NotEqual(
            WorkerAlerts.StageFailed("fark hesaplama", new IOException("x")).DedupeKey,
            WorkerAlerts.StageFailed("revizyon yayınlama", new IOException("x")).DedupeKey);
    }

    private static ScheduleDiff Diff(ScheduleDiffChange change, int count) => ScheduleDiff.Create(
        Guid.CreateVersion7(),
        Source,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        [.. Enumerable.Range(0, count).Select(_ => new ScheduleDiffEntry
        {
            Change = change,
            Match = ScheduleDiffMatch.None,
            PreviousRecordId = change is ScheduleDiffChange.Created ? null : Guid.CreateVersion7(),
            CurrentRecordId = change is ScheduleDiffChange.Deleted ? null : Guid.CreateVersion7(),
        })],
        new ScheduleDiffSafetyThresholds(),
        Now);
}
