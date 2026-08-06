using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Replays globally dispatched semantic diffs for users who regained Calendar authorization
/// (ADR-060). It is edge-triggered from immutable diff evidence: a current-state absence never
/// becomes a delete command.
/// </summary>
public sealed class CalendarReconciliationService(
    IScheduleDiffStore diffStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IStudentProfileStore profileStore,
    ICalendarSyncConnectionStore connectionStore,
    IUserCalendarEventMappingStore mappingStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    CalendarReconciliationOptions options,
    TimeProvider timeProvider,
    DepartmentColorService departmentColors)
{
    public async Task<CalendarReconciliationRunResult> RunPendingAsync(
        CancellationToken cancellationToken)
    {
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new CalendarReconciliationRunResult { Frozen = true, Users = [] };
        }

        IReadOnlyList<PendingCalendarReconciliation> pending =
            await connectionStore.ListPendingReconciliationAsync(
                options.ConnectionBatchSize,
                cancellationToken);

        List<CalendarReconciliationUserResult> results = [];
        foreach (PendingCalendarReconciliation connection in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReconcileOneAsync(connection, cancellationToken));
        }

        return new CalendarReconciliationRunResult { Frozen = false, Users = results };
    }

    private async Task<CalendarReconciliationUserResult> ReconcileOneAsync(
        PendingCalendarReconciliation connection,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        ReconciliationAccumulator accumulator = new(connection.UserId);

        try
        {
            StudentProfileView? profile = await profileStore.GetByUserIdAsync(
                connection.UserId,
                cancellationToken);
            if (profile is null)
            {
                return accumulator.ToResult(
                    CalendarReconciliationOutcome.Failed,
                    "The completed Calendar connection has no student profile.");
            }

            if (await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = profile.ClassYear,
                    ProgramLanguage = profile.ProgramLanguage,
                },
                cancellationToken))
            {
                return accumulator.ToResult(CalendarReconciliationOutcome.Frozen);
            }

            IReadOnlyList<DispatchedDiff> diffs =
                await diffStore.ListDispatchedForReplayAsync(
                    connection.CursorDispatchedAtUtc,
                    connection.CursorDiffId,
                    options.DiffsPerConnectionPerCycle,
                    cancellationToken);

            if (diffs.Count == 0)
            {
                await connectionStore.CompleteReconciliationAsync(
                    connection.UserId,
                    connection.RequiredSinceUtc,
                    now,
                    cancellationToken);
                return accumulator.ToResult(CalendarReconciliationOutcome.Completed);
            }

            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(connection.ProtectedRefreshToken),
            };

            foreach (DispatchedDiff diff in diffs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyDictionary<Guid, CanonicalScheduleRecord> records =
                    await LoadReferencedRecordsAsync(diff, cancellationToken);

                foreach (ScheduleDiffEntry entry in diff.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ApplyEntryAsync(
                        connection,
                        profile,
                        access,
                        diff,
                        entry,
                        records,
                        accumulator,
                        now,
                        cancellationToken);
                }

                // The cursor moves only after every entry in this semantic diff converged. A crash
                // before this write replays the whole diff through the idempotent ledger.
                await connectionStore.AdvanceReconciliationCursorAsync(
                    connection.UserId,
                    connection.RequiredSinceUtc,
                    diff.DispatchedAtUtc,
                    diff.DiffId,
                    now,
                    cancellationToken);
                accumulator.DiffsReplayed++;
            }

            // Completion is deliberately a later empty scan. This yields between bounded batches
            // and gives a concurrently finishing global dispatch another cycle to become visible.
            return accumulator.ToResult(CalendarReconciliationOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleCalendarCredentialException exception)
        {
            // Preserve the current cursor. Re-authorization keeps it, and the partially applied
            // diff is replayed from its beginning when the user returns.
            await connectionStore.MarkNeedsReauthorizationAsync(
                connection.UserId,
                now,
                cancellationToken);
            return accumulator.ToResult(
                CalendarReconciliationOutcome.AuthorizationRequired,
                exception.Message);
        }
        catch (GoogleCalendarTransientException exception)
        {
            // There is no separate reconciliation job row yet. Keeping the cursor replays this
            // user's current diff next cycle; the Calendar client has already exhausted its bounded
            // in-call back-off.
            return accumulator.ToResult(CalendarReconciliationOutcome.Deferred, exception.Message);
        }
        catch (Exception exception)
        {
            // Store faults, invalid immutable references and ledger conflicts must not advance or
            // clear the cursor. They remain visible for an operator instead of guessing a repair.
            return accumulator.ToResult(CalendarReconciliationOutcome.Failed, exception.Message);
        }
    }

    private async Task ApplyEntryAsync(
        PendingCalendarReconciliation connection,
        StudentProfileView profile,
        CalendarAccess access,
        DispatchedDiff diff,
        ScheduleDiffEntry entry,
        IReadOnlyDictionary<Guid, CanonicalScheduleRecord> records,
        ReconciliationAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (entry.Change)
        {
            case ScheduleDiffChange.Created:
                {
                    CanonicalScheduleRecord current = RequiredRecord(
                        entry.CurrentRecordId,
                        records,
                        diff,
                        "current");
                    EnsureSource(diff.SourceId, current);
                    await ApplyCreatedAsync(
                        connection,
                        profile,
                        access,
                        current,
                        accumulator,
                        now,
                        cancellationToken);
                    break;
                }

            case ScheduleDiffChange.Updated:
                {
                    CanonicalScheduleRecord previous = RequiredRecord(
                        entry.PreviousRecordId,
                        records,
                        diff,
                        "previous");
                    CanonicalScheduleRecord current = RequiredRecord(
                        entry.CurrentRecordId,
                        records,
                        diff,
                        "current");
                    EnsureSource(diff.SourceId, previous);
                    EnsureSource(diff.SourceId, current);
                    await ApplyUpdatedAsync(
                        connection,
                        profile,
                        access,
                        previous,
                        current,
                        accumulator,
                        now,
                        cancellationToken);
                    break;
                }

            case ScheduleDiffChange.Deleted:
                {
                    CanonicalScheduleRecord previous = RequiredRecord(
                        entry.PreviousRecordId,
                        records,
                        diff,
                        "previous");
                    EnsureSource(diff.SourceId, previous);
                    await ApplyDeletedAsync(
                        connection.UserId,
                        access,
                        previous,
                        accumulator,
                        cancellationToken);
                    break;
                }

            case ScheduleDiffChange.Unchanged:
                break;

            case ScheduleDiffChange.Ambiguous:
            default:
                throw new InvalidOperationException(
                    $"Dispatched diff '{diff.DiffId}' contains an ambiguous entry.");
        }
    }

    private async Task ApplyCreatedAsync(
        PendingCalendarReconciliation connection,
        StudentProfileView profile,
        CalendarAccess access,
        CanonicalScheduleRecord current,
        ReconciliationAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CalendarEventMappingView? mapping = await MappingForUserAsync(
            connection.UserId,
            current.SourceId,
            current.StableIdentity,
            cancellationToken);

        if (mapping is null)
        {
            if (CalendarAudienceResolver.Applies(current, profile))
            {
                await InsertAsync(
                    connection,
                    access,
                    current,
                    accumulator,
                    now,
                    cancellationToken);
            }

            return;
        }

        // A Created entry can confirm an insert or refresh already-caught-up content. It does not
        // authorize deleting a pre-existing mapping merely because today's profile does not match.
        if (CalendarAudienceResolver.Applies(current, profile)
            && !string.Equals(mapping.ContentHash, current.ContentHash, StringComparison.Ordinal))
        {
            await PatchAsync(
                connection.UserId,
                access,
                mapping,
                current,
                previousStableIdentity: null,
                accumulator,
                now,
                cancellationToken);
        }
    }

    private async Task ApplyUpdatedAsync(
        PendingCalendarReconciliation connection,
        StudentProfileView profile,
        CalendarAccess access,
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current,
        ReconciliationAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CalendarEventMappingView? previousMapping = await MappingForUserAsync(
            connection.UserId,
            previous.SourceId,
            previous.StableIdentity,
            cancellationToken);

        bool identityChanged = !string.Equals(
            previous.StableIdentity,
            current.StableIdentity,
            StringComparison.Ordinal);
        CalendarEventMappingView? currentMapping = identityChanged
            ? await MappingForUserAsync(
                connection.UserId,
                current.SourceId,
                current.StableIdentity,
                cancellationToken)
            : previousMapping;

        if (identityChanged && previousMapping is not null && currentMapping is not null)
        {
            throw new InvalidOperationException(
                $"User '{connection.UserId}' has mappings for both sides of updated diff "
                + $"'{previous.StableIdentity}' -> '{current.StableIdentity}'.");
        }

        CalendarEventMappingView? mapping = previousMapping ?? currentMapping;
        if (mapping is null)
        {
            if (CalendarAudienceResolver.Applies(current, profile))
            {
                await InsertAsync(
                    connection,
                    access,
                    current,
                    accumulator,
                    now,
                    cancellationToken);
            }

            return;
        }

        IncrementalCalendarOperation operation = IncrementalSyncPlanner.PlanForExistingHolder(
            current,
            profile,
            mapping.ContentHash);
        switch (operation)
        {
            case IncrementalCalendarOperation.Patch:
                await PatchAsync(
                    connection.UserId,
                    access,
                    mapping,
                    current,
                    previousStableIdentity: identityChanged && previousMapping is not null
                        ? previous.StableIdentity
                        : null,
                    accumulator,
                    now,
                    cancellationToken);
                break;

            case IncrementalCalendarOperation.Delete:
                await DeleteAsync(
                    connection.UserId,
                    access,
                    mapping,
                    accumulator,
                    cancellationToken);
                break;

            case IncrementalCalendarOperation.None:
                if (identityChanged && previousMapping is not null)
                {
                    // A stable-identity move must still patch the event's time/private marker even
                    // if an old producer happened to emit the same content hash on both sides.
                    await PatchAsync(
                        connection.UserId,
                        access,
                        mapping,
                        current,
                        previous.StableIdentity,
                        accumulator,
                        now,
                        cancellationToken);
                }

                break;

            case IncrementalCalendarOperation.Insert:
            default:
                throw new InvalidOperationException(
                    $"Unexpected operation '{operation}' for an existing Calendar mapping.");
        }
    }

    private async Task ApplyDeletedAsync(
        Guid userId,
        CalendarAccess access,
        CanonicalScheduleRecord previous,
        ReconciliationAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        CalendarEventMappingView? mapping = await MappingForUserAsync(
            userId,
            previous.SourceId,
            previous.StableIdentity,
            cancellationToken);
        if (mapping is not null)
        {
            // This is the only replay path that removes a lesson absent from the next revision, and
            // it is reached solely from this persisted Deleted semantic-diff entry.
            await DeleteAsync(userId, access, mapping, accumulator, cancellationToken);
        }
    }

    private async Task InsertAsync(
        PendingCalendarReconciliation connection,
        CalendarAccess access,
        CanonicalScheduleRecord record,
        ReconciliationAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                connection.UserId,
                cancellationToken);
        ManagedCalendarEvent calendarEvent = ManagedCalendarEventFactory.ToManagedEvent(
            connection.UserId,
            record,
            colors);
        await calendarClient.InsertEventAsync(
            access,
            connection.ManagedCalendarId,
            calendarEvent,
            cancellationToken);

        UserCalendarEventMapping mapping = UserCalendarEventMapping.Create(
            connection.UserId,
            record.StableIdentity,
            record.SourceId,
            record.Id,
            connection.ManagedCalendarId,
            calendarEvent.EventId,
            record.ContentHash,
            now);
        await mappingStore.AddAsync(mapping, cancellationToken);
        accumulator.Inserted++;
    }

    private async Task PatchAsync(
        Guid userId,
        CalendarAccess access,
        CalendarEventMappingView mapping,
        CanonicalScheduleRecord current,
        string? previousStableIdentity,
        ReconciliationAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A secondary match deliberately preserves the old deterministic Google event id while its
        // private stableIdentity marker moves to the current record.
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                userId,
                cancellationToken);
        ManagedCalendarEvent calendarEvent =
            ManagedCalendarEventFactory.ToManagedEvent(userId, current, colors) with
            {
                EventId = mapping.GoogleEventId,
            };

        CalendarEventPatchOutcome outcome = await calendarClient.PatchEventAsync(
            access,
            mapping.GoogleCalendarId,
            calendarEvent,
            cancellationToken);
        if (outcome is CalendarEventPatchOutcome.NotFound)
        {
            await calendarClient.InsertEventAsync(
                access,
                mapping.GoogleCalendarId,
                calendarEvent,
                cancellationToken);
        }

        if (previousStableIdentity is not null)
        {
            await ReidentifyMappingAsync(
                userId,
                previousStableIdentity,
                current,
                now,
                cancellationToken);
        }
        else
        {
            await mappingStore.UpdateContentAsync(
                userId,
                mapping.StableIdentity,
                current.Id,
                current.ContentHash,
                now,
                cancellationToken);
        }

        accumulator.Patched++;
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

    private async Task DeleteAsync(
        Guid userId,
        CalendarAccess access,
        CalendarEventMappingView mapping,
        ReconciliationAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        await calendarClient.DeleteEventAsync(
            access,
            mapping.GoogleCalendarId,
            mapping.GoogleEventId,
            cancellationToken);
        await mappingStore.RemoveAsync(userId, mapping.StableIdentity, cancellationToken);
        accumulator.Deleted++;
    }

    private async Task<CalendarEventMappingView?> MappingForUserAsync(
        Guid userId,
        SourceId sourceId,
        string stableIdentity,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEventMappingView> mappings =
            await mappingStore.ListForStableIdentityAsync(
                sourceId,
                stableIdentity,
                cancellationToken);
        return mappings.SingleOrDefault(mapping => mapping.UserId == userId);
    }

    private async Task<IReadOnlyDictionary<Guid, CanonicalScheduleRecord>> LoadReferencedRecordsAsync(
        DispatchedDiff diff,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> ids = [];
        foreach (ScheduleDiffEntry entry in diff.Entries)
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

        IReadOnlyList<CanonicalScheduleRecord> records =
            await scheduleReadStore.ListRecordsByIdsAsync(ids, cancellationToken);
        return records.ToDictionary(record => record.Id);
    }

    private static CanonicalScheduleRecord RequiredRecord(
        Guid? recordId,
        IReadOnlyDictionary<Guid, CanonicalScheduleRecord> records,
        DispatchedDiff diff,
        string side)
    {
        if (recordId is not { } id || !records.TryGetValue(id, out CanonicalScheduleRecord? record))
        {
            throw new InvalidOperationException(
                $"Dispatched diff '{diff.DiffId}' is missing its {side} canonical record.");
        }

        return record;
    }

    private static void EnsureSource(SourceId expected, CanonicalScheduleRecord record)
    {
        if (record.SourceId != expected)
        {
            throw new InvalidOperationException(
                $"Canonical record '{record.Id}' belongs to source '{record.SourceId}', "
                + $"not replay diff source '{expected}'.");
        }
    }

    private sealed class ReconciliationAccumulator(Guid userId)
    {
        public int DiffsReplayed { get; set; }

        public int Inserted { get; set; }

        public int Patched { get; set; }

        public int Deleted { get; set; }

        public CalendarReconciliationUserResult ToResult(
            CalendarReconciliationOutcome outcome,
            string? failureReason = null) => new()
            {
                UserId = userId,
                Outcome = outcome,
                DiffsReplayed = DiffsReplayed,
                Inserted = Inserted,
                Patched = Patched,
                Deleted = Deleted,
                FailureReason = failureReason,
            };
    }
}

public sealed record CalendarReconciliationRunResult
{
    public required bool Frozen { get; init; }

    public required IReadOnlyList<CalendarReconciliationUserResult> Users { get; init; }
}

public sealed record CalendarReconciliationUserResult
{
    public required Guid UserId { get; init; }

    public required CalendarReconciliationOutcome Outcome { get; init; }

    public int DiffsReplayed { get; init; }

    public int Inserted { get; init; }

    public int Patched { get; init; }

    public int Deleted { get; init; }

    public string? FailureReason { get; init; }
}

public enum CalendarReconciliationOutcome
{
    Frozen,
    Completed,
    InProgress,
    Deferred,
    AuthorizationRequired,
    Failed,
}
