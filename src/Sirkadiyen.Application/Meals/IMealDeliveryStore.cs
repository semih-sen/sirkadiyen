using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// The per-subscriber delivery ledger for cafeteria menus, and the set operations the convergence
/// pass reconciles it with (ADR-150).
/// </summary>
/// <remarks>
/// Unlike an announcement — a frozen campaign whose recipients are fixed at confirmation — a menu is
/// a standing subscription, so the ledger is reconciled continuously against two moving sets: the
/// enabled subscriptions and the published in-window days. The store expresses that reconciliation
/// in three set operations (materialize what is owed, list what to write, list what to remove); the
/// service does only the calendar I/O and the row marking.
/// </remarks>
public interface IMealDeliveryStore
{
    /// <summary>
    /// Brings the ledger in line with what is currently owed, in one transaction: a Pending row is
    /// created for every (enabled subscriber with a managed calendar) × (published in-window day)
    /// that has none, and a written row whose applied version is behind the day's current version is
    /// returned to Pending so the next pass patches it. Returns how many rows were newly created or
    /// reopened.
    /// </summary>
    Task<int> ReconcileOwedAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// The next copies to write, each with the menu content and the subscriber's eligibility as it
    /// stands now — a grant can die between passes, and writing to a dead one must be a skip the
    /// convergence survives.
    /// </summary>
    Task<IReadOnlyList<MealDeliveryWriteTarget>> ListWriteTargetsAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int limit,
        CancellationToken cancellationToken);

    Task MarkWrittenAsync(
        Guid deliveryId,
        string googleEventId,
        int contentVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task MarkSkippedAsync(
        Guid deliveryId,
        MealDeliveryExclusionReason reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid deliveryId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Copies that should no longer exist: their day was withdrawn, or their subscriber turned the
    /// menu off. A row that was never written (no event id) can be retired without a calendar call.
    /// </summary>
    Task<IReadOnlyList<MealDeliveryRemovalTarget>> ListRemovalTargetsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task MarkRemovedAsync(
        Guid deliveryId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

/// <summary>One copy to write, plus its menu content and the subscriber's current eligibility.</summary>
public sealed record MealDeliveryWriteTarget
{
    public required Guid DeliveryId { get; init; }

    public required Guid UserId { get; init; }

    public required DateOnly LocalDate { get; init; }

    public required MealCategory Category { get; init; }

    public required string MealText { get; init; }

    public required int ContentVersion { get; init; }

    /// <summary>Null when the subscriber is currently ineligible.</summary>
    public string? ProtectedRefreshToken { get; init; }

    public string? ManagedCalendarId { get; init; }

    /// <summary>Null when the copy may be written now; otherwise why it may not.</summary>
    public MealDeliveryExclusionReason? CurrentExclusion { get; init; }

    /// <summary>The event id already written under this row, if any — the insert-vs-patch signal.</summary>
    public string? GoogleEventId { get; init; }

    public int? AppliedContentVersion { get; init; }
}

/// <summary>One written copy to remove.</summary>
public sealed record MealDeliveryRemovalTarget
{
    public required Guid DeliveryId { get; init; }

    public required Guid UserId { get; init; }

    public string? GoogleEventId { get; init; }

    public string? ManagedCalendarId { get; init; }

    /// <summary>Null when the grant is gone; the event then cannot be reached to remove it.</summary>
    public string? ProtectedRefreshToken { get; init; }
}
