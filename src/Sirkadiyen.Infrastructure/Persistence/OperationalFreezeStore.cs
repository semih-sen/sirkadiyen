using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed global operational freeze with an atomic audit trail.
/// </summary>
public sealed class OperationalFreezeStore(SirkadiyenDbContext dbContext)
    : IOperationalFreezeStore
{
    public async Task<OperationalFreezeSnapshot> GetAsync(
        CancellationToken cancellationToken)
    {
        OperationalFreezeControl control = await dbContext.Set<OperationalFreezeControl>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == OperationalFreezeControl.SingletonId,
                cancellationToken)
            ?? throw MissingControl();

        return Snapshot(control);
    }

    public Task<OperationalFreezeChangeResult> SetAsync(
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // xmin backs the concurrency token and is not included in SELECT *.
            // The row lock serializes competing operator transitions so their
            // audit order and the current state cannot disagree.
            OperationalFreezeControl control = await dbContext
                .Set<OperationalFreezeControl>()
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.operational_freeze_control
                    WHERE "Id" = {OperationalFreezeControl.SingletonId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw MissingControl();

            if (control.IsFrozen == isFrozen)
            {
                await transaction.CommitAsync(cancellationToken);
                return new OperationalFreezeChangeResult
                {
                    Outcome = OperationalFreezeChangeOutcome.AlreadyInRequestedState,
                    State = Snapshot(control),
                };
            }

            OperationalFreezeAudit audit = control.Change(
                isFrozen,
                changedBy,
                reason,
                correlationId,
                changedAtUtc);
            dbContext.Set<OperationalFreezeAudit>().Add(audit);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new OperationalFreezeChangeResult
            {
                Outcome = OperationalFreezeChangeOutcome.Changed,
                State = Snapshot(control),
            };
        });

    private static OperationalFreezeSnapshot Snapshot(OperationalFreezeControl control) => new()
    {
        IsFrozen = control.IsFrozen,
        ChangedBy = control.ChangedBy,
        Reason = control.Reason,
        CorrelationId = control.CorrelationId,
        ChangedAtUtc = control.ChangedAtUtc,
    };

    private static InvalidOperationException MissingControl() => new(
        "The authoritative global operational freeze row is missing. "
        + "Pipeline mutation is stopped until the database is repaired.");
}
