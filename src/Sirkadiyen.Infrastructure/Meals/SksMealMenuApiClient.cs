using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Infrastructure.Meals;

/// <summary>
/// Reads the faculty cafeteria menu over HTTP from <c>sks.istanbul.edu.tr</c> (ADR-150).
/// </summary>
/// <remarks>
/// The client draws the one distinction the acquisition service depends on: a well-formed answer of
/// <c>success:false</c> is a definite "no menu for this date" (weekend, holiday, or not yet
/// published) and is returned as <see cref="MealMenuFetchResult.NotFound"/>; anything else — a
/// non-2xx status, an unreadable body — is a failure and is thrown, so the service can decline to
/// treat it as a miss and never let an outage withdraw a month of menus.
/// </remarks>
public sealed class SksMealMenuApiClient(HttpClient httpClient) : IMealMenuApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MealMenuFetchResult> FetchAsync(
        DateOnly date,
        MealCategory category,
        CancellationToken cancellationToken)
    {
        string requestUri =
            $"meals-by-date?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + $"&category={CategoryParameter(category)}";

        using HttpResponseMessage response = await httpClient.GetAsync(
            requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MealMenuApiException(response.StatusCode, SafeBody(body));
        }

        MealMenuApiResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MealMenuApiResponse>(body, JsonOptions)
                ?? throw new MealMenuApiException(response.StatusCode, "empty response document");
        }
        catch (JsonException exception)
        {
            throw new MealMenuApiException(response.StatusCode, exception.Message);
        }

        // success:false is the deliberate "no menu" answer; a success:true with no dishes is
        // treated the same, since there is nothing to write and nothing to hash.
        return parsed is { Success: true, Meal: { Length: > 0 } meal }
            ? MealMenuFetchResult.Found(meal)
            : MealMenuFetchResult.NotFound;
    }

    private static string CategoryParameter(MealCategory category) => category switch
    {
        MealCategory.Breakfast => "breakfast",
        MealCategory.Lunch => "lunch",
        MealCategory.Dinner => "dinner",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown meal category."),
    };

    private static string SafeBody(string body)
    {
        const int maximumLength = 500;
        string normalized = string.IsNullOrWhiteSpace(body) ? "no error document" : body.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed record MealMenuApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("meal")]
        public string? Meal { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}

/// <summary>A cafeteria API call that did not return a well-formed answer (ADR-150).</summary>
public sealed class MealMenuApiException(HttpStatusCode statusCode, string detail)
    : Exception($"The cafeteria menu API responded with {(int)statusCode}: {detail}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
