using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Reads one day's menu from the faculty cafeteria API (ADR-150).
/// </summary>
/// <remarks>
/// The API answers a single date and category at a time and cannot distinguish "no menu today"
/// (weekend or holiday) from "not published yet" (a future month): both come back as
/// <see cref="MealMenuFetchResult.HasMenu"/> <c>false</c>. Interpreting that ambiguity is the
/// acquisition service's job, not this client's — the client only reports what the API said.
/// </remarks>
public interface IMealMenuApiClient
{
    Task<MealMenuFetchResult> FetchAsync(
        DateOnly date,
        MealCategory category,
        CancellationToken cancellationToken);
}

/// <summary>What the API returned for one date and category.</summary>
public sealed record MealMenuFetchResult
{
    /// <summary>Whether the API returned a menu for the date.</summary>
    public required bool HasMenu { get; init; }

    /// <summary>The raw, un-normalized menu text when <see cref="HasMenu"/>; otherwise null.</summary>
    public string? RawMealText { get; init; }

    public static MealMenuFetchResult Found(string rawMealText) =>
        new() { HasMenu = true, RawMealText = rawMealText };

    public static readonly MealMenuFetchResult NotFound = new() { HasMenu = false };
}
