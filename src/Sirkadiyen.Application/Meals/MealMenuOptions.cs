using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// How the cafeteria menu is acquired, presented on the calendar, and delivered (ADR-150).
/// </summary>
public sealed record MealMenuOptions
{
    /// <summary>
    /// Whether the meal pipeline runs at all. The services and tasks are always registered so the
    /// object graph is complete, but a deployment (or a dev machine) that leaves this off makes no
    /// external API calls and writes no menu events.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the acquisition pass re-fetches the rolling window.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How often the delivery pass reconciles calendars. Short enough that a subscription toggle or
    /// a corrected menu reaches calendars promptly, since the preference is live (this session's
    /// decision).
    /// </summary>
    public TimeSpan DeliveryInterval { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The meal acquired and written today. Only lunch is enabled.</summary>
    public MealCategory Category { get; init; } = MealCategory.Lunch;

    /// <summary>
    /// How many days ahead of today the rolling acquisition window reaches. The window is what
    /// discovers a newly published month without any special-case logic: as days slide into it and
    /// the faculty publishes them, they stop coming back empty and get created.
    /// </summary>
    public int WindowDays { get; init; } = 35;

    /// <summary>
    /// Consecutive empty answers before a previously published day is treated as withdrawn. Never
    /// one: a single empty answer is far more likely a transient failure or a closed day than a
    /// genuine cancellation (AI_GUIDELINE §13).
    /// </summary>
    public int WithdrawalMissThreshold { get; init; } = 3;

    /// <summary>The IANA zone the menu times are interpreted in.</summary>
    public string TimeZoneId { get; init; } = "Europe/Istanbul";

    public TimeOnly StartLocalTime { get; init; } = new(12, 30);

    public TimeOnly EndLocalTime { get; init; } = new(13, 0);

    /// <summary>The location written on the event, e.g. the cafeteria name; null to leave it off.</summary>
    public string? Location { get; init; }

    /// <summary>Minutes before the start to remind, or null to leave the calendar default.</summary>
    public int? ReminderMinutesBefore { get; init; }

    /// <summary>How many menu events one delivery cycle writes or patches before yielding.</summary>
    public int MaxWritesPerCycle { get; init; } = 500;

    /// <summary>How many menu events one delivery cycle removes before yielding.</summary>
    public int MaxRemovalsPerCycle { get; init; } = 500;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(WindowDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(WithdrawalMissThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWritesPerCycle, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRemovalsPerCycle, 1);
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The meal poll interval must be positive.");
        }

        if (DeliveryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The meal delivery interval must be positive.");
        }

        if (EndLocalTime <= StartLocalTime)
        {
            throw new InvalidOperationException("The meal event must end after it starts.");
        }

        if (ReminderMinutesBefore is { } reminder)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(reminder, nameof(ReminderMinutesBefore));
        }
    }

    public MealEventPresentation Presentation() => new()
    {
        StartLocalTime = StartLocalTime,
        EndLocalTime = EndLocalTime,
        TimeZoneId = TimeZoneId,
        Location = Location,
        ReminderMinutesBefore = ReminderMinutesBefore,
    };
}
