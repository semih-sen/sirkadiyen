using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Meals;

namespace Sirkadiyen.Infrastructure.Meals;

public static class MealMenuClientServiceCollectionExtensions
{
    /// <summary>Registers the typed HTTP client that reads the cafeteria menu API (ADR-150).</summary>
    public static IServiceCollection AddSirkadiyenMealMenuClient(
        this IServiceCollection services,
        Uri baseAddress,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("Meal menu base address must be absolute.", nameof(baseAddress));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        services.AddHttpClient<IMealMenuApiClient, SksMealMenuApiClient>(client =>
        {
            // The base address must end in a slash so the relative "meals-by-date" is appended
            // rather than replacing the last path segment.
            client.BaseAddress = baseAddress;
            client.Timeout = timeout;
        });
        return services;
    }
}
