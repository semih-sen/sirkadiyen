using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Converges a student's calendar onto their changed academic profile (ADR-096).
/// </summary>
/// <remarks>
/// Nothing else does this. Initial sync runs once, diff dispatch is edge-triggered by a published
/// revision, and inventory is deliberately non-destructive — so a student who corrects their
/// practice or anatomy group would otherwise keep the old cohort's events forever and receive the
/// new cohort's only by accident.
/// <para>
/// It is driven by durable connection state rather than an in-memory queue, bounded per cycle, and
/// resumable: the mapping ledger is the progress record, so a pass that is killed halfway simply
/// replans the remaining difference. Following the pipeline convention it returns rich per-user
/// results and leaves logging to the worker.
/// </para>
/// </remarks>
public sealed class ProfileChangeResyncService(
    ICalendarSyncConnectionStore connectionStore,
    IStudentProfileStore profileStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IUserCalendarEventMappingStore mappingStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    ProfileResyncOptions options,
    TimeProvider timeProvider,
    DepartmentColorService departmentColors)
{
    public async Task<ProfileResyncRunResult> RunPendingAsync(CancellationToken cancellationToken)
    {
        // Every calendar-touching job reads the same authoritative switch and fails closed
        // (ADR-034/043): while frozen, nothing is written and every request stays pending.
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new ProfileResyncRunResult { Frozen = true, Users = [] };
        }

        IReadOnlyList<PendingProfileResync> pending =
            await connectionStore.ListPendingProfileResyncAsync(
                options.ConnectionBatchSize,
                cancellationToken);

        List<ProfileResyncResult> results = [];
        foreach (PendingProfileResync connection in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ResyncOneAsync(connection, cancellationToken));
        }

        return new ProfileResyncRunResult { Frozen = false, Users = results };
    }

    private async Task<ProfileResyncResult> ResyncOneAsync(
        PendingProfileResync connection,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        try
        {
            StudentProfileView? profile =
                await profileStore.GetByUserIdAsync(connection.UserId, cancellationToken);
            if (profile is null)
            {
                // The request is only ever created by a profile write, so its absence is a data
                // anomaly. Report it and leave the request pending rather than converging a
                // calendar onto an audience nothing can resolve.
                return new ProfileResyncResult
                {
                    UserId = connection.UserId,
                    Outcome = ProfileResyncOutcome.ProfileMissing,
                };
            }

            // Scoped by the profile as it stands now: freezing a class/program must stop work for
            // the students who are in it after the change, which is the audience being written.
            if (await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = profile.ClassYear,
                    ProgramLanguage = profile.ProgramLanguage,
                },
                cancellationToken))
            {
                return new ProfileResyncResult
                {
                    UserId = connection.UserId,
                    Outcome = ProfileResyncOutcome.Frozen,
                };
            }

            ProfileResyncPlan plan = await PlanAsync(connection.UserId, profile, cancellationToken);

            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(connection.ProtectedRefreshToken),
            };

            int budget = options.CalendarOperationsPerConnectionPerCycle;
            int removed = 0;
            int written = 0;

            // Removals run first. A student who has just corrected their group is looking at
            // lessons that are not theirs, and clearing those is the half of the change they
            // notice; the additions arrive on this cycle too unless the budget runs out.
            foreach (CalendarEventMappingView stale in plan.Removals)
            {
                if (removed + written >= budget)
                {
                    return Partial(connection.UserId, plan, removed, written);
                }

                await calendarClient.DeleteEventAsync(
                    access,
                    connection.ManagedCalendarId,
                    stale.GoogleEventId,
                    cancellationToken);
                await mappingStore.RemoveAsync(
                    connection.UserId,
                    stale.StableIdentity,
                    cancellationToken);
                removed++;
            }

            foreach (CanonicalScheduleRecord record in plan.Additions)
            {
                if (removed + written >= budget)
                {
                    return Partial(connection.UserId, plan, removed, written);
                }

                await WriteEventAsync(
                    connection.UserId,
                    connection.ManagedCalendarId,
                    access,
                    record,
                    now,
                    cancellationToken);
                written++;
            }

            // The pass converged everything it planned, so the request may be cleared — unless the
            // profile changed again while it ran, in which case the newer request stays pending.
            CompleteProfileResyncOutcome completion =
                await connectionStore.CompleteProfileResyncAsync(
                    connection.UserId,
                    connection.RequiredSinceUtc,
                    now,
                    cancellationToken);

            return new ProfileResyncResult
            {
                UserId = connection.UserId,
                Outcome = completion is CompleteProfileResyncOutcome.Completed
                    ? ProfileResyncOutcome.Completed
                    : ProfileResyncOutcome.Superseded,
                EventsRemoved = removed,
                EventsWritten = written,
                ApplicableRecordCount = plan.ApplicableCount,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleManagedCalendarUnavailableException exception)
        {
            await connectionStore.MarkManagedCalendarUnavailableAsync(
                connection.UserId,
                now,
                cancellationToken);
            return new ProfileResyncResult
            {
                UserId = connection.UserId,
                Outcome = ProfileResyncOutcome.CalendarRepairRequired,
                FailureReason = exception.Message,
            };
        }
        catch (GoogleCalendarCredentialException exception)
        {
            // A dead credential is not a failure of the request: the request survives, the user is
            // skipped, and what they already hold is left exactly as it is (ADR-059).
            await connectionStore.MarkNeedsReauthorizationAsync(
                connection.UserId,
                now,
                cancellationToken);
            return new ProfileResyncResult
            {
                UserId = connection.UserId,
                Outcome = ProfileResyncOutcome.AuthorizationRequired,
                FailureReason = exception.Message,
            };
        }
        catch (Exception exception)
        {
            // One user's failure must not stop the others. The request stays pending and the next
            // cycle replans it against the converged ledger; the worker logs this.
            return new ProfileResyncResult
            {
                UserId = connection.UserId,
                Outcome = ProfileResyncOutcome.Failed,
                FailureReason = exception.Message,
            };
        }
    }

    /// <summary>
    /// Works out what this user's calendar is missing and what it holds that is no longer theirs.
    /// </summary>
    /// <remarks>
    /// The deletion half is deliberately bounded by publication. A ledger row is stale only when
    /// its lesson is <em>still currently published</em> and the audience rule says it is not this
    /// student's; a stable identity that is absent from published truth is left alone, because
    /// removing it would be deleting from absence rather than from a published decision
    /// (AI_GUIDELINE §13). Retiring such a lesson remains the semantic diff's job.
    /// </remarks>
    private async Task<ProfileResyncPlan> PlanAsync(
        Guid userId,
        StudentProfileView profile,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CanonicalScheduleRecord> published =
            await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                profile.AcademicYear,
                profile.ClassYear,
                profile.ProgramLanguage,
                cancellationToken);

        Dictionary<string, CanonicalScheduleRecord> applicable = new(StringComparer.Ordinal);
        foreach (CanonicalScheduleRecord record in published)
        {
            if (CalendarAudienceResolver.Applies(record, profile))
            {
                applicable.TryAdd(record.StableIdentity, record);
            }
        }

        IReadOnlyList<CalendarEventMappingView> held =
            await mappingStore.ListForUserAsync(userId, cancellationToken);

        IReadOnlyList<PublishedRecordIdentity> live =
            await scheduleReadStore.ListCurrentPublishedIdentitiesAsync(
                profile.AcademicYear,
                cancellationToken);
        HashSet<(string Source, string Identity)> liveIdentities =
        [
            .. live.Select(identity => (identity.SourceId.Value, identity.StableIdentity)),
        ];

        HashSet<string> heldIdentities =
            [.. held.Select(mapping => mapping.StableIdentity)];

        return new ProfileResyncPlan
        {
            Removals =
            [
                .. held.Where(mapping =>
                    !applicable.ContainsKey(mapping.StableIdentity)
                    && liveIdentities.Contains(
                        (mapping.SourceId.Value, mapping.StableIdentity))),
            ],
            Additions =
            [
                .. applicable
                    .Where(entry => !heldIdentities.Contains(entry.Key))
                    .Select(entry => entry.Value),
            ],
            ApplicableCount = applicable.Count,
        };
    }

    private async Task WriteEventAsync(
        Guid userId,
        string calendarId,
        CalendarAccess access,
        CanonicalScheduleRecord record,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> colors =
            await DepartmentColorPaletteResolver.GetAsync(
                departmentColors,
                userId,
                cancellationToken);
        ManagedCalendarEvent calendarEvent =
            ManagedCalendarEventFactory.ToManagedEvent(userId, record, colors);

        // The insert is idempotent on the deterministic event id, so a re-run after a crash
        // between the insert and the mapping write reports AlreadyExists rather than duplicating.
        await calendarClient.InsertEventAsync(access, calendarId, calendarEvent, cancellationToken);

        UserCalendarEventMapping mapping = UserCalendarEventMapping.Create(
            userId,
            record.StableIdentity,
            record.SourceId,
            record.Id,
            calendarId,
            calendarEvent.EventId,
            record.ContentHash,
            now);
        await mappingStore.AddAsync(mapping, cancellationToken);
    }

    private static ProfileResyncResult Partial(
        Guid userId,
        ProfileResyncPlan plan,
        int removed,
        int written) =>
        new()
        {
            UserId = userId,
            Outcome = ProfileResyncOutcome.InProgress,
            EventsRemoved = removed,
            EventsWritten = written,
            ApplicableRecordCount = plan.ApplicableCount,
        };

    private sealed record ProfileResyncPlan
    {
        public required IReadOnlyList<CalendarEventMappingView> Removals { get; init; }

        public required IReadOnlyList<CanonicalScheduleRecord> Additions { get; init; }

        public required int ApplicableCount { get; init; }
    }
}

public sealed record ProfileResyncRunResult
{
    /// <summary>Whether the run did nothing because the global operational freeze is active.</summary>
    public required bool Frozen { get; init; }

    public required IReadOnlyList<ProfileResyncResult> Users { get; init; }
}

public sealed record ProfileResyncResult
{
    public required Guid UserId { get; init; }

    public required ProfileResyncOutcome Outcome { get; init; }

    /// <summary>Events removed this cycle because they no longer apply to the student.</summary>
    public int EventsRemoved { get; init; }

    /// <summary>Events written this cycle because they now apply to the student.</summary>
    public int EventsWritten { get; init; }

    /// <summary>How many published records apply to the student under the new profile.</summary>
    public int ApplicableRecordCount { get; init; }

    public string? FailureReason { get; init; }
}

public enum ProfileResyncOutcome
{
    /// <summary>The student's class/program pipeline is frozen; the request remains pending.</summary>
    Frozen,

    /// <summary>The calendar now matches the profile, and the request was cleared.</summary>
    Completed,

    /// <summary>
    /// The calendar was converged, but the profile changed again while the pass ran, so the newer
    /// request was left pending for the next cycle.
    /// </summary>
    Superseded,

    /// <summary>The per-cycle budget was reached; the request remains pending, without back-off.</summary>
    InProgress,

    /// <summary>The user has no profile, so nothing could be resolved (a data anomaly).</summary>
    ProfileMissing,

    /// <summary>The stored grant was revoked or expired; the request survives re-authorization.</summary>
    AuthorizationRequired,

    /// <summary>The managed calendar itself is gone; recreation is never automatic.</summary>
    CalendarRepairRequired,

    /// <summary>A Calendar error stopped this user; the request is retried next cycle.</summary>
    Failed,
}
