using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Stores;

/// <summary>Persists acquired menu-days in PostgreSQL (ADR-150).</summary>
public sealed class MealMenuStore(SirkadiyenDbContext dbContext) : IMealMenuStore
{
    public async Task<IReadOnlyList<MealMenuDay>> ListForWindowAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken)
    {
        // Tracked on purpose: the acquisition service mutates these entities in place, and
        // PersistAsync — the same scoped context — saves those mutations without re-attaching.
        return await dbContext.MealMenuDays
            .Where(day => day.Category == category
                && day.LocalDate >= fromInclusive
                && day.LocalDate <= toInclusive)
            .OrderBy(day => day.LocalDate)
            .ToListAsync(cancellationToken);
    }

    public async Task PersistAsync(
        IReadOnlyCollection<MealMenuDay> newDays,
        IReadOnlyCollection<MealMenuDay> mutatedDays,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newDays);
        ArgumentNullException.ThrowIfNull(mutatedDays);

        // New rows are added; the mutated ones are already tracked from ListForWindowAsync, so a
        // single SaveChanges commits both in one transaction. _ = mutatedDays keeps the contract
        // explicit even though tracking is what persists them.
        _ = mutatedDays;
        if (newDays.Count > 0)
        {
            dbContext.MealMenuDays.AddRange(newDays);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Another worker instance polled the same window at the same 12h mark and inserted this
            // date first. Acquisition is not fenced (it is a poll, not a calendar write), so losing
            // the race is expected and harmless: the winning row already holds the same menu, and
            // the next poll reconciles anything left. The batch is abandoned rather than reconciled
            // row-by-row, because it is cheaper to re-fetch next cycle than to merge here.
            dbContext.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Same race on an existing row's version token. Same resolution.
            dbContext.ChangeTracker.Clear();
        }
    }

    private const string UniqueViolation = "23505";
}
