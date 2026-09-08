using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Retirement of a source the catalog no longer declares (ADR-155).
/// </summary>
/// <remarks>
/// These run without a database because the reconciliation that applies them runs on every worker
/// start, against whatever the server already has: what it must never do is rewrite the date of a
/// retirement that already happened, or leave a permanent failure standing on a row nothing will
/// ever poll again.
/// </remarks>
public sealed class ScheduleSourceRetirementTests
{
    private static readonly DateTimeOffset RetiredAt =
        new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetiringStopsPollingAndClearsAFailureThatCanNeverResolve()
    {
        ScheduleSource source = Source();
        source.RecordPolled(RetiredAt.AddDays(-30), changed: true);
        source.RecordPollFailure(RetiredAt.AddDays(-29), "The Drive file is in the trash.");

        Assert.True(source.Retire(RetiredAt));

        Assert.Equal(RetiredAt, source.RetiredAtUtc);
        Assert.False(source.IsPollingEnabled);

        // Nothing will poll it again, so the failure can never be resolved; left standing it says
        // a source that no longer exists is broken.
        Assert.Null(source.LastPollFailureAtUtc);
        Assert.Null(source.LastPollFailureReason);

        // What it did while it was configured is evidence and stays.
        Assert.Equal(RetiredAt.AddDays(-30), source.LastPolledAtUtc);
    }

    [Fact]
    public void RetiringAgainDoesNotRewriteWhenItHappened()
    {
        ScheduleSource source = Source();
        source.Retire(RetiredAt);

        Assert.False(source.Retire(RetiredAt.AddDays(5)));
        Assert.Equal(RetiredAt, source.RetiredAtUtc);
    }

    [Fact]
    public void ReinstatingPollsTheSourceAgainAndOnlyAppliesToARetiredOne()
    {
        ScheduleSource source = Source();
        source.Retire(RetiredAt);

        Assert.True(source.Reinstate());
        Assert.Null(source.RetiredAtUtc);
        Assert.True(source.IsPollingEnabled);

        // A source an operator disabled deliberately is not retired, so reinstatement never
        // overrides that decision.
        source.SetPollingEnabled(false);
        Assert.False(source.Reinstate());
        Assert.False(source.IsPollingEnabled);
    }

    private static ScheduleSource Source() => new(
        SourceId.Parse("G2-VERTICAL-SPRING"),
        "Dönem 2 dikey koridor beceri uygulamaları bahar",
        ScheduleSourceTransport.GoogleDriveFile,
        ScheduleDocumentFormat.Docx,
        "https://drive.google.com/file/d/example/view",
        "grade2_vertical_corridor_v1",
        "1.2.0",
        "2025-2026",
        2,
        ProgramLanguage.Turkish,
        "Europe/Istanbul",
        "example");
}
