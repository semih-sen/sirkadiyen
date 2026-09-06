using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Acquires the cafeteria menu for a rolling forward window and reconciles the stored menu-days
/// against it (ADR-150).
/// </summary>
/// <remarks>
/// This is the meal counterpart of source polling, and shares its core discipline — re-fetch,
/// hash, diff — but not its machinery: no snapshot, no parser, no revision. Two rules make it safe:
/// a transport failure for a date is not a miss (only an explicit empty answer is), so an outage
/// cannot mass-withdraw menus; and a withdrawal needs several consecutive misses, so a single blip
/// never deletes a written event.
/// </remarks>
public sealed class MealMenuAcquisitionService(
    IMealMenuApiClient apiClient,
    IMealMenuStore store,
    MealMenuOptions options,
    TimeProvider timeProvider)
{
    public async Task<MealAcquisitionResult> AcquireAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = TodayLocal(now);
        DateOnly windowEnd = today.AddDays(options.WindowDays);

        IReadOnlyList<MealMenuDay> existing =
            await store.ListForWindowAsync(options.Category, today, windowEnd, cancellationToken);
        Dictionary<DateOnly, MealMenuDay> byDate = existing.ToDictionary(day => day.LocalDate);

        List<MealMenuDay> newDays = [];
        List<MealMenuDay> mutatedDays = [];
        MealAcquisitionResult.Builder result = new();

        for (DateOnly date = today; date <= windowEnd; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MealMenuFetchResult fetched;
            try
            {
                fetched = await apiClient.FetchAsync(date, options.Category, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A transport or server failure is NOT an empty menu. Recording it as a miss would
                // let an outage withdraw a month of menus, so the date is simply left untouched.
                result.RecordApiError(date, exception.Message);
                continue;
            }

            string? normalized = fetched.HasMenu
                ? MealMenuText.Normalize(fetched.RawMealText!)
                : null;

            byDate.TryGetValue(date, out MealMenuDay? day);

            if (!string.IsNullOrEmpty(normalized))
            {
                string hash = MealMenuText.Hash(normalized);
                if (day is null)
                {
                    newDays.Add(MealMenuDay.CreatePublished(
                        date, options.Category, normalized, hash, now));
                    result.Published++;
                }
                else
                {
                    bool changed = day.ApplyObservedContent(normalized, hash, now);
                    mutatedDays.Add(day);
                    if (changed)
                    {
                        result.ContentChanged++;
                    }
                }

                result.Confirmed++;
            }
            else if (day is not null)
            {
                bool withdrawn = day.RecordMiss(options.WithdrawalMissThreshold, now);
                mutatedDays.Add(day);
                result.Missed++;
                if (withdrawn)
                {
                    result.Withdrawn++;
                }
            }
        }

        await store.PersistAsync(newDays, mutatedDays, cancellationToken);
        return result.Build(today, windowEnd);
    }

    private DateOnly TodayLocal(DateTimeOffset nowUtc)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
    }
}

/// <summary>What one acquisition pass observed, for logging and metrics (AI_GUIDELINE §19).</summary>
public sealed record MealAcquisitionResult
{
    public required DateOnly WindowStart { get; init; }

    public required DateOnly WindowEndInclusive { get; init; }

    /// <summary>Dates seen for the first time.</summary>
    public required int Published { get; init; }

    /// <summary>Dates whose menu text changed since the last poll.</summary>
    public required int ContentChanged { get; init; }

    /// <summary>Dates that returned a menu this pass (new or existing).</summary>
    public required int Confirmed { get; init; }

    /// <summary>Dates that returned no menu but were known before.</summary>
    public required int Missed { get; init; }

    /// <summary>Dates withdrawn this pass after reaching the miss threshold.</summary>
    public required int Withdrawn { get; init; }

    /// <summary>Dates skipped because the API call failed (never counted as a miss).</summary>
    public required int ApiErrors { get; init; }

    /// <summary>The first API failure message, when any, for the operator log.</summary>
    public string? FirstApiError { get; init; }

    internal sealed class Builder
    {
        public int Published { get; set; }

        public int ContentChanged { get; set; }

        public int Confirmed { get; set; }

        public int Missed { get; set; }

        public int Withdrawn { get; set; }

        private int _apiErrors;
        private string? _firstApiError;

        public void RecordApiError(DateOnly date, string message)
        {
            _apiErrors++;
            _firstApiError ??= $"{date:yyyy-MM-dd}: {message}";
        }

        public MealAcquisitionResult Build(DateOnly windowStart, DateOnly windowEnd) => new()
        {
            WindowStart = windowStart,
            WindowEndInclusive = windowEnd,
            Published = Published,
            ContentChanged = ContentChanged,
            Confirmed = Confirmed,
            Missed = Missed,
            Withdrawn = Withdrawn,
            ApiErrors = _apiErrors,
            FirstApiError = _firstApiError,
        };
    }
}
