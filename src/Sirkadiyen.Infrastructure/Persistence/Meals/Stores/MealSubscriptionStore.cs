using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Stores;

/// <summary>Persists each user's cafeteria-menu preference in PostgreSQL (ADR-150).</summary>
public sealed class MealSubscriptionStore(SirkadiyenDbContext dbContext) : IMealSubscriptionStore
{
    private const string UniqueViolation = "23505";

    public async Task<bool?> GetEnabledAsync(Guid userId, CancellationToken cancellationToken)
    {
        MealMenuSubscription? subscription = await dbContext.MealMenuSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        return subscription?.IsEnabled;
    }

    public Task<bool> SetAsync(
        Guid userId,
        bool isEnabled,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            MealMenuSubscription? subscription = await dbContext.MealMenuSubscriptions
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

            bool changed;
            if (subscription is null)
            {
                dbContext.MealMenuSubscriptions.Add(
                    MealMenuSubscription.Create(userId, isEnabled, atUtc));
                // A first choice of "off" restates the opt-in default, so it queues no work even
                // though a row is now recorded.
                changed = isEnabled;
            }
            else
            {
                changed = subscription.Set(isEnabled, atUtc);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                // Another request created the row first. Re-read and apply the choice to it.
                dbContext.ChangeTracker.Clear();
                MealMenuSubscription existing = await dbContext.MealMenuSubscriptions
                    .SingleAsync(candidate => candidate.UserId == userId, cancellationToken);
                changed = existing.Set(isEnabled, atUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return changed;
        });
}
