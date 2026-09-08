using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Runs the one-time initial synchronization for users who asked for it (ADR-058): creates
/// their dedicated calendar (ADR-024) and writes every currently-published event that applies
/// to their profile, resumably and idempotently.
/// </summary>
/// <remarks>
/// It is driven by connection state, not by an in-memory queue, so a worker killed mid-run
/// resumes from what is not yet mapped. Following the pipeline convention, it returns rich
/// per-user results and leaves logging to the worker.
/// </remarks>
public sealed class InitialCalendarSyncService(
    ICalendarSyncConnectionStore connectionStore,
    IStudentProfileStore profileStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IUserCalendarEventMappingStore mappingStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    IOperationalFreezeStore freezeStore,
    InitialSyncOptions options,
    TimeProvider timeProvider,
    DepartmentColorService departmentColors)
{
    /// <summary>
    /// Runs every pending connection this cycle claims, one after another, in this service's own
    /// scope. Kept as the single-scope entry point: the worker fans the same two halves out across
    /// scopes instead (ADR-157), and every test drives synchronization through here.
    /// </summary>
    public async Task<InitialCalendarSyncRunResult> RunPendingAsync(CancellationToken cancellationToken)
    {
        InitialCalendarSyncBatch batch = await ListPendingAsync(cancellationToken);
        if (batch.Frozen)
        {
            return new InitialCalendarSyncRunResult { Frozen = true, Users = [] };
        }

        List<InitialCalendarSyncResult> results = [];
        foreach (PendingCalendarSync connection in batch.Connections)
        {
            results.Add(await SyncOneAsync(connection, cancellationToken));
        }

        return new InitialCalendarSyncRunResult { Frozen = false, Users = results };
    }

    /// <summary>
    /// Reads the freeze and lists the connections this cycle should advance, without touching a
    /// calendar. Separated from <see cref="SyncOneAsync"/> so the worker can run each connection
    /// in its own scope and therefore concurrently (ADR-157); the two together are exactly what
    /// <see cref="RunPendingAsync"/> does sequentially.
    /// </summary>
    public async Task<InitialCalendarSyncBatch> ListPendingAsync(CancellationToken cancellationToken)
    {
        // Every calendar-touching job reads the same authoritative switch and fails closed
        // (ADR-034/043): while frozen, no calendar is created and no event is written.
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new InitialCalendarSyncBatch { Frozen = true, Connections = [] };
        }

        IReadOnlyList<PendingCalendarSync> pending =
            await connectionStore.ListPendingInitialSyncAsync(
                options.ConnectionBatchSize,
                cancellationToken);

        return new InitialCalendarSyncBatch { Frozen = false, Connections = pending };
    }

    /// <summary>
    /// Advances one connection: creates its calendar if needed and writes this cycle's budget of
    /// events. Safe to run concurrently with other connections only when each call has its own
    /// scope, because the stores it writes through are not thread-safe.
    /// </summary>
    public async Task<InitialCalendarSyncResult> SyncOneAsync(
        PendingCalendarSync connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DateTimeOffset now = timeProvider.GetUtcNow();

        try
        {
            StudentProfileView? profile =
                await profileStore.GetByUserIdAsync(connection.UserId, cancellationToken);
            if (profile is null)
            {
                // Onboarding order guarantees a profile before authorization, so this is a
                // data anomaly rather than a normal case; report it and leave the connection
                // in progress rather than completing an empty calendar.
                return new InitialCalendarSyncResult
                {
                    UserId = connection.UserId,
                    Outcome = InitialCalendarSyncOutcome.ProfileMissing,
                };
            }

            if (await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = profile.ClassYear,
                    ProgramLanguage = profile.ProgramLanguage,
                },
                cancellationToken))
            {
                return new InitialCalendarSyncResult
                {
                    UserId = connection.UserId,
                    Outcome = InitialCalendarSyncOutcome.Frozen,
                };
            }

            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(connection.ProtectedRefreshToken),
            };

            string calendarId = await EnsureCalendarAsync(connection, access, now, cancellationToken);

            IReadOnlyList<CanonicalScheduleRecord> published =
                await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                    profile.AcademicYear,
                    profile.ClassYear,
                    profile.ProgramLanguage,
                    cancellationToken);

            List<CanonicalScheduleRecord> applicable =
                [.. published.Where(record => CalendarAudienceResolver.Applies(record, profile))];

            IReadOnlySet<string> alreadyWritten =
                await mappingStore.ListStableIdentitiesForUserAsync(
                    connection.UserId,
                    cancellationToken);

            // The pass is planned before anything is written, because the writes below run
            // concurrently and a running counter could no longer decide the budget cut-off.
            HashSet<string> handledThisPass = new(StringComparer.Ordinal);
            List<CanonicalScheduleRecord> unwritten = [];
            foreach (CanonicalScheduleRecord record in applicable)
            {
                if (alreadyWritten.Contains(record.StableIdentity)
                    || !handledThisPass.Add(record.StableIdentity))
                {
                    continue;
                }

                unwritten.Add(record);
            }

            bool deferredRemainder = unwritten.Count > options.EventsPerConnectionPerCycle;
            List<CanonicalScheduleRecord> thisPass = deferredRemainder
                ? unwritten[..options.EventsPerConnectionPerCycle]
                : unwritten;

            // Resolved once for the user rather than once per event: the colors are the same for
            // every event of one calendar, and the service's cache is a plain dictionary that the
            // concurrent writes below must not race on.
            IReadOnlyDictionary<string, string> colors =
                await DepartmentColorPaletteResolver.GetAsync(
                    departmentColors,
                    connection.UserId,
                    cancellationToken);

            // One user's events are written concurrently (ADR-157). Each insert is idempotent on
            // the deterministic event id, so ordering carries no meaning and a partially written
            // pass resumes from the ledger exactly as a sequential one did. The ledger writes
            // themselves are serialized: the mapping store is a scoped DbContext.
            using SemaphoreSlim ledgerGate = new(1, 1);
            int written = 0;
            await Parallel.ForEachAsync(
                thisPass,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.EventWriteConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (record, token) =>
                {
                    await WriteEventAsync(
                        connection.UserId,
                        calendarId,
                        access,
                        record,
                        colors,
                        now,
                        ledgerGate,
                        token);
                    Interlocked.Increment(ref written);
                });

            if (deferredRemainder)
            {
                return new InitialCalendarSyncResult
                {
                    UserId = connection.UserId,
                    Outcome = InitialCalendarSyncOutcome.InProgress,
                    EventsWritten = written,
                    ApplicableRecordCount = applicable.Count,
                };
            }

            // Nothing applicable remains unwritten, so the user's calendar is fully populated.
            await connectionStore.MarkInitialSyncCompletedAsync(connection.UserId, now, cancellationToken);
            return new InitialCalendarSyncResult
            {
                UserId = connection.UserId,
                Outcome = InitialCalendarSyncOutcome.Completed,
                EventsWritten = written,
                ApplicableRecordCount = applicable.Count,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (Unwrap<GoogleCalendarCredentialException>(exception) is not null)
        {
            // Concurrent writes surface a revoked grant wrapped in an AggregateException, so the
            // taxonomy is matched through the wrapper rather than only on the thrown type.
            await connectionStore.MarkNeedsReauthorizationAsync(
                connection.UserId,
                now,
                cancellationToken);
            return new InitialCalendarSyncResult
            {
                UserId = connection.UserId,
                Outcome = InitialCalendarSyncOutcome.AuthorizationRequired,
                FailureReason =
                    Unwrap<GoogleCalendarCredentialException>(exception)!.Message,
            };
        }
        catch (Exception exception)
        {
            // One user's failure (a bad token, a Calendar error) must not stop the others. The
            // connection stays in progress and is retried next cycle; the worker logs this.
            return new InitialCalendarSyncResult
            {
                UserId = connection.UserId,
                Outcome = InitialCalendarSyncOutcome.Failed,
                FailureReason = exception.Message,
            };
        }
    }

    private async Task<string> EnsureCalendarAsync(
        PendingCalendarSync connection,
        CalendarAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (connection.ManagedCalendarId is { Length: > 0 } existing)
        {
            return existing;
        }

        string descriptionMarker = ManagedCalendarIdentity.DescriptionMarker(connection.UserId);
        IReadOnlyList<string> existingCalendars = await calendarClient.FindManagedCalendarIdsAsync(
            access,
            descriptionMarker,
            cancellationToken);
        if (existingCalendars.Count > 1)
        {
            throw new GoogleCalendarSyncException(
                $"Found {existingCalendars.Count} app-created calendars carrying the user's "
                + "managed-calendar marker; automatic attachment is unsafe.");
        }

        if (existingCalendars.Count == 1)
        {
            string recovered = existingCalendars[0];
            await connectionStore.AttachManagedCalendarAsync(
                connection.UserId,
                recovered,
                now,
                cancellationToken);
            return recovered;
        }

        string calendarId = await calendarClient.CreateManagedCalendarAsync(
            access,
            options.CalendarSummary,
            options.CalendarTimeZoneId,
            descriptionMarker,
            cancellationToken);

        // Persisted immediately so a crash after this point resumes with the calendar attached
        // rather than creating a second one.
        await connectionStore.AttachManagedCalendarAsync(
            connection.UserId,
            calendarId,
            now,
            cancellationToken);
        return calendarId;
    }

    private async Task WriteEventAsync(
        Guid userId,
        string calendarId,
        CalendarAccess access,
        CanonicalScheduleRecord record,
        IReadOnlyDictionary<string, string> colors,
        DateTimeOffset now,
        SemaphoreSlim ledgerGate,
        CancellationToken cancellationToken)
    {
        ManagedCalendarEvent calendarEvent =
            ManagedCalendarEventFactory.ToManagedEvent(userId, record, colors);

        // The insert is idempotent on the deterministic event id, so a re-run after a crash
        // between the insert and the mapping write reports AlreadyExists rather than duplicating.
        // Either way the event now exists, so the mapping is recorded.
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

        // The Google write above is the concurrent part; this one is not. A scoped DbContext
        // rejects concurrent use, and the ledger row is milliseconds of work next to the round
        // trip it records, so serializing it costs the pass nothing.
        await ledgerGate.WaitAsync(cancellationToken);
        try
        {
            await mappingStore.AddAsync(mapping, cancellationToken);
        }
        finally
        {
            ledgerGate.Release();
        }
    }

    /// <summary>
    /// Finds <typeparamref name="TException"/> in a thrown exception, looking through the
    /// <see cref="AggregateException"/> a concurrent pass wraps its failures in.
    /// </summary>
    private static TException? Unwrap<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is TException match)
        {
            return match;
        }

        return exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.OfType<TException>().FirstOrDefault()
            : null;
    }
}

/// <summary>
/// What one cycle should advance: the connections listed for it, or nothing because the global
/// operational freeze is active. Carries the ciphertext credential the sync path needs, exactly as
/// <see cref="PendingCalendarSync"/> always has (ADR-058), and never leaves the backend.
/// </summary>
public sealed record InitialCalendarSyncBatch
{
    /// <summary>Whether the freeze is active, so no connection may be advanced at all.</summary>
    public required bool Frozen { get; init; }

    public required IReadOnlyList<PendingCalendarSync> Connections { get; init; }
}

public sealed record InitialCalendarSyncRunResult
{
    /// <summary>Whether the run did nothing because the global operational freeze is active.</summary>
    public required bool Frozen { get; init; }

    public required IReadOnlyList<InitialCalendarSyncResult> Users { get; init; }
}

public sealed record InitialCalendarSyncResult
{
    public required Guid UserId { get; init; }

    public required InitialCalendarSyncOutcome Outcome { get; init; }

    /// <summary>How many events were written this cycle.</summary>
    public int EventsWritten { get; init; }

    /// <summary>How many published records apply to the user in total.</summary>
    public int ApplicableRecordCount { get; init; }

    /// <summary>Why this user's sync failed, when it did.</summary>
    public string? FailureReason { get; init; }
}

public enum InitialCalendarSyncOutcome
{
    /// <summary>The user's class/program pipeline is frozen; its durable work remains pending.</summary>
    Frozen,

    /// <summary>Every applicable event is now written; the connection is marked complete.</summary>
    Completed,

    /// <summary>More events remain for a following cycle.</summary>
    InProgress,

    /// <summary>The user has no profile, so nothing could be resolved (a data anomaly).</summary>
    ProfileMissing,

    /// <summary>A Calendar or credential error stopped this user; it retries next cycle.</summary>
    Failed,

    /// <summary>The stored grant lacks a required scope or was revoked; consent must run again.</summary>
    AuthorizationRequired,
}
