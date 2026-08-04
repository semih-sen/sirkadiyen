using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Periodically compares current published truth, the local event ledger, and the actual
/// Sirkadiyen-marked Google events. It repairs missing/stale expected events and missing
/// ledger rows, but never deletes from level-triggered absence or duplicate detection.
/// </summary>
public sealed class CalendarInventoryReconciliationService(
    ICalendarSyncTargetReadStore targetStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IUserCalendarEventMappingStore mappingStore,
    IGoogleCalendarConnectionStore connectionStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    CalendarInventoryReconciliationOptions options,
    TimeProvider timeProvider,
    DepartmentColorService departmentColors)
{
    public async Task<CalendarInventoryRunResult> RunDueAsync(
        CancellationToken cancellationToken)
    {
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new CalendarInventoryRunResult { Frozen = true, Users = [] };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<CalendarInventoryTarget> targets =
            await targetStore.ListInventoryTargetsAsync(
                now - options.Interval,
                options.ConnectionBatchSize,
                cancellationToken);

        List<CalendarInventoryUserResult> results = [];
        foreach (CalendarInventoryTarget target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReconcileOneAsync(target, now, cancellationToken));
        }

        return new CalendarInventoryRunResult { Frozen = false, Users = results };
    }

    private async Task<CalendarInventoryUserResult> ReconcileOneAsync(
        CalendarInventoryTarget target,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        InventoryAccumulator accumulator = new(target.UserId);
        try
        {
            if (await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = target.Profile.ClassYear,
                    ProgramLanguage = target.Profile.ProgramLanguage,
                },
                cancellationToken))
            {
                return accumulator.ToResult(CalendarInventoryOutcome.Frozen);
            }

            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken),
            };

            IReadOnlyList<CanonicalScheduleRecord> published =
                await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                    target.Profile.AcademicYear,
                    target.Profile.ClassYear,
                    target.Profile.ProgramLanguage,
                    cancellationToken);
            List<CanonicalScheduleRecord> expected =
                [.. published.Where(record =>
                    CalendarAudienceResolver.Applies(record, target.Profile))];

            Dictionary<string, CanonicalScheduleRecord> expectedByIdentity =
                ToUniqueExpected(expected);
            IReadOnlyList<CalendarEventMappingView> mappings =
                await mappingStore.ListForUserAsync(target.UserId, cancellationToken);
            Dictionary<string, CalendarEventMappingView> mappingByIdentity =
                mappings.ToDictionary(mapping => mapping.StableIdentity, StringComparer.Ordinal);

            IReadOnlyList<ManagedCalendarEventSnapshot> actual =
                await calendarClient.ListManagedEventsAsync(
                    access,
                    target.ManagedCalendarId,
                    cancellationToken);
            Dictionary<string, List<ManagedCalendarEventSnapshot>> actualByIdentity =
                GroupActual(actual, accumulator);

            foreach ((string stableIdentity, CanonicalScheduleRecord record)
                in expectedByIdentity.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                mappingByIdentity.TryGetValue(stableIdentity, out CalendarEventMappingView? mapping);
                actualByIdentity.TryGetValue(
                    stableIdentity,
                    out List<ManagedCalendarEventSnapshot>? candidates);
                candidates ??= [];

                List<ManagedCalendarEventSnapshot> sameSource =
                    [.. candidates.Where(candidate => HasSource(candidate, record.SourceId.Value))];
                accumulator.UnexpectedEvents += candidates.Count - sameSource.Count;

                await RepairExpectedAsync(
                    target,
                    access,
                    record,
                    mapping,
                    sameSource,
                    accumulator,
                    now,
                    cancellationToken);
            }

            accumulator.UnexpectedMappings = mappingByIdentity.Keys.Count(identity =>
                !expectedByIdentity.ContainsKey(identity));
            accumulator.UnexpectedEvents += actualByIdentity
                .Where(pair => !expectedByIdentity.ContainsKey(pair.Key))
                .Sum(pair => pair.Value.Count);

            // Conflicts and unexpected stale rows are observations, not deletion authority.
            // The scan is complete and therefore scheduled normally rather than hammered on
            // every worker cycle.
            await connectionStore.MarkCalendarInventoryCompletedAsync(
                target.UserId,
                now,
                cancellationToken);
            return accumulator.ToResult(
                accumulator.Conflicts == 0
                    ? CalendarInventoryOutcome.Completed
                    : CalendarInventoryOutcome.CompletedWithConflicts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleManagedCalendarUnavailableException exception)
        {
            await connectionStore.MarkManagedCalendarUnavailableAsync(
                target.UserId,
                now,
                cancellationToken);
            return accumulator.ToResult(
                CalendarInventoryOutcome.CalendarRepairRequired,
                exception.Message);
        }
        catch (GoogleCalendarCredentialException exception)
        {
            await connectionStore.MarkNeedsReauthorizationAsync(
                target.UserId,
                now,
                cancellationToken);
            return accumulator.ToResult(
                CalendarInventoryOutcome.AuthorizationRequired,
                exception.Message);
        }
        catch (GoogleCalendarTransientException exception)
        {
            return accumulator.ToResult(CalendarInventoryOutcome.Deferred, exception.Message);
        }
        catch (Exception exception)
        {
            return accumulator.ToResult(CalendarInventoryOutcome.Failed, exception.Message);
        }
    }

    private async Task RepairExpectedAsync(
        CalendarInventoryTarget target,
        CalendarAccess access,
        CanonicalScheduleRecord record,
        CalendarEventMappingView? mapping,
        IReadOnlyList<ManagedCalendarEventSnapshot> actual,
        InventoryAccumulator accumulator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (mapping is not null
            && !string.Equals(
                mapping.GoogleCalendarId,
                target.ManagedCalendarId,
                StringComparison.Ordinal))
        {
            accumulator.Conflicts++;
            return;
        }

        ManagedCalendarEventSnapshot? selected = null;
        if (mapping is not null)
        {
            selected = actual.SingleOrDefault(candidate =>
                string.Equals(candidate.EventId, mapping.GoogleEventId, StringComparison.Ordinal));
            if (actual.Count > 1)
            {
                accumulator.Conflicts += actual.Count - 1;
            }

            if (selected is null && actual.Count > 0)
            {
                // The ledger and Google each point at a different event for the same lesson.
                // Creating another or selecting one would deepen the ambiguity.
                accumulator.Conflicts++;
                return;
            }
        }
        else
        {
            if (actual.Count > 1)
            {
                accumulator.Conflicts += actual.Count;
                return;
            }

            selected = actual.SingleOrDefault();
        }

        string eventId = mapping?.GoogleEventId
            ?? selected?.EventId
            ?? ManagedCalendarEventFactory.DeterministicEventId(
                target.UserId,
                record.StableIdentity);
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                target.UserId,
                cancellationToken);
        ManagedCalendarEvent desired =
            ManagedCalendarEventFactory.ToManagedEvent(target.UserId, record, colors) with
            {
                EventId = eventId,
            };
        await calendarClient.EnsureEventLabelAsync(
            access,
            target.ManagedCalendarId,
            desired.Label,
            cancellationToken);

        if (selected is null)
        {
            CalendarEventPatchOutcome patch = mapping is null
                ? CalendarEventPatchOutcome.NotFound
                : await calendarClient.PatchEventAsync(
                    access,
                    target.ManagedCalendarId,
                    desired,
                    cancellationToken);
            if (patch is CalendarEventPatchOutcome.NotFound)
            {
                await calendarClient.InsertEventAsync(
                    access,
                    target.ManagedCalendarId,
                    desired,
                    cancellationToken);
                accumulator.Inserted++;
            }
            else
            {
                accumulator.Patched++;
            }
        }
        else if (!ManagedCalendarEventComparer.IsEquivalent(desired, selected))
        {
            CalendarEventPatchOutcome patch = await calendarClient.PatchEventAsync(
                access,
                target.ManagedCalendarId,
                desired,
                cancellationToken);
            if (patch is CalendarEventPatchOutcome.NotFound)
            {
                await calendarClient.InsertEventAsync(
                    access,
                    target.ManagedCalendarId,
                    desired,
                    cancellationToken);
                accumulator.Inserted++;
            }
            else
            {
                accumulator.Patched++;
            }
        }

        if (mapping is null)
        {
            UserCalendarEventMapping recovered = UserCalendarEventMapping.Create(
                target.UserId,
                record.StableIdentity,
                record.SourceId,
                record.Id,
                target.ManagedCalendarId,
                eventId,
                record.ContentHash,
                now);
            await mappingStore.AddAsync(recovered, cancellationToken);
            accumulator.MappingsRecovered++;
        }
        else if (!string.Equals(mapping.ContentHash, record.ContentHash, StringComparison.Ordinal)
            || mapping.CanonicalRecordId != record.Id)
        {
            await mappingStore.UpdateContentAsync(
                target.UserId,
                record.StableIdentity,
                record.Id,
                record.ContentHash,
                now,
                cancellationToken);
            accumulator.LedgerRowsUpdated++;
        }
    }

    private static Dictionary<string, CanonicalScheduleRecord> ToUniqueExpected(
        IEnumerable<CanonicalScheduleRecord> records)
    {
        Dictionary<string, CanonicalScheduleRecord> result = new(StringComparer.Ordinal);
        foreach (CanonicalScheduleRecord record in records)
        {
            if (!result.TryAdd(record.StableIdentity, record))
            {
                throw new InvalidOperationException(
                    $"Current published truth contains duplicate stable identity "
                    + $"'{record.StableIdentity}' for inventory reconciliation.");
            }
        }

        return result;
    }

    private static Dictionary<string, List<ManagedCalendarEventSnapshot>> GroupActual(
        IEnumerable<ManagedCalendarEventSnapshot> events,
        InventoryAccumulator accumulator)
    {
        Dictionary<string, List<ManagedCalendarEventSnapshot>> result =
            new(StringComparer.Ordinal);
        foreach (ManagedCalendarEventSnapshot calendarEvent in events)
        {
            if (!calendarEvent.PrivateProperties.TryGetValue(
                    "stableIdentity",
                    out string? stableIdentity)
                || string.IsNullOrWhiteSpace(stableIdentity))
            {
                accumulator.Conflicts++;
                accumulator.UnexpectedEvents++;
                continue;
            }

            if (!result.TryGetValue(stableIdentity, out List<ManagedCalendarEventSnapshot>? group))
            {
                group = [];
                result.Add(stableIdentity, group);
            }

            group.Add(calendarEvent);
        }

        return result;
    }

    private static bool HasSource(
        ManagedCalendarEventSnapshot calendarEvent,
        string sourceId) =>
        calendarEvent.PrivateProperties.TryGetValue("sourceId", out string? actual)
        && string.Equals(sourceId, actual, StringComparison.Ordinal);

    private sealed class InventoryAccumulator(Guid userId)
    {
        public Guid UserId { get; } = userId;

        public int Inserted { get; set; }

        public int Patched { get; set; }

        public int MappingsRecovered { get; set; }

        public int LedgerRowsUpdated { get; set; }

        public int Conflicts { get; set; }

        public int UnexpectedMappings { get; set; }

        public int UnexpectedEvents { get; set; }

        public CalendarInventoryUserResult ToResult(
            CalendarInventoryOutcome outcome,
            string? failureReason = null) => new()
            {
                UserId = UserId,
                Outcome = outcome,
                Inserted = Inserted,
                Patched = Patched,
                MappingsRecovered = MappingsRecovered,
                LedgerRowsUpdated = LedgerRowsUpdated,
                Conflicts = Conflicts,
                UnexpectedMappings = UnexpectedMappings,
                UnexpectedEvents = UnexpectedEvents,
                FailureReason = failureReason,
            };
    }
}

public sealed record CalendarInventoryRunResult
{
    public required bool Frozen { get; init; }

    public required IReadOnlyList<CalendarInventoryUserResult> Users { get; init; }
}

public sealed record CalendarInventoryUserResult
{
    public required Guid UserId { get; init; }

    public required CalendarInventoryOutcome Outcome { get; init; }

    public int Inserted { get; init; }

    public int Patched { get; init; }

    public int MappingsRecovered { get; init; }

    public int LedgerRowsUpdated { get; init; }

    public int Conflicts { get; init; }

    public int UnexpectedMappings { get; init; }

    public int UnexpectedEvents { get; init; }

    public string? FailureReason { get; init; }
}

public enum CalendarInventoryOutcome
{
    Frozen,
    Completed,
    CompletedWithConflicts,
    Deferred,
    AuthorizationRequired,
    CalendarRepairRequired,
    Failed,
}
