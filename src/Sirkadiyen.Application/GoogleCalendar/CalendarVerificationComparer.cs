using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The pure three-way comparison behind the on-demand admin calendar verification (ADR-121): current
/// published truth (what the student's calendar <em>should</em> hold), the local mapping ledger (what
/// Sirkadiyen recorded writing), and the actual Sirkadiyen-marked Google events (what is really there).
/// </summary>
/// <remarks>
/// This is the read-only twin of <see cref="CalendarInventoryReconciliationService"/>'s classification.
/// It observes and never repairs: it computes what each side holds and reports the differences, so it
/// is a pure function of its three inputs and is unit-tested without a database or the Calendar API.
/// It reuses <see cref="ManagedCalendarEventFactory"/> and <see cref="ManagedCalendarEventComparer"/> so
/// "content drift" here means exactly what a repair pass would act on.
/// </remarks>
public static class CalendarVerificationComparer
{
    public static CalendarVerificationReport Compare(
        Guid userId,
        string managedCalendarId,
        DateTimeOffset checkedAtUtc,
        IReadOnlyList<CanonicalScheduleRecord> expectedRecords,
        IReadOnlyList<CalendarEventMappingView> ledger,
        IReadOnlyList<ManagedCalendarEventSnapshot> actualEvents,
        IReadOnlyDictionary<string, string> departmentColors,
        int maxItems)
    {
        ArgumentNullException.ThrowIfNull(expectedRecords);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(actualEvents);
        ArgumentNullException.ThrowIfNull(departmentColors);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);

        Dictionary<string, CanonicalScheduleRecord> recordByIdentity = new(StringComparer.Ordinal);
        Dictionary<string, ManagedCalendarEvent> desiredByIdentity = new(StringComparer.Ordinal);
        foreach (CanonicalScheduleRecord record in expectedRecords)
        {
            // Current published truth can technically repeat a stable identity across sources; the
            // first wins here, matching how the ledger keys a single event per identity.
            if (recordByIdentity.TryAdd(record.StableIdentity, record))
            {
                desiredByIdentity[record.StableIdentity] =
                    ManagedCalendarEventFactory.ToManagedEvent(userId, record, departmentColors);
            }
        }

        Dictionary<string, CalendarEventMappingView> ledgerByIdentity = new(StringComparer.Ordinal);
        foreach (CalendarEventMappingView mapping in ledger)
        {
            ledgerByIdentity[mapping.StableIdentity] = mapping;
        }

        int unmarkedCount = 0;
        Dictionary<string, List<ManagedCalendarEventSnapshot>> actualByIdentity =
            new(StringComparer.Ordinal);
        foreach (ManagedCalendarEventSnapshot calendarEvent in actualEvents)
        {
            // An announcement or a cafeteria menu is Sirkadiyen-managed but is not schedule truth
            // (ADR-107, ADR-150); the verification has nothing to say about it, exactly as inventory
            // skips it.
            if (calendarEvent.PrivateProperties.TryGetValue(
                    ManagedCalendarEventFactory.KindKey,
                    out string? kind)
                && ManagedCalendarEventFactory.IsNonScheduleKind(kind))
            {
                continue;
            }

            if (!calendarEvent.PrivateProperties.TryGetValue("stableIdentity", out string? stableIdentity)
                || string.IsNullOrWhiteSpace(stableIdentity))
            {
                unmarkedCount++;
                continue;
            }

            if (!actualByIdentity.TryGetValue(stableIdentity, out List<ManagedCalendarEventSnapshot>? group))
            {
                group = [];
                actualByIdentity.Add(stableIdentity, group);
            }

            group.Add(calendarEvent);
        }

        HashSet<string> allIdentities = new(StringComparer.Ordinal);
        allIdentities.UnionWith(desiredByIdentity.Keys);
        allIdentities.UnionWith(ledgerByIdentity.Keys);
        allIdentities.UnionWith(actualByIdentity.Keys);

        int matched = 0, missing = 0, drift = 0, extra = 0, duplicate = 0, staleLedger = 0;
        List<CalendarVerificationDiff> items = [];

        foreach (string identity in allIdentities.OrderBy(value => value, StringComparer.Ordinal))
        {
            desiredByIdentity.TryGetValue(identity, out ManagedCalendarEvent? desired);
            recordByIdentity.TryGetValue(identity, out CanonicalScheduleRecord? record);
            ledgerByIdentity.TryGetValue(identity, out CalendarEventMappingView? mapping);
            actualByIdentity.TryGetValue(identity, out List<ManagedCalendarEventSnapshot>? candidates);
            candidates ??= [];

            bool inLedger = mapping is not null;
            string? sourceId = record?.SourceId.Value ?? mapping?.SourceId.Value;

            if (candidates.Count > 1)
            {
                duplicate++;
                items.Add(new CalendarVerificationDiff
                {
                    StableIdentity = identity,
                    Kind = CalendarVerificationDiffKind.Duplicate,
                    SourceId = sourceId,
                    ExpectedSummary = desired?.Summary,
                    ActualSummary = candidates[0].Summary,
                    InLedger = inLedger,
                    Detail = $"{candidates.Count} Google etkinliği aynı ders kimliğini taşıyor.",
                });
                continue;
            }

            ManagedCalendarEventSnapshot? actual = candidates.Count == 1 ? candidates[0] : null;

            if (desired is not null)
            {
                if (actual is null)
                {
                    missing++;
                    items.Add(new CalendarVerificationDiff
                    {
                        StableIdentity = identity,
                        Kind = CalendarVerificationDiffKind.MissingOnGoogle,
                        SourceId = sourceId,
                        ExpectedSummary = desired.Summary,
                        ActualSummary = null,
                        InLedger = inLedger,
                        Detail = inLedger
                            ? "Kayıt yazıldığını söylüyor ama Google'da bulunamadı."
                            : "Uygulanabilir ders henüz Google'a yazılmamış.",
                    });
                }
                else if (ManagedCalendarEventComparer.IsEquivalent(desired, actual))
                {
                    matched++;
                }
                else
                {
                    drift++;
                    items.Add(new CalendarVerificationDiff
                    {
                        StableIdentity = identity,
                        Kind = CalendarVerificationDiffKind.ContentDrift,
                        SourceId = sourceId,
                        ExpectedSummary = desired.Summary,
                        ActualSummary = actual.Summary,
                        InLedger = inLedger,
                        Detail = "Google'daki etkinlik beklenen içerikten farklı.",
                    });
                }
            }
            else if (actual is not null)
            {
                extra++;
                items.Add(new CalendarVerificationDiff
                {
                    StableIdentity = identity,
                    Kind = CalendarVerificationDiffKind.ExtraOnGoogle,
                    SourceId = sourceId,
                    ExpectedSummary = null,
                    ActualSummary = actual.Summary,
                    InLedger = inLedger,
                    Detail = "Google'da bir etkinlik var ama artık bu öğrenciye uygulanmıyor.",
                });
            }
            else
            {
                // A ledger row for a lesson that is neither currently published nor on Google. Google
                // and reality agree it is gone; only the ledger is stale.
                staleLedger++;
                items.Add(new CalendarVerificationDiff
                {
                    StableIdentity = identity,
                    Kind = CalendarVerificationDiffKind.StaleLedger,
                    SourceId = sourceId,
                    ExpectedSummary = null,
                    ActualSummary = null,
                    InLedger = true,
                    Detail = "Kayıtta satır var ama ders ne yayımlı ne de Google'da.",
                });
            }
        }

        bool inSync = missing == 0
            && drift == 0
            && extra == 0
            && duplicate == 0
            && unmarkedCount == 0
            && staleLedger == 0;

        // Show the most actionable differences first, then bound the list; the counts already carry
        // the full totals, so a truncated sample never hides that something is wrong.
        List<CalendarVerificationDiff> ordered =
            [.. items.OrderBy(item => KindOrder(item.Kind)).ThenBy(item => item.StableIdentity, StringComparer.Ordinal)];
        bool truncated = ordered.Count > maxItems;

        return new CalendarVerificationReport
        {
            ManagedCalendarId = managedCalendarId,
            CheckedAtUtc = checkedAtUtc,
            ExpectedCount = desiredByIdentity.Count,
            LedgerCount = ledgerByIdentity.Count,
            GoogleEventCount = actualByIdentity.Values.Sum(group => group.Count) + unmarkedCount,
            MatchedCount = matched,
            MissingOnGoogleCount = missing,
            ContentDriftCount = drift,
            ExtraOnGoogleCount = extra,
            DuplicateCount = duplicate,
            UnmarkedCount = unmarkedCount,
            StaleLedgerCount = staleLedger,
            InSync = inSync,
            ItemsTruncated = truncated,
            Items = truncated ? ordered[..maxItems] : ordered,
        };
    }

    private static int KindOrder(CalendarVerificationDiffKind kind) => kind switch
    {
        CalendarVerificationDiffKind.Duplicate => 0,
        CalendarVerificationDiffKind.ContentDrift => 1,
        CalendarVerificationDiffKind.MissingOnGoogle => 2,
        CalendarVerificationDiffKind.ExtraOnGoogle => 3,
        CalendarVerificationDiffKind.StaleLedger => 4,
        _ => 5,
    };
}
