namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>Selects the next worker delay from the Istanbul-time policy in ADR-026.</summary>
public sealed class AdaptivePollingIntervalPolicy
{
    private readonly AdaptivePollingOptions options;
    private readonly TimeZoneInfo timeZone;

    public AdaptivePollingIntervalPolicy(AdaptivePollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
    }

    public TimeSpan GetInterval(DateTimeOffset utcNow)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return options.WeekendInterval;
        }

        TimeOnly time = TimeOnly.FromDateTime(local.DateTime);
        if (time >= options.DaytimeStart && time < options.LateAfternoonStart)
        {
            return options.DaytimeInterval;
        }

        if (time >= options.LateAfternoonStart && time < options.NightStart)
        {
            return options.LateAfternoonInterval;
        }

        return options.NightInterval;
    }
}

public sealed record AdaptivePollingOptions
{
    public string TimeZoneId { get; init; } = "Europe/Istanbul";

    public TimeOnly DaytimeStart { get; init; } = new(7, 0);

    public TimeOnly LateAfternoonStart { get; init; } = new(16, 0);

    public TimeOnly NightStart { get; init; } = new(21, 0);

    public TimeSpan DaytimeInterval { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan LateAfternoonInterval { get; init; } = TimeSpan.FromMinutes(25);

    public TimeSpan NightInterval { get; init; } = TimeSpan.FromMinutes(45);

    public TimeSpan WeekendInterval { get; init; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TimeZoneId);

        if (DaytimeStart >= LateAfternoonStart || LateAfternoonStart >= NightStart)
        {
            throw new InvalidOperationException(
                "Polling windows must be ordered daytime, late afternoon, then night.");
        }

        foreach (TimeSpan interval in new[]
        {
            DaytimeInterval,
            LateAfternoonInterval,
            NightInterval,
            WeekendInterval,
        })
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Polling intervals must be positive.");
            }
        }
    }
}
