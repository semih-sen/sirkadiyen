using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Identity.Stores;

/// <summary>PostgreSQL-backed administrative role change (ADR-119).</summary>
public sealed class UserRoleStore(SirkadiyenDbContext dbContext) : IUserRoleStore
{
    public Task<ChangeUserRoleResult> ChangeRoleAsync(
        Guid userId,
        UserRole newRole,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            User? user = await dbContext.Users
                .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChangeUserRoleResult { Outcome = ChangeUserRoleOutcome.UserNotFound };
            }

            UserRole previousRole = user.Role;
            bool changed = user.ChangeRole(newRole, atUtc);
            if (!changed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChangeUserRoleResult
                {
                    Outcome = ChangeUserRoleOutcome.Unchanged,
                    PreviousRole = previousRole,
                    NewRole = newRole,
                };
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ChangeUserRoleResult
            {
                Outcome = ChangeUserRoleOutcome.Changed,
                PreviousRole = previousRole,
                NewRole = newRole,
            };
        });
}
