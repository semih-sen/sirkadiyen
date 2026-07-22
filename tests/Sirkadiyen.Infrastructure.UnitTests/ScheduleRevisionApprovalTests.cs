using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The approval rules that hold regardless of storage: who may be approved, what
/// an approval must state, and what it is not allowed to skip.
/// </summary>
public sealed class ScheduleRevisionApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovalReleasesAQuarantinedRevisionAndRecordsWhoDidIt()
    {
        ScheduleRevision revision = Quarantined();

        revision.Approve("semih", "The drop is the exam period.", Now);

        Assert.Equal(RevisionState.Validated, revision.State);
        Assert.Equal("semih", revision.ApprovedBy);
        Assert.Equal("The drop is the exam period.", revision.ApprovalReason);
        Assert.Equal(Now, revision.ApprovedAtUtc);
    }

    [Fact]
    public void ApprovalDoesNotPublish()
    {
        // Approval only clears the revision for publication. Publishing stays a
        // separate transaction, so an approved revision goes live through exactly
        // the same path as one that was never held.
        ScheduleRevision revision = Quarantined();

        revision.Approve("semih", "Reviewed.", Now);

        Assert.NotEqual(RevisionState.Published, revision.State);
        Assert.Null(revision.PublishedAtUtc);
    }

    [Fact]
    public void ApprovalKeepsTheReasonTheRevisionWasHeld()
    {
        ScheduleRevision revision = Quarantined();

        revision.Approve("semih", "Reviewed.", Now);

        Assert.Contains("MassDeletion", revision.StateReason!, StringComparison.Ordinal);
        Assert.Contains("semih", revision.StateReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RevisionState.Parsed)]
    [InlineData(RevisionState.Validated)]
    [InlineData(RevisionState.Published)]
    [InlineData(RevisionState.Rejected)]
    public void OnlyARevisionAwaitingReviewCanBeApproved(RevisionState state)
    {
        ScheduleRevision revision = InState(state);

        Assert.Throws<InvalidOperationException>(
            () => revision.Approve("semih", "Reviewed.", Now));
    }

    [Theory]
    [InlineData("", "Reviewed.")]
    [InlineData("   ", "Reviewed.")]
    [InlineData("semih", "")]
    [InlineData("semih", "   ")]
    public void AnApprovalMustSayWhoAndWhy(string approvedBy, string reason)
    {
        // The audit trail is the entire justification for this method existing.
        ScheduleRevision revision = Quarantined();

        Assert.Throws<ArgumentException>(() => revision.Approve(approvedBy, reason, Now));
    }

    [Fact]
    public void AnOverLongApprovalIsRefusedRatherThanTruncated()
    {
        ScheduleRevision revision = Quarantined();

        Assert.Throws<ArgumentOutOfRangeException>(() => revision.Approve(
            new string('x', ScheduleRevision.MaximumApprovedByLength + 1),
            "Reviewed.",
            Now));
    }

    [Fact]
    public void ARejectedRevisionStaysTerminal()
    {
        ScheduleRevision revision = InState(RevisionState.Rejected);

        Assert.Throws<InvalidOperationException>(
            () => revision.TransitionTo(RevisionState.Validated, Now));
        Assert.Throws<InvalidOperationException>(
            () => revision.TransitionTo(RevisionState.Published, Now));
    }

    private static ScheduleRevision Quarantined() => InState(RevisionState.ReviewRequired);

    private static ScheduleRevision InState(RevisionState state)
    {
        ScheduleRevision revision = new(
            Guid.CreateVersion7(),
            SourceId.Parse("G1-TR-PRACTICE"),
            Guid.CreateVersion7(),
            Now);

        if (state is RevisionState.Parsed)
        {
            return revision;
        }

        if (state is RevisionState.Rejected)
        {
            revision.TransitionTo(RevisionState.Rejected, Now, "Empty revision.");
            return revision;
        }

        revision.TransitionTo(RevisionState.Validating, Now);
        revision.TransitionTo(
            state is RevisionState.ReviewRequired
                ? RevisionState.ReviewRequired
                : RevisionState.Validated,
            Now,
            "Held for review: MassDeletion");

        if (state is RevisionState.Published)
        {
            revision.TransitionTo(RevisionState.Published, Now);
        }

        return revision;
    }
}
