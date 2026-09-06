namespace Sirkadiyen.Domain.Meals;

/// <summary>
/// One user's standing choice to have the cafeteria menu on their calendar (ADR-150).
/// </summary>
/// <remarks>
/// Deliberately meal-specific rather than a general "calendar preferences" aggregate: it is the only
/// such preference today, and inventing a generic one now would be a speculative abstraction
/// (AI_GUIDELINE §4). It can be generalized when a second preference actually exists.
/// <para>
/// The preference is live: enabling it is a request to backfill the whole currently-known window,
/// and disabling it is a request to remove the written copies. Neither happens here — the aggregate
/// only records the choice and when it changed; the delivery pass reconciles the calendar to it.
/// </para>
/// </remarks>
public sealed class MealMenuSubscription
{
    private MealMenuSubscription()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    /// <summary>When the choice last changed, so the delivery pass can find newly-toggled users.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public static MealMenuSubscription Create(Guid userId, bool isEnabled, DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A subscription owner is required.", nameof(userId));
        }

        return new MealMenuSubscription
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IsEnabled = isEnabled,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>
    /// Sets the choice. Returns whether it actually changed, so a caller does not re-queue delivery
    /// work for a toggle that restated the current state.
    /// </summary>
    public bool Set(bool isEnabled, DateTimeOffset atUtc)
    {
        if (IsEnabled == isEnabled)
        {
            return false;
        }

        IsEnabled = isEnabled;
        UpdatedAtUtc = atUtc;
        return true;
    }
}
