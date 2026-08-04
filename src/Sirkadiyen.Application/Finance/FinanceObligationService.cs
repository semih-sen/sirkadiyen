using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

/// <summary>Thin orchestration over <see cref="IFinanceObligationStore"/>, owning the clock.</summary>
public sealed class FinanceObligationService(IFinanceObligationStore store, TimeProvider timeProvider)
{
    public Task<FinanceObligationMutationResult> CreateAsync(
        FinanceObligationDirection direction,
        FinanceCategory category,
        string counterpartyName,
        string? description,
        decimal amount,
        DateOnly issuedOn,
        DateOnly? dueOn,
        Guid actorUserId,
        string actorEmail,
        CancellationToken cancellationToken) =>
        store.CreateAsync(
            direction,
            category,
            counterpartyName,
            description,
            amount,
            issuedOn,
            dueOn,
            actorUserId,
            actorEmail,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceObligationMutationResult> SettleAsync(
        Guid obligationId,
        Guid accountId,
        decimal amount,
        DateOnly settledOn,
        string? reference,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.SettleAsync(
            obligationId,
            accountId,
            amount,
            settledOn,
            reference,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceObligationMutationResult> CancelSettlementAsync(
        Guid obligationId,
        Guid settlementId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.CancelSettlementAsync(
            obligationId,
            settlementId,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceObligationMutationResult> WriteOffAsync(
        Guid obligationId,
        string reason,
        DateOnly writtenOffOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.WriteOffAsync(
            obligationId,
            reason,
            writtenOffOn,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceObligationMutationResult> CancelAsync(
        Guid obligationId,
        string reason,
        DateOnly cancelledOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.CancelAsync(
            obligationId,
            reason,
            cancelledOn,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
