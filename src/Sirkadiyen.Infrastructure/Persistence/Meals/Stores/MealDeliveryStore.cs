using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Meals;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

namespace Sirkadiyen.Infrastructure.Persistence.Meals.Stores;

/// <summary>
/// The cafeteria-menu delivery ledger and its convergence queries, in PostgreSQL (ADR-150).
/// </summary>
/// <remarks>
/// Runs inside the shared Calendar fence (ADR-122), so it is a single writer: no two workers
/// materialize or mark the same rows at once, which is why the set operations here need no
/// unique-violation recovery of their own beyond the index that still guards the invariant.
/// </remarks>
public sealed class MealDeliveryStore(SirkadiyenDbContext dbContext) : IMealDeliveryStore
{
    public Task<int> ReconcileOwedAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            // Every (enabled subscriber with a managed calendar) × (published in-window day) that has
            // no ledger row yet. A stale written row is NOT created here — the write-targets query
            // finds it directly and patches it, so there is no need to reopen it to Pending.
            var missing = await (
                from subscription in dbContext.MealMenuSubscriptions.AsNoTracking()
                where subscription.IsEnabled
                join connection in dbContext.GoogleCalendarConnections.AsNoTracking()
                    on subscription.UserId equals connection.UserId
                where connection.ManagedCalendarId != null
                from day in dbContext.MealMenuDays.AsNoTracking()
                    .Where(candidate => candidate.Category == category
                        && candidate.Status == MealMenuDayStatus.Published
                        && candidate.LocalDate >= fromInclusive
                        && candidate.LocalDate <= toInclusive)
                where !dbContext.MealCalendarDeliveries.Any(existing =>
                    existing.UserId == subscription.UserId
                    && existing.LocalDate == day.LocalDate
                    && existing.Category == category)
                select new OwedRow
                {
                    UserId = subscription.UserId,
                    LocalDate = day.LocalDate,
                    ManagedCalendarId = connection.ManagedCalendarId!,
                }).ToListAsync(cancellationToken);

            foreach (OwedRow owed in missing)
            {
                dbContext.MealCalendarDeliveries.Add(MealCalendarDelivery.Pending(
                    owed.UserId, owed.LocalDate, category, owed.ManagedCalendarId, nowUtc));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return missing.Count;
        });

    public async Task<IReadOnlyList<MealDeliveryWriteTarget>> ListWriteTargetsAsync(
        MealCategory category,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<WriteRow> rows = await (
            from delivery in dbContext.MealCalendarDeliveries.AsNoTracking()
            where delivery.Category == category
                && delivery.LocalDate >= fromInclusive
                && delivery.LocalDate <= toInclusive
                && (delivery.State == MealDeliveryState.Pending
                    || delivery.State == MealDeliveryState.Written)
            join day in dbContext.MealMenuDays.AsNoTracking()
                on new { delivery.LocalDate, delivery.Category }
                equals new { day.LocalDate, day.Category }
            where day.Status == MealMenuDayStatus.Published
                && (delivery.State == MealDeliveryState.Pending
                    || day.ContentVersion > (delivery.AppliedContentVersion ?? 0))
            join subscription in dbContext.MealMenuSubscriptions.AsNoTracking()
                on delivery.UserId equals subscription.UserId
            where subscription.IsEnabled
            join connection in dbContext.GoogleCalendarConnections.AsNoTracking()
                on delivery.UserId equals connection.UserId into connections
            from connection in connections.DefaultIfEmpty()
            orderby delivery.LocalDate, delivery.UserId
            select new WriteRow
            {
                Delivery = delivery,
                MealText = day.MealText,
                ContentVersion = day.ContentVersion,
                Connection = connection,
                HasActiveLicense =
                    ActiveLicenseQuery.UserIds(dbContext).Contains(delivery.UserId),
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToWriteTarget)];
    }

    public async Task<IReadOnlyList<MealDeliveryRemovalTarget>> ListRemovalTargetsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<RemovalRow> rows = await (
            from delivery in dbContext.MealCalendarDeliveries.AsNoTracking()
            where (delivery.State == MealDeliveryState.Pending
                    || delivery.State == MealDeliveryState.Written)
                && (!dbContext.MealMenuSubscriptions.Any(subscription =>
                        subscription.UserId == delivery.UserId && subscription.IsEnabled)
                    || dbContext.MealMenuDays.Any(day =>
                        day.LocalDate == delivery.LocalDate
                        && day.Category == delivery.Category
                        && day.Status == MealMenuDayStatus.Withdrawn))
            join connection in dbContext.GoogleCalendarConnections.AsNoTracking()
                on delivery.UserId equals connection.UserId into connections
            from connection in connections.DefaultIfEmpty()
            orderby delivery.UpdatedAtUtc
            select new RemovalRow
            {
                Delivery = delivery,
                Connection = connection,
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToRemovalTarget)];
    }

    public Task MarkWrittenAsync(
        Guid deliveryId,
        string googleEventId,
        int contentVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAsync(
            deliveryId,
            delivery => delivery.MarkWritten(googleEventId, contentVersion, atUtc),
            cancellationToken);

    public Task MarkSkippedAsync(
        Guid deliveryId,
        MealDeliveryExclusionReason reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAsync(deliveryId, delivery => delivery.MarkSkipped(reason, atUtc), cancellationToken);

    public Task MarkFailedAsync(
        Guid deliveryId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAsync(deliveryId, delivery => delivery.MarkFailed(reason, atUtc), cancellationToken);

    public Task MarkRemovedAsync(
        Guid deliveryId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAsync(deliveryId, delivery => delivery.MarkRemoved(atUtc), cancellationToken);

    private async Task MutateAsync(
        Guid deliveryId,
        Action<MealCalendarDelivery> mutate,
        CancellationToken cancellationToken)
    {
        MealCalendarDelivery? delivery = await dbContext.MealCalendarDeliveries
            .SingleOrDefaultAsync(candidate => candidate.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        mutate(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MealDeliveryWriteTarget ToWriteTarget(WriteRow row)
    {
        // The same eligibility ladder as an announcement (ADR-107): the question is whether this
        // already-owed subscriber can be written to right now.
        MealDeliveryExclusionReason? exclusion =
            !row.HasActiveLicense ? MealDeliveryExclusionReason.LicenseInactive
            : row.Connection is null ? MealDeliveryExclusionReason.NoCalendarConnection
            : row.Connection.Status is not GoogleCalendarConnectionStatus.Authorized
                ? MealDeliveryExclusionReason.CalendarAuthorizationRevoked
            : row.Connection.ManagedCalendarUnavailableAtUtc is not null
                ? MealDeliveryExclusionReason.ManagedCalendarUnavailable
            : string.IsNullOrWhiteSpace(row.Connection.ManagedCalendarId)
                ? MealDeliveryExclusionReason.InitialSyncIncomplete
            : null;

        return new MealDeliveryWriteTarget
        {
            DeliveryId = row.Delivery.Id,
            UserId = row.Delivery.UserId,
            LocalDate = row.Delivery.LocalDate,
            Category = row.Delivery.Category,
            MealText = row.MealText,
            ContentVersion = row.ContentVersion,
            ProtectedRefreshToken = exclusion is null ? row.Connection!.ProtectedRefreshToken : null,
            ManagedCalendarId = row.Delivery.GoogleCalendarId ?? row.Connection?.ManagedCalendarId,
            CurrentExclusion = exclusion,
            GoogleEventId = row.Delivery.GoogleEventId,
            AppliedContentVersion = row.Delivery.AppliedContentVersion,
        };
    }

    private static MealDeliveryRemovalTarget ToRemovalTarget(RemovalRow row) => new()
    {
        DeliveryId = row.Delivery.Id,
        UserId = row.Delivery.UserId,
        GoogleEventId = row.Delivery.GoogleEventId,
        // The copy has to be removed from the calendar it was written to, which is the one recorded
        // on the row rather than whichever the connection points at now.
        ManagedCalendarId = row.Delivery.GoogleCalendarId ?? row.Connection?.ManagedCalendarId,
        ProtectedRefreshToken =
            row.Connection is { Status: GoogleCalendarConnectionStatus.Authorized }
                ? row.Connection.ProtectedRefreshToken
                : null,
    };

    private sealed class OwedRow
    {
        public required Guid UserId { get; init; }

        public required DateOnly LocalDate { get; init; }

        public required string ManagedCalendarId { get; init; }
    }

    private sealed class WriteRow
    {
        public required MealCalendarDelivery Delivery { get; init; }

        public required string MealText { get; init; }

        public required int ContentVersion { get; init; }

        public GoogleCalendarConnection? Connection { get; init; }

        public required bool HasActiveLicense { get; init; }
    }

    private sealed class RemovalRow
    {
        public required MealCalendarDelivery Delivery { get; init; }

        public GoogleCalendarConnection? Connection { get; init; }
    }
}
