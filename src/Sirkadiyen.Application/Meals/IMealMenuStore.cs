using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Persists acquired menu-days (ADR-150). This is only the menu itself; the per-subscriber delivery
/// ledger is <see cref="IMealDeliveryStore"/>.
/// </summary>
public interface IMealMenuStore
{
    /// <summary>Every stored day for a category within the inclusive date range, in date order.</summary>
    Task<IReadOnlyList<MealMenuDay>> ListForWindowAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds the newly seen days and saves the mutated ones in one transaction. Mutated days are the
    /// existing rows the acquisition pass touched — a content change, a republish, a recorded miss,
    /// or just a refreshed confirmation time.
    /// </summary>
    Task PersistAsync(
        IReadOnlyCollection<MealMenuDay> newDays,
        IReadOnlyCollection<MealMenuDay> mutatedDays,
        CancellationToken cancellationToken);
}
