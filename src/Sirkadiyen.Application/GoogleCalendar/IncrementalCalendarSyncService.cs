using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Fans each dispatchable schedule diff out onto the calendars of the users it affects (ADR-059):
/// inserting newly applicable lessons, patching changed ones, and deleting those that no longer
/// apply — resumably, idempotently, and behind the operational freeze.
/// </summary>
/// <remarks>
/// It is driven by diff state, not an in-memory queue, so a worker killed mid-fan-out re-runs the
/// diff until its per-pass mutation budget is exhausted. Every completed per-user operation
/// converges into the durable ledger (a re-insert is a safe 409, an unchanged patch is skipped, a
/// delete of an absent event is a no-op), so a later pass naturally plans only what remains. The
/// mapping ledger is the authority for who currently holds a lesson; audience resolution decides
/// only insertions. Per the pipeline convention it returns rich per-diff results and leaves logging
/// to the worker.
/// </remarks>
public sealed class IncrementalCalendarSyncService(
    IScheduleDiffStore diffStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    ICalendarSyncTargetReadStore targetStore,
    ICalendarSyncConnectionStore connectionStore,
    IUserCalendarEventMappingStore mappingStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    IncrementalSyncOptions options,
    TimeProvider timeProvider,
    DepartmentColorService departmentColors)
{
    public async Task<IncrementalCalendarSyncRunResult> RunPendingAsync(
        CancellationToken cancellationToken)
    {
        // Every calendar-touching job reads the same authoritative switch and fails closed
        // (ADR-034/043): while frozen, no diff is dispatched and no calendar is written.
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new IncrementalCalendarSyncRunResult { Frozen = true, Diffs = [] };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<Guid> pending = await diffStore.ListPendingDispatchAsync(
            options.DiffDispatchBatchSize,
            now,
            cancellationToken);

        List<IncrementalCalendarSyncDiffResult> results = [];
        foreach (Guid diffId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await DispatchOneAsync(diffId, now, cancellationToken));
        }

        return new IncrementalCalendarSyncRunResult { Frozen = false, Diffs = results };
    }

    private async Task<IncrementalCalendarSyncDiffResult> DispatchOneAsync(
        Guid diffId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DispatchableDiff? diff = await diffStore.LoadForDispatchAsync(diffId, cancellationToken);
        if (diff is null)
        {
            // Another pass dispatched it, or it stopped being dispatchable, between the scan and now.
            return new IncrementalCalendarSyncDiffResult
            {
                DiffId = diffId,
                Outcome = IncrementalDispatchOutcome.NoLongerPending,
            };
        }

        DispatchAccumulator accumulator = new(
            now,
            options.CalendarOperationsPerDiffBatch);

        try
        {
            IReadOnlyDictionary<Guid, CanonicalScheduleRecord> records =
                await LoadSubjectRecordsAsync(diff.Entries, cancellationToken);

            CanonicalScheduleRecord? scopedRecord = records.Values.FirstOrDefault();
            if (scopedRecord is not null
                && await freezeStore.IsFrozenAsync(
                    new OperationalFreezeScope
                    {
                        ClassYear = scopedRecord.ClassYear,
                        ProgramLanguage = scopedRecord.ProgramLanguage,
                    },
                    cancellationToken))
            {
                return accumulator.ToResult(diffId, IncrementalDispatchOutcome.Frozen);
            }

            foreach (ScheduleDiffEntry entry in diff.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchEntryAsync(entry, records, accumulator, cancellationToken);
                if (accumulator.HasMoreWork)
                {
                    // This is ordinary quota yielding, not a failure. The diff remains pending
                    // without incrementing attempts or scheduling back-off. Every completed
                    // mutation changed the durable ledger, so the next plan naturally excludes it.
                    return accumulator.ToResult(
                        diffId,
                        IncrementalDispatchOutcome.PartiallyDispatched);
                }
            }

            await diffStore.MarkDispatchedAsync(diffId, now, cancellationToken);
            return accumulator.ToResult(diffId, IncrementalDispatchOutcome.Dispatched);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleCalendarTransientException transient)
        {
            // A transient Google failure (rate limiting, a 5xx) aborts this diff: it stays pending
            // with a back-off, or gives up after too many attempts, and re-runs idempotently later.
            CalendarDispatchState state = await diffStore.RecordDispatchFailureAsync(
                diffId,
                transient.Message,
                options.RetryBaseDelay,
                options.MaxDispatchAttempts,
                now,
                cancellationToken);
            return accumulator.ToResult(
                diffId,
                state is CalendarDispatchState.Failed
                    ? IncrementalDispatchOutcome.Failed
                    : IncrementalDispatchOutcome.Deferred,
                transient.Message);
        }
        catch (Exception exception)
        {
            // An unexpected error (a store fault, a bug) is treated like a transient failure so the
            // diff backs off and retries rather than being lost; recording that must not itself throw.
            try
            {
                CalendarDispatchState state = await diffStore.RecordDispatchFailureAsync(
                    diffId,
                    exception.Message,
                    options.RetryBaseDelay,
                    options.MaxDispatchAttempts,
                    now,
                    cancellationToken);
                return accumulator.ToResult(
                    diffId,
                    state is CalendarDispatchState.Failed
                        ? IncrementalDispatchOutcome.Failed
                        : IncrementalDispatchOutcome.Deferred,
                    exception.Message);
            }
            catch (Exception recordFailure) when (recordFailure is not OperationCanceledException)
            {
                return accumulator.ToResult(
                    diffId,
                    IncrementalDispatchOutcome.Failed,
                    exception.Message);
            }
        }
    }

    private async Task DispatchEntryAsync(
        ScheduleDiffEntry entry,
        IReadOnlyDictionary<Guid, CanonicalScheduleRecord> records,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        switch (entry.Change)
        {
            case ScheduleDiffChange.Created
                when entry.CurrentRecordId is { } currentId
                    && records.TryGetValue(currentId, out CanonicalScheduleRecord? current):
                await ReconcileRecordAsync(current, accumulator, cancellationToken);
                break;

            case ScheduleDiffChange.Updated
                when entry.Match == ScheduleDiffMatch.ExactStableIdentity
                    && entry.CurrentRecordId is { } exactCurrentId
                    && records.TryGetValue(
                        exactCurrentId,
                        out CanonicalScheduleRecord? exactCurrent):
                await ReconcileRecordAsync(exactCurrent, accumulator, cancellationToken);
                break;

            case ScheduleDiffChange.Updated
                when entry.PreviousRecordId is { } previousId
                    && records.TryGetValue(previousId, out CanonicalScheduleRecord? previous)
                    && entry.CurrentRecordId is { } updatedId
                    && records.TryGetValue(updatedId, out CanonicalScheduleRecord? updated):
                await ReconcileMovedRecordAsync(
                    previous,
                    updated,
                    accumulator,
                    cancellationToken);
                break;

            case ScheduleDiffChange.Deleted
                when entry.PreviousRecordId is { } previousId
                    && records.TryGetValue(previousId, out CanonicalScheduleRecord? previous):
                await DeleteForHoldersAsync(previous, accumulator, cancellationToken);
                break;

            default:
                // Unchanged and ambiguous entries never become calendar operations; a dispatchable
                // diff has no ambiguity by construction (it would have been held). A referenced
                // record that could not be loaded is skipped rather than stalling the whole diff.
                break;
        }
    }

    /// <summary>
    /// Brings one created or updated lesson into line for everyone: patches or removes it for current
    /// holders whose profile still (or no longer) matches, and inserts it for cohort users it now
    /// applies to but who do not yet have it.
    /// </summary>
    private async Task ReconcileRecordAsync(
        CanonicalScheduleRecord record,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEventMappingView> holders =
            await mappingStore.ListForStableIdentityAsync(
                record.SourceId,
                record.StableIdentity,
                cancellationToken);
        HashSet<Guid> holderIds = [.. holders.Select(holder => holder.UserId)];

        IReadOnlyDictionary<Guid, CalendarSyncTarget> holderTargets =
            await TargetsByUserIdsAsync(holderIds, cancellationToken);

        foreach (CalendarEventMappingView holder in holders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accumulator.IsFlaggedForReauth(holder.UserId)
                || !holderTargets.TryGetValue(holder.UserId, out CalendarSyncTarget? target))
            {
                // Not authorized or not finished initial sync: skip, leaving whatever they have.
                continue;
            }

            IncrementalCalendarOperation operation = IncrementalSyncPlanner.PlanForExistingHolder(
                record,
                target.Profile,
                holder.ContentHash);
            await ApplyHolderOperationAsync(operation, target, holder, record, accumulator, cancellationToken);
            if (accumulator.HasMoreWork)
            {
                return;
            }
        }

        IReadOnlyList<CalendarSyncTarget> cohort =
            await CohortTargetsAsync(record, accumulator, cancellationToken);
        foreach (CalendarSyncTarget target in cohort)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (holderIds.Contains(target.UserId) || accumulator.IsFlaggedForReauth(target.UserId))
            {
                continue;
            }

            if (IncrementalSyncPlanner.PlanForCohortCandidate(record, target.Profile)
                is IncrementalCalendarOperation.Insert)
            {
                if (!accumulator.TryReserveCalendarOperation())
                {
                    return;
                }

                await ApplyInsertAsync(target, record, accumulator, cancellationToken);
            }
        }
    }

    private async Task DeleteForHoldersAsync(
        CanonicalScheduleRecord previousRecord,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEventMappingView> holders =
            await mappingStore.ListForStableIdentityAsync(
                previousRecord.SourceId,
                previousRecord.StableIdentity,
                cancellationToken);
        if (holders.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<Guid, CalendarSyncTarget> targets = await TargetsByUserIdsAsync(
            [.. holders.Select(holder => holder.UserId)],
            cancellationToken);

        foreach (CalendarEventMappingView holder in holders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accumulator.IsFlaggedForReauth(holder.UserId)
                || !targets.TryGetValue(holder.UserId, out CalendarSyncTarget? target))
            {
                continue;
            }

            if (!accumulator.TryReserveCalendarOperation())
            {
                return;
            }

            await ApplyDeleteAsync(target, holder, accumulator, cancellationToken);
        }
    }

    /// <summary>
    /// Applies a secondary-matched update whose start-time identity changed. Existing holders keep
    /// their Google event id; only the ledger identity and private marker move to the current record.
    /// </summary>
    private async Task ReconcileMovedRecordAsync(
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEventMappingView> previousHolders =
            await mappingStore.ListForStableIdentityAsync(
                previous.SourceId,
                previous.StableIdentity,
                cancellationToken);
        IReadOnlyList<CalendarEventMappingView> currentHolders =
            await mappingStore.ListForStableIdentityAsync(
                current.SourceId,
                current.StableIdentity,
                cancellationToken);

        HashSet<Guid> duplicateUsers =
        [
            .. previousHolders.Select(holder => holder.UserId)
                .Intersect(currentHolders.Select(holder => holder.UserId)),
        ];
        if (duplicateUsers.Count > 0)
        {
            throw new InvalidOperationException(
                $"A secondary-matched update found both previous and current mappings for "
                + $"{duplicateUsers.Count} user(s). Automatic event merging is unsafe.");
        }

        CalendarEventMappingView[] holders = [.. previousHolders, .. currentHolders];
        HashSet<Guid> holderIds = [.. holders.Select(holder => holder.UserId)];
        IReadOnlyDictionary<Guid, CalendarSyncTarget> targets =
            await TargetsByUserIdsAsync(holderIds, cancellationToken);

        foreach (CalendarEventMappingView holder in holders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accumulator.IsFlaggedForReauth(holder.UserId)
                || !targets.TryGetValue(holder.UserId, out CalendarSyncTarget? target))
            {
                continue;
            }

            IncrementalCalendarOperation operation = IncrementalSyncPlanner.PlanForExistingHolder(
                current,
                target.Profile,
                holder.ContentHash);
            bool isPreviousHolder = string.Equals(
                holder.StableIdentity,
                previous.StableIdentity,
                StringComparison.Ordinal);

            switch (operation)
            {
                case IncrementalCalendarOperation.Patch:
                    if (!accumulator.TryReserveCalendarOperation())
                    {
                        return;
                    }

                    await ApplyPatchAsync(
                        target,
                        holder,
                        current,
                        isPreviousHolder ? previous.StableIdentity : null,
                        accumulator,
                        cancellationToken);
                    break;

                case IncrementalCalendarOperation.Delete:
                    if (!accumulator.TryReserveCalendarOperation())
                    {
                        return;
                    }

                    await ApplyDeleteAsync(target, holder, accumulator, cancellationToken);
                    break;

                case IncrementalCalendarOperation.None when isPreviousHolder:
                    // A stable-identity move must still patch the event's time/private marker even
                    // if an old producer happened to emit the same content hash on both sides.
                    if (!accumulator.TryReserveCalendarOperation())
                    {
                        return;
                    }

                    await ApplyPatchAsync(
                        target,
                        holder,
                        current,
                        previous.StableIdentity,
                        accumulator,
                        cancellationToken);
                    break;

                case IncrementalCalendarOperation.None:
                    break;

                case IncrementalCalendarOperation.Insert:
                default:
                    throw new InvalidOperationException(
                        $"Unexpected operation '{operation}' for an existing Calendar mapping.");
            }
        }

        IReadOnlyList<CalendarSyncTarget> cohort =
            await CohortTargetsAsync(current, accumulator, cancellationToken);
        foreach (CalendarSyncTarget target in cohort)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (holderIds.Contains(target.UserId) || accumulator.IsFlaggedForReauth(target.UserId))
            {
                continue;
            }

            if (IncrementalSyncPlanner.PlanForCohortCandidate(current, target.Profile)
                is IncrementalCalendarOperation.Insert)
            {
                if (!accumulator.TryReserveCalendarOperation())
                {
                    return;
                }

                await ApplyInsertAsync(target, current, accumulator, cancellationToken);
            }
        }
    }

    private async Task ApplyHolderOperationAsync(
        IncrementalCalendarOperation operation,
        CalendarSyncTarget target,
        CalendarEventMappingView holder,
        CanonicalScheduleRecord record,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case IncrementalCalendarOperation.Patch:
                if (!accumulator.TryReserveCalendarOperation())
                {
                    return;
                }

                await ApplyPatchAsync(
                    target,
                    holder,
                    record,
                    previousStableIdentity: null,
                    accumulator,
                    cancellationToken);
                break;
            case IncrementalCalendarOperation.Delete:
                if (!accumulator.TryReserveCalendarOperation())
                {
                    return;
                }

                await ApplyDeleteAsync(target, holder, accumulator, cancellationToken);
                break;
            case IncrementalCalendarOperation.Insert:
            case IncrementalCalendarOperation.None:
            default:
                break;
        }
    }

    private async Task ApplyInsertAsync(
        CalendarSyncTarget target,
        CanonicalScheduleRecord record,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                target.UserId,
                cancellationToken);
        ManagedCalendarEvent calendarEvent = ManagedCalendarEventFactory.ToManagedEvent(
            target.UserId,
            record,
            colors);

        try
        {
            CalendarAccess access = AccessFor(target);
            await calendarClient.InsertEventAsync(
                access,
                target.ManagedCalendarId,
                calendarEvent,
                cancellationToken);

            UserCalendarEventMapping mapping = UserCalendarEventMapping.Create(
                target.UserId,
                record.StableIdentity,
                record.SourceId,
                record.Id,
                target.ManagedCalendarId,
                calendarEvent.EventId,
                record.ContentHash,
                accumulator.Now);
            await mappingStore.AddAsync(mapping, cancellationToken);
            accumulator.Inserted++;
        }
        catch (GoogleCalendarCredentialException)
        {
            await FlagForReauthAsync(target.UserId, accumulator, cancellationToken);
        }
    }

    private async Task ApplyPatchAsync(
        CalendarSyncTarget target,
        CalendarEventMappingView holder,
        CanonicalScheduleRecord record,
        string? previousStableIdentity,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        // A mapping reidentified after a secondary match deliberately keeps the Google event id
        // created from its original stable identity. The ledger, not a re-derived id, is therefore
        // the authoritative patch target.
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                target.UserId,
                cancellationToken);
        ManagedCalendarEvent calendarEvent =
            ManagedCalendarEventFactory.ToManagedEvent(target.UserId, record, colors) with
            {
                EventId = holder.GoogleEventId,
            };

        try
        {
            CalendarAccess access = AccessFor(target);
            CalendarEventPatchOutcome outcome = await calendarClient.PatchEventAsync(
                access,
                holder.GoogleCalendarId,
                calendarEvent,
                cancellationToken);

            if (outcome is CalendarEventPatchOutcome.NotFound)
            {
                // The ledger says this event should exist, so a missing one is re-created rather than
                // left divergent; the deterministic id keeps this idempotent (ADR-024, ADR-059).
                await calendarClient.InsertEventAsync(
                    access,
                    holder.GoogleCalendarId,
                    calendarEvent,
                    cancellationToken);
            }

            if (previousStableIdentity is null)
            {
                await mappingStore.UpdateContentAsync(
                    target.UserId,
                    holder.StableIdentity,
                    record.Id,
                    record.ContentHash,
                    accumulator.Now,
                    cancellationToken);
            }
            else
            {
                await ReidentifyMappingAsync(
                    target.UserId,
                    previousStableIdentity,
                    record,
                    accumulator.Now,
                    cancellationToken);
            }

            accumulator.Patched++;
        }
        catch (GoogleCalendarCredentialException)
        {
            await FlagForReauthAsync(target.UserId, accumulator, cancellationToken);
        }
    }

    private async Task ReidentifyMappingAsync(
        Guid userId,
        string previousStableIdentity,
        CanonicalScheduleRecord current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CalendarEventMappingReidentifyOutcome outcome = await mappingStore.ReidentifyAsync(
            userId,
            current.SourceId,
            previousStableIdentity,
            current.StableIdentity,
            current.Id,
            current.ContentHash,
            now,
            cancellationToken);
        if (outcome is CalendarEventMappingReidentifyOutcome.NotFound
            or CalendarEventMappingReidentifyOutcome.Conflict)
        {
            throw new InvalidOperationException(
                $"Could not move user '{userId}' Calendar mapping from "
                + $"'{previousStableIdentity}' to '{current.StableIdentity}': {outcome}.");
        }
    }

    private async Task ApplyDeleteAsync(
        CalendarSyncTarget target,
        CalendarEventMappingView holder,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        try
        {
            CalendarAccess access = AccessFor(target);
            await calendarClient.DeleteEventAsync(
                access,
                holder.GoogleCalendarId,
                holder.GoogleEventId,
                cancellationToken);

            await mappingStore.RemoveAsync(target.UserId, holder.StableIdentity, cancellationToken);
            accumulator.Deleted++;
        }
        catch (GoogleCalendarCredentialException)
        {
            await FlagForReauthAsync(target.UserId, accumulator, cancellationToken);
        }
    }

    private async Task FlagForReauthAsync(
        Guid userId,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        if (accumulator.FlagForReauth(userId))
        {
            await connectionStore.MarkNeedsReauthorizationAsync(userId, accumulator.Now, cancellationToken);
        }
    }

    private CalendarAccess AccessFor(CalendarSyncTarget target) => new()
    {
        RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken),
    };

    private async Task<IReadOnlyList<CalendarSyncTarget>> CohortTargetsAsync(
        CanonicalScheduleRecord record,
        DispatchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        string key = $"{record.AcademicYear}|{record.ClassYear}|{record.ProgramLanguage}";
        if (accumulator.CohortCache.TryGetValue(key, out IReadOnlyList<CalendarSyncTarget>? cached))
        {
            return cached;
        }

        IReadOnlyList<CalendarSyncTarget> targets = await targetStore.ListCohortTargetsAsync(
            record.AcademicYear,
            record.ClassYear,
            record.ProgramLanguage,
            cancellationToken);
        accumulator.CohortCache[key] = targets;
        return targets;
    }

    private async Task<IReadOnlyDictionary<Guid, CalendarSyncTarget>> TargetsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, CalendarSyncTarget>();
        }

        IReadOnlyList<CalendarSyncTarget> targets = await targetStore.ListTargetsByUserIdsAsync(
            userIds,
            cancellationToken);
        return targets.ToDictionary(target => target.UserId);
    }

    private async Task<IReadOnlyDictionary<Guid, CanonicalScheduleRecord>> LoadSubjectRecordsAsync(
        IReadOnlyList<ScheduleDiffEntry> entries,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> ids = [];
        foreach (ScheduleDiffEntry entry in entries)
        {
            if (entry.PreviousRecordId is { } previousId)
            {
                ids.Add(previousId);
            }

            if (entry.CurrentRecordId is { } currentId)
            {
                ids.Add(currentId);
            }
        }

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, CanonicalScheduleRecord>();
        }

        IReadOnlyList<CanonicalScheduleRecord> records =
            await scheduleReadStore.ListRecordsByIdsAsync(ids, cancellationToken);
        return records.ToDictionary(record => record.Id);
    }

    private sealed class DispatchAccumulator(
        DateTimeOffset now,
        int calendarOperationsPerDiffBatch)
    {
        private readonly HashSet<Guid> reauthFlagged = [];

        public DateTimeOffset Now { get; } = now;

        public int Inserted { get; set; }

        public int Patched { get; set; }

        public int Deleted { get; set; }

        public int CalendarOperationsAttempted { get; private set; }

        /// <summary>
        /// True only after the planner found one more required mutation than this pass may attempt.
        /// Merely reaching the numeric limit is not enough: if no further mutation exists, the diff
        /// is complete and may be marked dispatched in this pass.
        /// </summary>
        public bool HasMoreWork { get; private set; }

        public Dictionary<string, IReadOnlyList<CalendarSyncTarget>> CohortCache { get; } =
            new(StringComparer.Ordinal);

        public bool IsFlaggedForReauth(Guid userId) => reauthFlagged.Contains(userId);

        /// <summary>Records a user as flagged; returns true the first time so the store is hit once.</summary>
        public bool FlagForReauth(Guid userId) => reauthFlagged.Add(userId);

        public bool TryReserveCalendarOperation()
        {
            if (CalendarOperationsAttempted >= calendarOperationsPerDiffBatch)
            {
                HasMoreWork = true;
                return false;
            }

            CalendarOperationsAttempted++;
            return true;
        }

        public IncrementalCalendarSyncDiffResult ToResult(
            Guid diffId,
            IncrementalDispatchOutcome outcome,
            string? failureReason = null) => new()
            {
                DiffId = diffId,
                Outcome = outcome,
                Inserted = Inserted,
                Patched = Patched,
                Deleted = Deleted,
                CalendarOperationsAttempted = CalendarOperationsAttempted,
                ReauthFlagged = reauthFlagged.Count,
                FailureReason = failureReason,
            };
    }
}

public sealed record IncrementalCalendarSyncRunResult
{
    /// <summary>Whether the run did nothing because the global operational freeze is active.</summary>
    public required bool Frozen { get; init; }

    public required IReadOnlyList<IncrementalCalendarSyncDiffResult> Diffs { get; init; }
}

public sealed record IncrementalCalendarSyncDiffResult
{
    public required Guid DiffId { get; init; }

    public required IncrementalDispatchOutcome Outcome { get; init; }

    public int Inserted { get; init; }

    public int Patched { get; init; }

    public int Deleted { get; init; }

    /// <summary>
    /// Per-user Calendar mutations attempted in this pass, including an attempt that discovered a
    /// dead credential. A patch that finds a missing event may issue one recovery insert too.
    /// </summary>
    public int CalendarOperationsAttempted { get; init; }

    /// <summary>How many users were flagged for re-authorization because their credential was dead.</summary>
    public int ReauthFlagged { get; init; }

    /// <summary>Why dispatch was deferred or failed, when it was.</summary>
    public string? FailureReason { get; init; }
}

public enum IncrementalDispatchOutcome
{
    Frozen,
    /// <summary>Every applicable calendar was updated; the diff is marked dispatched.</summary>
    Dispatched,

    /// <summary>
    /// This pass reached its per-diff mutation budget. The diff remains pending and resumes from
    /// the converged ledger on the next worker cycle without counting as a failure.
    /// </summary>
    PartiallyDispatched,

    /// <summary>A transient failure deferred the diff with a back-off; it will retry.</summary>
    Deferred,

    /// <summary>Too many attempts failed; the diff needs an operator rather than another retry.</summary>
    Failed,

    /// <summary>The diff was no longer dispatch-pending when loaded; nothing to do.</summary>
    NoLongerPending,
}
