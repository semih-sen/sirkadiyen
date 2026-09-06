namespace Sirkadiyen.Domain.Meals;

/// <summary>
/// One subscriber's copy of one menu-day, and the durable ledger the delivery worker resumes from
/// (ADR-150).
/// </summary>
/// <remarks>
/// This is the announcement delivery ledger (ADR-107) applied to a standing subscription rather than
/// a frozen campaign. A row exists per (subscriber, date, meal); the deterministic event id makes
/// every write idempotent, and the applied content version is what a corrected menu is patched up
/// to. It is this module's own table, never the schedule's calendar-event mapping, because a menu is
/// not a lesson and must not appear in the ledger that decides what published truth owes a student.
/// </remarks>
public sealed class MealCalendarDelivery
{
    public const int MaximumGoogleCalendarIdLength = 1024;

    public const int MaximumGoogleEventIdLength = 1024;

    public const int MaximumFailureReasonLength = 2000;

    private MealCalendarDelivery()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    public DateOnly LocalDate { get; private init; }

    public MealCategory Category { get; private init; }

    /// <summary>The subscriber's dedicated calendar, as it stood when the row was created.</summary>
    public string? GoogleCalendarId { get; private set; }

    /// <summary>The deterministic event id this copy is written under.</summary>
    public string? GoogleEventId { get; private set; }

    public MealDeliveryState State { get; private set; }

    /// <summary>Which content version the subscriber currently holds, if any.</summary>
    public int? AppliedContentVersion { get; private set; }

    public MealDeliveryExclusionReason? SkipReason { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>A copy the delivery pass will write on its next run.</summary>
    public static MealCalendarDelivery Pending(
        Guid userId,
        DateOnly localDate,
        MealCategory category,
        string googleCalendarId,
        DateTimeOffset atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = Required(userId, nameof(userId)),
            LocalDate = localDate,
            Category = category,
            GoogleCalendarId = Bounded(
                googleCalendarId,
                MaximumGoogleCalendarIdLength,
                nameof(googleCalendarId)),
            State = MealDeliveryState.Pending,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };

    public void MarkWritten(string googleEventId, int contentVersion, DateTimeOffset atUtc)
    {
        GoogleEventId = Bounded(googleEventId, MaximumGoogleEventIdLength, nameof(googleEventId));
        AppliedContentVersion = contentVersion;
        State = MealDeliveryState.Written;
        SkipReason = null;
        FailureReason = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// The subscriber cannot receive it — most often a revoked grant. Their copy is skipped with the
    /// reason; nothing already on their calendar is touched.
    /// </summary>
    public void MarkSkipped(MealDeliveryExclusionReason reason, DateTimeOffset atUtc)
    {
        State = MealDeliveryState.Skipped;
        SkipReason = reason;
        UpdatedAtUtc = atUtc;
    }

    public void MarkFailed(string reason, DateTimeOffset atUtc)
    {
        State = MealDeliveryState.Failed;
        FailureReason = Optional(reason, MaximumFailureReasonLength);
        UpdatedAtUtc = atUtc;
    }

    /// <summary>The written copy was removed — the day was withdrawn or the menu turned off.</summary>
    public void MarkRemoved(DateTimeOffset atUtc)
    {
        State = MealDeliveryState.Removed;
        AppliedContentVersion = null;
        UpdatedAtUtc = atUtc;
    }

    private static Guid Required(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A required identifier was empty.", parameterName);
        }

        return value;
    }

    private static string Bounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maximumLength)];
}
