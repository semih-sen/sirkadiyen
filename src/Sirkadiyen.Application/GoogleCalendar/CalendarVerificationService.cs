using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Verifies one user's managed calendar directly against Google, on demand (ADR-121). Unlike the
/// admin calendar view — which reads the local mapping ledger, i.e. what Sirkadiyen recorded writing —
/// this reads the actual Google events and compares them with both the ledger and current published
/// truth, so an operator can answer "is what we think on this calendar really there?".
/// </summary>
/// <remarks>
/// It mirrors <see cref="CalendarInventoryReconciliationService"/>'s Google read and classification but
/// is strictly read-only: it never writes to Google, never touches the ledger, and never changes the
/// connection's health state. A dead credential or an unavailable calendar is reported, not recorded —
/// recording those is the worker's job, and a verification must be safe to run at any time. The
/// credential is unprotected only in memory for the single read, exactly as the inventory service does.
/// </remarks>
public sealed class CalendarVerificationService(
    IGoogleCalendarConnectionReader connectionReader,
    ICalendarSyncTargetReadStore targetStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IUserCalendarEventMappingStore mappingStore,
    IUserCalendarClient calendarClient,
    ICalendarTokenProtector tokenProtector,
    DepartmentColorService departmentColors,
    TimeProvider timeProvider)
{
    /// <summary>Bounds the sampled difference list returned to the UI; the counts carry the totals.</summary>
    public const int MaxReportItems = 200;

    public async Task<CalendarVerificationResult> VerifyAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnectionView? connection =
            await connectionReader.GetByUserIdAsync(userId, cancellationToken);

        if (connection is null)
        {
            return Result(CalendarVerificationOutcome.NoConnection,
                "Bu hesabın bir Google Takvim bağlantısı yok.");
        }

        if (connection.Status is GoogleCalendarConnectionStatus.NeedsReauthorization)
        {
            return Result(CalendarVerificationOutcome.NeedsReauthorization,
                "Google, saklanan yetkiyi reddediyor; takvim yeniden yetkilendirilene kadar okunamaz.");
        }

        if (connection.ManagedCalendarId is null)
        {
            return Result(CalendarVerificationOutcome.NoManagedCalendar,
                "İlk senkronizasyon henüz bu kullanıcının özel takvimini oluşturmadı.");
        }

        // The per-user target carries the profile (for audience), the managed calendar id, and the
        // encrypted credential — and it is only returned for an authorized, initial-sync-completed,
        // actively licensed connection, which is exactly the precondition a live read needs.
        IReadOnlyList<CalendarSyncTarget> targets =
            await targetStore.ListTargetsByUserIdsAsync([userId], cancellationToken);
        CalendarSyncTarget? target = targets.Count == 1 ? targets[0] : null;
        if (target is null)
        {
            return Result(CalendarVerificationOutcome.NotSyncReady,
                "Takvim doğrulaması, yetkili, ilk senkronizasyonu tamamlanmış ve etkin lisanslı bir "
                    + "bağlantı gerektirir.");
        }

        IReadOnlyList<ManagedCalendarEventSnapshot> actual;
        try
        {
            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(target.ProtectedRefreshToken),
            };
            actual = await calendarClient.ListManagedEventsAsync(
                access,
                target.ManagedCalendarId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleManagedCalendarUnavailableException exception)
        {
            return Result(CalendarVerificationOutcome.CalendarUnavailable, exception.Message);
        }
        catch (GoogleCalendarCredentialException exception)
        {
            return Result(CalendarVerificationOutcome.NeedsReauthorization, exception.Message);
        }
        catch (GoogleCalendarTransientException exception)
        {
            return Result(CalendarVerificationOutcome.TransientError, exception.Message);
        }

        IReadOnlyList<CanonicalScheduleRecord> published =
            await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                target.Profile.AcademicYear,
                target.Profile.ClassYear,
                target.Profile.ProgramLanguage,
                cancellationToken);
        List<CanonicalScheduleRecord> expected =
            [.. published.Where(record => CalendarAudienceResolver.Applies(record, target.Profile))];

        IReadOnlyList<CalendarEventMappingView> ledger =
            await mappingStore.ListForUserAsync(userId, cancellationToken);

        IReadOnlyDictionary<string, string> colors =
            await departmentColors.GetEffectiveColorsAsync(userId, cancellationToken);

        CalendarVerificationReport report = CalendarVerificationComparer.Compare(
            userId,
            target.ManagedCalendarId,
            timeProvider.GetUtcNow(),
            expected,
            ledger,
            actual,
            colors,
            MaxReportItems);

        return new CalendarVerificationResult
        {
            Outcome = CalendarVerificationOutcome.Verified,
            Report = report,
        };
    }

    private static CalendarVerificationResult Result(
        CalendarVerificationOutcome outcome,
        string detail) => new() { Outcome = outcome, Detail = detail };
}

public sealed record CalendarVerificationResult
{
    public required CalendarVerificationOutcome Outcome { get; init; }

    /// <summary>A human-readable reason for a non-verified outcome.</summary>
    public string? Detail { get; init; }

    /// <summary>The comparison, present only when <see cref="Outcome"/> is Verified.</summary>
    public CalendarVerificationReport? Report { get; init; }
}

public enum CalendarVerificationOutcome
{
    /// <summary>The live Google read succeeded and the report is present.</summary>
    Verified,

    /// <summary>The user has no Calendar connection.</summary>
    NoConnection,

    /// <summary>Initial sync has not yet created the dedicated calendar.</summary>
    NoManagedCalendar,

    /// <summary>The stored credential is rejected; the calendar cannot be read until re-authorized.</summary>
    NeedsReauthorization,

    /// <summary>The connection is not an authorized, sync-completed, actively-licensed one.</summary>
    NotSyncReady,

    /// <summary>The managed calendar is gone or inaccessible.</summary>
    CalendarUnavailable,

    /// <summary>A transient Google failure; retrying later may succeed.</summary>
    TransientError,
}

/// <summary>The read-only three-way comparison of one user's calendar (ADR-121).</summary>
public sealed record CalendarVerificationReport
{
    public required string ManagedCalendarId { get; init; }

    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>Currently-published lessons that apply to this student (the expected calendar).</summary>
    public required int ExpectedCount { get; init; }

    /// <summary>Rows in the local mapping ledger for this user.</summary>
    public required int LedgerCount { get; init; }

    /// <summary>Sirkadiyen-marked events actually on the Google calendar, announcements excluded.</summary>
    public required int GoogleEventCount { get; init; }

    public required int MatchedCount { get; init; }

    public required int MissingOnGoogleCount { get; init; }

    public required int ContentDriftCount { get; init; }

    public required int ExtraOnGoogleCount { get; init; }

    public required int DuplicateCount { get; init; }

    /// <summary>Marked events on Google carrying no stable identity.</summary>
    public required int UnmarkedCount { get; init; }

    /// <summary>Ledger rows for a lesson neither currently published nor on Google.</summary>
    public required int StaleLedgerCount { get; init; }

    /// <summary>True when every difference count is zero.</summary>
    public required bool InSync { get; init; }

    /// <summary>Whether <see cref="Items"/> is a bounded sample of a larger set.</summary>
    public required bool ItemsTruncated { get; init; }

    public required IReadOnlyList<CalendarVerificationDiff> Items { get; init; }
}

public sealed record CalendarVerificationDiff
{
    public required string StableIdentity { get; init; }

    public required CalendarVerificationDiffKind Kind { get; init; }

    public string? SourceId { get; init; }

    public string? ExpectedSummary { get; init; }

    public string? ActualSummary { get; init; }

    /// <summary>Whether the local mapping ledger has a row for this identity.</summary>
    public required bool InLedger { get; init; }

    public string? Detail { get; init; }
}

public enum CalendarVerificationDiffKind
{
    /// <summary>Expected (and often in the ledger) but not found on Google.</summary>
    MissingOnGoogle,

    /// <summary>Present on both but the Google event differs from what it should be.</summary>
    ContentDrift,

    /// <summary>On Google but no longer applies to this student.</summary>
    ExtraOnGoogle,

    /// <summary>More than one Google event carries the same stable identity.</summary>
    Duplicate,

    /// <summary>A ledger row for a lesson that is neither published nor on Google.</summary>
    StaleLedger,
}
