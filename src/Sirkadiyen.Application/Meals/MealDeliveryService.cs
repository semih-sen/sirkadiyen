using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Converges each subscriber's calendar to the current set of published menu-days: it writes the
/// copies that are owed and removes the ones that no longer are (ADR-150).
/// </summary>
/// <remarks>
/// The same shape as announcement delivery (ADR-107) with one deliberate difference: there is no
/// per-campaign attempt counter or back-off, because a menu is a standing subscription that the next
/// cycle reconciles again anyway. A transient provider failure simply stops the pass; the ledger it
/// leaves behind is what the next cycle resumes from. Every write is idempotent on a deterministic
/// event id, so a pass killed halfway re-runs cleanly.
/// </remarks>
public sealed class MealDeliveryService(
    IMealDeliveryStore store,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    ICalendarConnectionHealthWriter connectionStore,
    IOperationalFreezeStore freezeStore,
    MealMenuOptions options,
    TimeProvider timeProvider)
{
    public async Task<MealDeliveryRunResult> RunAsync(CancellationToken cancellationToken)
    {
        // Delivery is a calendar mutation, so it reads the same authoritative switch as every other
        // one and fails closed (ADR-034, ADR-043).
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new MealDeliveryRunResult { Frozen = true };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = TodayLocal(now);
        DateOnly windowEnd = today.AddDays(options.WindowDays);
        MealEventPresentation presentation = options.Presentation();
        presentation.Validate();

        await store.ReconcileOwedAsync(options.Category, today, windowEnd, now, cancellationToken);

        Accumulator accumulator = new();
        bool moreWrites = await RunWritesAsync(
            today, windowEnd, presentation, accumulator, cancellationToken);
        bool moreRemovals = await RunRemovalsAsync(accumulator, cancellationToken);

        return accumulator.ToResult(frozen: false, workRemains: moreWrites || moreRemovals);
    }

    private async Task<bool> RunWritesAsync(
        DateOnly today,
        DateOnly windowEnd,
        MealEventPresentation presentation,
        Accumulator accumulator,
        CancellationToken cancellationToken)
    {
        int budget = options.MaxWritesPerCycle;

        // One page, sized one past the budget: the extra row is how the pass learns more work
        // remains without a second query.
        IReadOnlyList<MealDeliveryWriteTarget> targets = await store.ListWriteTargetsAsync(
            options.Category, today, windowEnd, budget + 1, cancellationToken);
        bool truncated = targets.Count > budget;

        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (MealDeliveryWriteTarget target in targets.Take(budget))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target.CurrentExclusion is { } exclusion)
            {
                await store.MarkSkippedAsync(target.DeliveryId, exclusion, now, cancellationToken);
                accumulator.Skipped++;
                continue;
            }

            try
            {
                await WriteOneAsync(target, presentation, accumulator, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GoogleCalendarTransientException exception)
            {
                // A rate limit or a 5xx is about the provider, not this subscriber. Stop the pass so
                // the rest are not burned through the same limit; the next cycle resumes.
                accumulator.MarkDeferred(exception.Message);
                return true;
            }
            catch (Exception exception)
            {
                await store.MarkFailedAsync(
                    target.DeliveryId, exception.Message, now, cancellationToken);
                accumulator.Failed++;
            }
        }

        return truncated;
    }

    private async Task WriteOneAsync(
        MealDeliveryWriteTarget target,
        MealEventPresentation presentation,
        Accumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CalendarAccess access = new()
        {
            RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken!),
        };
        ManagedCalendarEvent calendarEvent = MealEventFactory.ToManagedEvent(
            target.UserId,
            new MealMenuDayContent
            {
                LocalDate = target.LocalDate,
                Category = target.Category,
                MealText = target.MealText,
                ContentVersion = target.ContentVersion,
            },
            presentation);

        try
        {
            if (target.AppliedContentVersion is null)
            {
                CalendarEventInsertOutcome inserted = await calendarClient.InsertEventAsync(
                    access, target.ManagedCalendarId!, calendarEvent, cancellationToken);

                // AlreadyExists means a previous pass wrote it and crashed before the row was
                // updated; patching makes the content current either way.
                if (inserted is CalendarEventInsertOutcome.AlreadyExists)
                {
                    await calendarClient.PatchEventAsync(
                        access, target.ManagedCalendarId!, calendarEvent, cancellationToken);
                }

                accumulator.Written++;
            }
            else
            {
                CalendarEventPatchOutcome patched = await calendarClient.PatchEventAsync(
                    access, target.ManagedCalendarId!, calendarEvent, cancellationToken);

                // The subscriber deleted it themselves. Re-inserting keeps the ledger honest.
                if (patched is CalendarEventPatchOutcome.NotFound)
                {
                    await calendarClient.InsertEventAsync(
                        access, target.ManagedCalendarId!, calendarEvent, cancellationToken);
                }

                accumulator.Patched++;
            }
        }
        catch (GoogleCalendarCredentialException)
        {
            // Recorded once, where it is discovered, so schedule synchronization learns about the
            // dead grant too instead of rediscovering it on its own next write (ADR-059).
            await connectionStore.MarkNeedsReauthorizationAsync(target.UserId, now, cancellationToken);
            await store.MarkSkippedAsync(
                target.DeliveryId,
                MealDeliveryExclusionReason.CalendarAuthorizationRevoked,
                now,
                cancellationToken);
            accumulator.Skipped++;
            return;
        }
        catch (GoogleManagedCalendarUnavailableException)
        {
            await connectionStore.MarkManagedCalendarUnavailableAsync(
                target.UserId, now, cancellationToken);
            await store.MarkSkippedAsync(
                target.DeliveryId,
                MealDeliveryExclusionReason.ManagedCalendarUnavailable,
                now,
                cancellationToken);
            accumulator.Skipped++;
            return;
        }

        await store.MarkWrittenAsync(
            target.DeliveryId, calendarEvent.EventId, target.ContentVersion, now, cancellationToken);
    }

    private async Task<bool> RunRemovalsAsync(
        Accumulator accumulator,
        CancellationToken cancellationToken)
    {
        int budget = options.MaxRemovalsPerCycle;
        IReadOnlyList<MealDeliveryRemovalTarget> targets =
            await store.ListRemovalTargetsAsync(budget + 1, cancellationToken);
        bool truncated = targets.Count > budget;

        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (MealDeliveryRemovalTarget target in targets.Take(budget))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target.GoogleEventId is null || target.ManagedCalendarId is null)
            {
                // Nothing was ever written under this row, so there is nothing to remove.
                await store.MarkRemovedAsync(target.DeliveryId, now, cancellationToken);
                accumulator.Removed++;
                continue;
            }

            if (target.ProtectedRefreshToken is null)
            {
                // The grant is gone, so the event cannot be reached. It is left where it is and the
                // row says so; nothing is claimed to have been removed.
                await store.MarkFailedAsync(
                    target.DeliveryId,
                    "Takvim yetkisi bulunmadığı için yemek etkinliği kaldırılamadı.",
                    now,
                    cancellationToken);
                accumulator.Failed++;
                continue;
            }

            try
            {
                await calendarClient.DeleteEventAsync(
                    new CalendarAccess { RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken) },
                    target.ManagedCalendarId,
                    target.GoogleEventId,
                    cancellationToken);
                await store.MarkRemovedAsync(target.DeliveryId, now, cancellationToken);
                accumulator.Removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GoogleCalendarTransientException exception)
            {
                accumulator.MarkDeferred(exception.Message);
                return true;
            }
            catch (GoogleCalendarCredentialException)
            {
                await connectionStore.MarkNeedsReauthorizationAsync(target.UserId, now, cancellationToken);
                await store.MarkFailedAsync(
                    target.DeliveryId,
                    "Takvim yetkisi geri alındığı için yemek etkinliği kaldırılamadı.",
                    now,
                    cancellationToken);
                accumulator.Failed++;
            }
            catch (Exception exception)
            {
                await store.MarkFailedAsync(target.DeliveryId, exception.Message, now, cancellationToken);
                accumulator.Failed++;
            }
        }

        return truncated;
    }

    private DateOnly TodayLocal(DateTimeOffset nowUtc)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
    }

    private sealed class Accumulator
    {
        public int Written { get; set; }

        public int Patched { get; set; }

        public int Skipped { get; set; }

        public int Removed { get; set; }

        public int Failed { get; set; }

        private bool _deferred;
        private string? _failureReason;

        public void MarkDeferred(string reason)
        {
            _deferred = true;
            _failureReason ??= reason;
        }

        public MealDeliveryRunResult ToResult(bool frozen, bool workRemains) => new()
        {
            Frozen = frozen,
            WorkRemains = workRemains || _deferred,
            Deferred = _deferred,
            EventsWritten = Written,
            EventsPatched = Patched,
            SubscribersSkipped = Skipped,
            EventsRemoved = Removed,
            DeliveriesFailed = Failed,
            FailureReason = _failureReason,
        };
    }
}

/// <summary>What one meal delivery pass did, for logging and metrics (AI_GUIDELINE §19).</summary>
public sealed record MealDeliveryRunResult
{
    /// <summary>Whether the pass did nothing because the global operational freeze is active.</summary>
    public bool Frozen { get; init; }

    /// <summary>Whether work remains, so the worker shortens the next cycle.</summary>
    public bool WorkRemains { get; init; }

    /// <summary>Whether the pass stopped early on a transient provider failure.</summary>
    public bool Deferred { get; init; }

    public int EventsWritten { get; init; }

    public int EventsPatched { get; init; }

    public int SubscribersSkipped { get; init; }

    public int EventsRemoved { get; init; }

    public int DeliveriesFailed { get; init; }

    public string? FailureReason { get; init; }
}
