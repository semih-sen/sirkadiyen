using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.ScheduleSources;

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

    public async Task<OperationalFreezeSnapshot> GetScopedAsync(
        OperationalFreezeScope scope,
        CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        ScopedOperationalFreezeControl? control = await dbContext.ScopedOperationalFreezeControls
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClassYear == scope.ClassYear
                    && candidate.ProgramLanguage == scope.ProgramLanguage,
                cancellationToken);

        return control is null
            ? new OperationalFreezeSnapshot { IsFrozen = false, Scope = scope }
            : Snapshot(control);
    }

    public async Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedAsync(
        CancellationToken cancellationToken) =>
        await dbContext.ScopedOperationalFreezeControls
            .AsNoTracking()
            .OrderBy(control => control.ClassYear)
            .ThenBy(control => control.ProgramLanguage)
            .Select(control => new OperationalFreezeSnapshot
            {
                IsFrozen = control.IsFrozen,
                Scope = new OperationalFreezeScope
                {
                    ClassYear = control.ClassYear,
                    ProgramLanguage = control.ProgramLanguage,
                },
                ChangedBy = control.ChangedBy,
                Reason = control.Reason,
                CorrelationId = control.CorrelationId,
                ChangedAtUtc = control.ChangedAtUtc,
            })
            .ToListAsync(cancellationToken);

    public Task<OperationalFreezeChangeResult> SetScopedAsync(
        OperationalFreezeScope scope,
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // A scope row is created on its first transition. Serialize that rare creation
            // so two operators cannot both miss the row and race the unique scope index.
            await dbContext.Database.ExecuteSqlRawAsync(
                "LOCK TABLE sirkadiyen.scoped_operational_freeze_controls "
                    + "IN SHARE ROW EXCLUSIVE MODE",
                cancellationToken);

            ScopedOperationalFreezeControl? control = await dbContext
                .ScopedOperationalFreezeControls
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.scoped_operational_freeze_controls
                    WHERE "ClassYear" = {scope.ClassYear}
                      AND "ProgramLanguage" = {scope.ProgramLanguage.ToString()}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (control is null)
            {
                control = ScopedOperationalFreezeControl.Create(
                    scope.ClassYear,
                    scope.ProgramLanguage);
                dbContext.ScopedOperationalFreezeControls.Add(control);
            }

            if (control.IsFrozen == isFrozen)
            {
                await transaction.CommitAsync(cancellationToken);
                return new OperationalFreezeChangeResult
                {
                    Outcome = OperationalFreezeChangeOutcome.AlreadyInRequestedState,
                    State = Snapshot(control),
                };
            }

            ScopedOperationalFreezeAudit audit = control.Change(
                isFrozen,
                changedBy,
                reason,
                correlationId,
                changedAtUtc);
            dbContext.ScopedOperationalFreezeAudits.Add(audit);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new OperationalFreezeChangeResult
            {
                Outcome = OperationalFreezeChangeOutcome.Changed,
                State = Snapshot(control),
            };
        });
    }

    private static OperationalFreezeSnapshot Snapshot(OperationalFreezeControl control) => new()
    {
        IsFrozen = control.IsFrozen,
        ChangedBy = control.ChangedBy,
        Reason = control.Reason,
        CorrelationId = control.CorrelationId,
        ChangedAtUtc = control.ChangedAtUtc,
    };

    private static OperationalFreezeSnapshot Snapshot(ScopedOperationalFreezeControl control) =>
        new()
        {
            IsFrozen = control.IsFrozen,
            Scope = new OperationalFreezeScope
            {
                ClassYear = control.ClassYear,
                ProgramLanguage = control.ProgramLanguage,
            },
            ChangedBy = control.ChangedBy,
            Reason = control.Reason,
            CorrelationId = control.CorrelationId,
            ChangedAtUtc = control.ChangedAtUtc,
        };

    private static void ValidateScope(OperationalFreezeScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(scope.ClassYear, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scope.ClassYear, 6);
        if (!Enum.IsDefined(scope.ProgramLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }

    private static InvalidOperationException MissingControl() => new(
        "The authoritative global operational freeze row is missing. "
        + "Pipeline mutation is stopped until the database is repaired.");
}
