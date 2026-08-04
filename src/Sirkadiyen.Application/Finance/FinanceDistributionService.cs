namespace Sirkadiyen.Application.Finance;

/// <summary>Thin orchestration over <see cref="IFinanceDistributionStore"/>, owning the clock.</summary>
public sealed class FinanceDistributionService(IFinanceDistributionStore store, TimeProvider timeProvider)
{
    public Task<FinanceDistributionPlan> PreviewAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        CancellationToken cancellationToken) =>
        store.PreviewAsync(periodStartOn, periodEndOn, sourceAccountId, cancellationToken);

    public Task<FinanceDistributionResult> ExecuteAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        Guid confirmationToken,
        string planHash,
        string expectedConfirmationPhrase,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.ExecuteAsync(
            periodStartOn,
            periodEndOn,
            sourceAccountId,
            confirmationToken,
            planHash,
            expectedConfirmationPhrase,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceDistributionResult> ReverseAsync(
        Guid distributionId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.ReverseAsync(
            distributionId,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
