using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.Scheduling.Publication;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class OperationalFreezeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTransitionRecordsWhoWhyWhenAndCorrelation()
    {
        OperationalFreezeControl control = OperationalFreezeControl.CreateInitial();

        OperationalFreezeAudit audit = control.Change(
            isFrozen: true,
            " semih ",
            " unexpected source structure ",
            " incident-42 ",
            Now);

        Assert.True(control.IsFrozen);
        Assert.Equal("semih", control.ChangedBy);
        Assert.Equal("unexpected source structure", control.Reason);
        Assert.Equal("incident-42", control.CorrelationId);
        Assert.Equal(Now, control.ChangedAtUtc);
        Assert.True(audit.IsFrozen);
        Assert.Equal(control.Id, audit.OperationalFreezeControlId);
        Assert.Equal("semih", audit.ChangedBy);
    }

    [Fact]
    public void RepeatingTheCurrentStateCannotInventAnAuditTransition()
    {
        OperationalFreezeControl control = OperationalFreezeControl.CreateInitial();

        Assert.Throws<InvalidOperationException>(() => control.Change(
            isFrozen: false,
            "semih",
            "already healthy",
            "incident-42",
            Now));
    }

    [Fact]
    public async Task FrozenPublicationReturnsAnExplicitOutcomeWithoutCallingTheStore()
    {
        FakePublicationStore publicationStore = new();
        ScheduleRevisionPublicationService service = new(
            publicationStore,
            new FixedFreezeStore(isFrozen: true),
            new FixedTimeProvider(Now));
        Guid revisionId = Guid.CreateVersion7();

        RevisionPublicationResult result = await service.PublishAsync(
            revisionId,
            CancellationToken.None);

        Assert.Equal(RevisionPublicationOutcome.Frozen, result.Outcome);
        Assert.Equal(revisionId, result.RevisionId);
        Assert.Equal(0, publicationStore.PublishCallCount);
    }

    [Fact]
    public async Task AFreezeReadFailureFailsPublicationClosed()
    {
        FakePublicationStore publicationStore = new();
        ScheduleRevisionPublicationService service = new(
            publicationStore,
            new FixedFreezeStore(new InvalidOperationException("database unavailable")),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(
            Guid.CreateVersion7(),
            CancellationToken.None));

        Assert.Equal(0, publicationStore.PublishCallCount);
    }

    private sealed class FakePublicationStore : IScheduleRevisionPublicationStore
    {
        public int PublishCallCount { get; private set; }

        public Task<RevisionPublicationResult> PublishAsync(
            Guid revisionId,
            DateTimeOffset publishedAtUtc,
            CancellationToken cancellationToken)
        {
            PublishCallCount++;
            return Task.FromResult(new RevisionPublicationResult
            {
                RevisionId = revisionId,
                Outcome = RevisionPublicationOutcome.Published,
            });
        }

        public Task<IReadOnlyList<Guid>> ListPublishableAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<RevisionApprovalResult> ApproveAsync(
            Guid revisionId,
            string approvedBy,
            string approvalReason,
            DateTimeOffset approvedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RevisionRejectionResult> RejectAsync(
            Guid revisionId,
            string rejectedBy,
            string rejectionReason,
            DateTimeOffset rejectedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedFreezeStore : IOperationalFreezeStore
    {
        private readonly bool isFrozen;
        private readonly Exception? exception;

        public FixedFreezeStore(bool isFrozen) => this.isFrozen = isFrozen;

        public FixedFreezeStore(Exception exception) => this.exception = exception;

        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            exception is null
                ? Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = isFrozen })
                : Task.FromException<OperationalFreezeSnapshot>(exception);

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool requestedState,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
