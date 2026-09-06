using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// The calendar label a meal event carries: its stable key, its label name, and its colour
/// (ADR-150).
/// </summary>
/// <remarks>
/// Server-owned rather than free-form, for the same reason department and announcement colours are
/// (ADR-072, ADR-107): the label is calendar-scoped and shared by every event under the key, so a
/// free-form colour would let one event silently recolour another. The palette deliberately avoids
/// both the schedule categories' and the announcement colours, so a menu never looks like a lesson
/// or a message from Sirkadiyen.
/// </remarks>
public static class MealCategoryCatalog
{
    private static readonly IReadOnlyList<MealCategoryPresentation> All =
    [
        new(MealCategory.Breakfast, "meal:breakfast", "Kahvaltı", "#F9A825"),
        new(MealCategory.Lunch, "meal:lunch", "Öğle Yemeği", "#2E7D32"),
        new(MealCategory.Dinner, "meal:dinner", "Akşam Yemeği", "#6A1B9A"),
    ];

    public static MealCategoryPresentation Get(MealCategory category) =>
        All.FirstOrDefault(presentation => presentation.Category == category)
        ?? throw new ArgumentOutOfRangeException(
            nameof(category),
            category,
            "Unknown meal category.");
}

/// <summary>One meal category's calendar presentation.</summary>
public sealed record MealCategoryPresentation(
    MealCategory Category,
    string Key,
    string Name,
    string BackgroundColor);
