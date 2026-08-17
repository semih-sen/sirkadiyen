using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Writes queued announcements onto recipients' managed calendars, and removes them again when one
/// is cancelled (ADR-107).
/// </summary>
/// <remarks>
/// This is the same shape as the schedule fan-out (systemPatterns §27) with one deliberate
/// difference: the per-recipient ledger is this module's own delivery table rather than the shared
/// calendar-event mapping, because an announcement is not a lesson and must never appear in the
/// ledger that decides what published truth owes a student.
/// <para>
/// Every write is idempotent on a deterministic event id, so a pass killed halfway simply re-runs:
/// an insert of an existing id is reported as already-present, a patch of a missing one as
/// not-found, and a delete of a gone event as a no-op.
/// </para>
/// </remarks>
public sealed class AnnouncementDispatchService(
    IAnnouncementStore store,
    ICalendarConnectionHealthWriter connectionStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    AnnouncementDispatchOptions options,
    TimeProvider timeProvider)
{
    public async Task<AnnouncementDispatchRunResult> RunPendingAsync(
        CancellationToken cancellationToken)
    {
        // Delivery is a calendar mutation, so it reads the same authoritative switch as every
        // other one and fails closed (ADR-034, ADR-043).
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new AnnouncementDispatchRunResult { Frozen = true, Announcements = [] };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<AnnouncementDispatchCandidate> candidates =
            await store.ListDispatchableAsync(now, options.AnnouncementBatchSize, cancellationToken);

        List<AnnouncementDispatchResult> results = [];
        foreach (AnnouncementDispatchCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(candidate.Status is CalendarAnnouncementStatus.Cancelling
                ? await CancelOneAsync(candidate, cancellationToken)
                : await DeliverOneAsync(candidate, cancellationToken));
        }

        return new AnnouncementDispatchRunResult { Frozen = false, Announcements = results };
    }

    private async Task<AnnouncementDispatchResult> DeliverOneAsync(
        AnnouncementDispatchCandidate announcement,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int budget = options.CalendarOperationsPerAnnouncementPerCycle;
        Accumulator accumulator = new(announcement.AnnouncementId);

        await store.ApplyDispatchOutcomeAsync(
            announcement.AnnouncementId,
            AnnouncementDispatchTransition.Started,
            now,
            cancellationToken);

        // One page, sized one past the budget: the extra row is how the pass learns that more work
        // remains without a second query, and re-listing inside the loop could never terminate
        // while a scoped freeze keeps returning the same recipients.
        IReadOnlyList<AnnouncementDeliveryTarget> targets =
            await store.ListDeliveryTargetsAsync(
                announcement.AnnouncementId,
                CalendarAnnouncementDeliveryState.Pending,
                budget + 1,
                cancellationToken);

        if (targets.Count == 0)
        {
            await store.ApplyDispatchOutcomeAsync(
                announcement.AnnouncementId,
                AnnouncementDispatchTransition.Completed,
                now,
                cancellationToken);
            return accumulator.ToResult(AnnouncementDispatchOutcome.Completed);
        }

        bool truncated = targets.Count > budget;
        foreach (AnnouncementDeliveryTarget target in targets.Take(budget))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await DeliverToOneAsync(announcement, target, accumulator, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GoogleCalendarTransientException exception)
            {
                // A rate limit or a 5xx is about the provider, not this recipient. Stop the whole
                // pass so the remaining recipients are not burned through the same limit.
                return await DeferAsync(announcement, accumulator, exception.Message, now, cancellationToken);
            }
            catch (Exception exception)
            {
                // One recipient's failure must not stop the campaign; the row records why.
                await store.MarkDeliveryFailedAsync(
                    target.DeliveryId,
                    exception.Message,
                    now,
                    cancellationToken);
                accumulator.Failed++;
            }
        }

        if (truncated || accumulator.LeftPending > 0)
        {
            await store.ApplyDispatchOutcomeAsync(
                announcement.AnnouncementId,
                AnnouncementDispatchTransition.DeferredForBudget,
                now,
                cancellationToken);
            return accumulator.ToResult(AnnouncementDispatchOutcome.InProgress);
        }

        await store.ApplyDispatchOutcomeAsync(
            announcement.AnnouncementId,
            AnnouncementDispatchTransition.Completed,
            now,
            cancellationToken);
        return accumulator.ToResult(AnnouncementDispatchOutcome.Completed);
    }

    private async Task DeliverToOneAsync(
        AnnouncementDispatchCandidate announcement,
        AnnouncementDeliveryTarget target,
        Accumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Eligibility is re-read at delivery time, not trusted from confirmation: a grant can die
        // in the minutes between, and a revoked student has stopped synchronizing (ADR-095).
        if (target.CurrentExclusion is { } exclusion)
        {
            await store.MarkDeliverySkippedAsync(
                target.DeliveryId,
                exclusion,
                now,
                cancellationToken);
            accumulator.Skipped++;
            return;
        }

        if (target.ClassYear is { } classYear && target.ProgramLanguage is { } language
            && await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope { ClassYear = classYear, ProgramLanguage = language },
                cancellationToken))
        {
            // Frozen is not skipped: the recipient is still owed this announcement, so the row
            // stays pending and the next pass after the thaw picks it up.
            accumulator.LeftPending++;
            return;
        }

        CalendarAccess access = new()
        {
            RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken!),
        };
        ManagedCalendarEvent calendarEvent =
            AnnouncementEventFactory.ToManagedEvent(target.UserId, announcement);

        try
        {
            if (target.AppliedContentVersion is null)
            {
                CalendarEventInsertOutcome inserted = await calendarClient.InsertEventAsync(
                    access,
                    target.ManagedCalendarId!,
                    calendarEvent,
                    cancellationToken);

                // AlreadyExists means a previous pass wrote it and crashed before the ledger row
                // was updated. Patching then makes the content current either way.
                if (inserted is CalendarEventInsertOutcome.AlreadyExists)
                {
                    await calendarClient.PatchEventAsync(
                        access,
                        target.ManagedCalendarId!,
                        calendarEvent,
                        cancellationToken);
                }

                accumulator.Written++;
            }
            else
            {
                CalendarEventPatchOutcome patched = await calendarClient.PatchEventAsync(
                    access,
                    target.ManagedCalendarId!,
                    calendarEvent,
                    cancellationToken);

                // The recipient deleted it themselves. Re-inserting keeps the ledger honest
                // rather than leaving a row claiming an event that is not there.
                if (patched is CalendarEventPatchOutcome.NotFound)
                {
                    await calendarClient.InsertEventAsync(
                        access,
                        target.ManagedCalendarId!,
                        calendarEvent,
                        cancellationToken);
                }

                accumulator.Patched++;
            }
        }
        catch (GoogleCalendarCredentialException)
        {
            // Recorded once, where it is discovered, so schedule synchronization learns about the
            // dead grant too instead of rediscovering it on its own next write (ADR-059).
            await connectionStore.MarkNeedsReauthorizationAsync(target.UserId, now, cancellationToken);
            await store.MarkDeliverySkippedAsync(
                target.DeliveryId,
                AnnouncementExclusionReason.CalendarAuthorizationRevoked,
                now,
                cancellationToken);
            accumulator.Skipped++;
            return;
        }
        catch (GoogleManagedCalendarUnavailableException)
        {
            await connectionStore.MarkManagedCalendarUnavailableAsync(
                target.UserId,
                now,
                cancellationToken);
            await store.MarkDeliverySkippedAsync(
                target.DeliveryId,
                AnnouncementExclusionReason.ManagedCalendarUnavailable,
                now,
                cancellationToken);
            accumulator.Skipped++;
            return;
        }

        await store.MarkDeliveryWrittenAsync(
            target.DeliveryId,
            calendarEvent.EventId,
            announcement.ContentVersion,
            now,
            cancellationToken);
    }

    /// <summary>
    /// Removes every copy of a cancelled announcement. Unlike a schedule deletion, this needs no
    /// published revision or semantic diff to authorize it (AI_GUIDELINE §13): the event was never
    /// schedule truth, and the authority is the named operator who asked for it — recorded on the
    /// announcement with their reason.
    /// </summary>
    private async Task<AnnouncementDispatchResult> CancelOneAsync(
        AnnouncementDispatchCandidate announcement,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int budget = options.CalendarOperationsPerAnnouncementPerCycle;
        Accumulator accumulator = new(announcement.AnnouncementId);

        await store.ApplyDispatchOutcomeAsync(
            announcement.AnnouncementId,
            AnnouncementDispatchTransition.Started,
            now,
            cancellationToken);

        IReadOnlyList<AnnouncementDeliveryTarget> targets =
            await store.ListDeliveryTargetsAsync(
                announcement.AnnouncementId,
                CalendarAnnouncementDeliveryState.Written,
                budget + 1,
                cancellationToken);

        bool truncated = targets.Count > budget;
        foreach (AnnouncementDeliveryTarget target in targets.Take(budget))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target.GoogleEventId is null || target.ManagedCalendarId is null)
            {
                // Nothing was ever written under this row, so there is nothing to remove.
                await store.MarkDeliveryRemovedAsync(target.DeliveryId, now, cancellationToken);
                accumulator.Removed++;
                continue;
            }

            try
            {
                if (target.ProtectedRefreshToken is null)
                {
                    // The grant is gone, so the event cannot be reached. It is left where it is
                    // and the row says so; nothing is claimed to have been removed.
                    await store.MarkDeliveryFailedAsync(
                        target.DeliveryId,
                        "Takvim yetkisi bulunmadığı için etkinlik kaldırılamadı.",
                        now,
                        cancellationToken);
                    accumulator.Failed++;
                    continue;
                }

                await calendarClient.DeleteEventAsync(
                    new CalendarAccess
                    {
                        RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken),
                    },
                    target.ManagedCalendarId,
                    target.GoogleEventId,
                    cancellationToken);
                await store.MarkDeliveryRemovedAsync(target.DeliveryId, now, cancellationToken);
                accumulator.Removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GoogleCalendarTransientException exception)
            {
                return await DeferAsync(announcement, accumulator, exception.Message, now, cancellationToken);
            }
            catch (GoogleCalendarCredentialException)
            {
                await connectionStore.MarkNeedsReauthorizationAsync(
                    target.UserId,
                    now,
                    cancellationToken);
                await store.MarkDeliveryFailedAsync(
                    target.DeliveryId,
                    "Takvim yetkisi geri alındığı için etkinlik kaldırılamadı.",
                    now,
                    cancellationToken);
                accumulator.Failed++;
            }
            catch (Exception exception)
            {
                await store.MarkDeliveryFailedAsync(
                    target.DeliveryId,
                    exception.Message,
                    now,
                    cancellationToken);
                accumulator.Failed++;
            }
        }

        if (truncated)
        {
            await store.ApplyDispatchOutcomeAsync(
                announcement.AnnouncementId,
                AnnouncementDispatchTransition.DeferredForBudget,
                now,
                cancellationToken);
            return accumulator.ToResult(AnnouncementDispatchOutcome.InProgress);
        }

        await store.ApplyDispatchOutcomeAsync(
            announcement.AnnouncementId,
            AnnouncementDispatchTransition.Cancelled,
            now,
            cancellationToken);
        return accumulator.ToResult(AnnouncementDispatchOutcome.Cancelled);
    }

    private async Task<AnnouncementDispatchResult> DeferAsync(
        AnnouncementDispatchCandidate announcement,
        Accumulator accumulator,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The attempt just counted by Started is included, which is why the comparison is >=.
        int attempts = announcement.DeliveryAttempts + 1;
        if (attempts >= options.MaximumDeliveryAttempts)
        {
            await store.MarkDeliveryRunFailedAsync(
                announcement.AnnouncementId,
                reason,
                now,
                cancellationToken);
            return accumulator.ToResult(AnnouncementDispatchOutcome.Failed, reason);
        }

        await store.DeferAfterFailureAsync(
            announcement.AnnouncementId,
            reason,
            now + options.BackoffFor(attempts),
            now,
            cancellationToken);
        return accumulator.ToResult(AnnouncementDispatchOutcome.Deferred, reason);
    }

    private sealed class Accumulator(Guid announcementId)
    {
        public int Written { get; set; }

        public int Patched { get; set; }

        public int Skipped { get; set; }

        public int Removed { get; set; }

        public int Failed { get; set; }

        /// <summary>Recipients left for a later pass, currently only because of a scoped freeze.</summary>
        public int LeftPending { get; set; }

        public AnnouncementDispatchResult ToResult(
            AnnouncementDispatchOutcome outcome,
            string? failureReason = null) =>
            new()
            {
                AnnouncementId = announcementId,
                Outcome = outcome,
                EventsWritten = Written,
                EventsPatched = Patched,
                RecipientsSkipped = Skipped,
                EventsRemoved = Removed,
                DeliveriesFailed = Failed,
                RecipientsDeferred = LeftPending,
                FailureReason = failureReason,
            };
    }
}

public sealed record AnnouncementDispatchRunResult
{
    /// <summary>Whether the run did nothing because the global operational freeze is active.</summary>
    public required bool Frozen { get; init; }

    public required IReadOnlyList<AnnouncementDispatchResult> Announcements { get; init; }
}

public sealed record AnnouncementDispatchResult
{
    public required Guid AnnouncementId { get; init; }

    public required AnnouncementDispatchOutcome Outcome { get; init; }

    public int EventsWritten { get; init; }

    public int EventsPatched { get; init; }

    public int RecipientsSkipped { get; init; }

    public int EventsRemoved { get; init; }

    public int DeliveriesFailed { get; init; }

    /// <summary>Recipients whose class/program pipeline is frozen; they stay queued.</summary>
    public int RecipientsDeferred { get; init; }

    public string? FailureReason { get; init; }
}

public enum AnnouncementDispatchOutcome
{
    /// <summary>Every recipient reached a terminal state.</summary>
    Completed,

    /// <summary>The budget was reached or some recipients are frozen; work remains.</summary>
    InProgress,

    /// <summary>A transient provider failure; the pass retries after a back-off.</summary>
    Deferred,

    /// <summary>The attempt cap was reached; an operator has to look at it.</summary>
    Failed,

    /// <summary>Every written copy has been removed.</summary>
    Cancelled,
}
