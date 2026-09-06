using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Translates a canonical schedule record into the calendar event to write for one user
/// (ADR-058). Pure and deterministic: the same record for the same user always produces the
/// same event id, which is the idempotency key the initial sync relies on.
/// </summary>
public static class ManagedCalendarEventFactory
{
    /// <summary>Marks a Sirkadiyen-managed event; readable only by Sirkadiyen (ADR-024).</summary>
    public const string ManagedMarkerKey = "sirkadiyen";

    /// <summary>
    /// Distinguishes what kind of managed event this is (ADR-107). A lesson written before
    /// announcements existed carries no such key, so its absence means "lesson" — the marker is
    /// added only to the new kind precisely so no existing event has to be rewritten to gain it.
    /// </summary>
    public const string KindKey = "sirkadiyenKind";

    /// <summary>The <see cref="KindKey"/> value of an administrator-authored announcement.</summary>
    public const string AnnouncementKind = "announcement";

    /// <summary>The <see cref="KindKey"/> value of a cafeteria menu event (ADR-150).</summary>
    public const string MealKind = "meal";

    /// <summary>
    /// Whether a <see cref="KindKey"/> value marks a managed event that is Sirkadiyen's but is not
    /// schedule truth — an announcement or a menu (ADR-107, ADR-150). Inventory and verification use
    /// it to leave such events alone rather than reporting them as unexpected marked events.
    /// </summary>
    public static bool IsNonScheduleKind(string? kind) =>
        string.Equals(kind, AnnouncementKind, StringComparison.Ordinal)
        || string.Equals(kind, MealKind, StringComparison.Ordinal);

    // RFC 4648 base32hex alphabet, lowercase. Every symbol is in Google Calendar's allowed
    // event-id set (a-v and 0-9), so a hash encoded with it is always a valid id.
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    public static ManagedCalendarEvent ToManagedEvent(
        Guid userId,
        CanonicalScheduleRecord record,
        IReadOnlyDictionary<string, string>? departmentColors = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        Dictionary<string, string> privateProperties = new(StringComparer.Ordinal)
        {
            [ManagedMarkerKey] = "1",
            ["stableIdentity"] = record.StableIdentity,
            ["contentHash"] = record.ContentHash,
            ["sourceId"] = record.SourceId.Value,
            ["canonicalRecordId"] = record.Id.ToString(),
        };

        ManagedCalendarEvent managedEvent = new()
        {
            EventId = DeterministicEventId(userId, record.StableIdentity),
            Summary = CalendarEventPresentationPolicy.Summary(record),
            Description = CalendarEventPresentationPolicy.Description(record),
            Location = CalendarEventPresentationPolicy.Location(record),
            Label = CalendarEventPresentationPolicy.EventLabel(record, departmentColors),
            TimeZoneId = record.TimeZoneId,
            IsAllDay = record.IsAllDay,
            PrivateProperties = privateProperties,
        };

        if (record.IsAllDay)
        {
            // Google Calendar treats an all-day end date as exclusive, so a one-day closure
            // spans from its date to the following day (the conversion noted on the record).
            return managedEvent with
            {
                StartDate = record.LocalDate,
                EndDateExclusive = record.LocalDate.AddDays(1),
            };
        }

        // A timed record always carries both local times (a domain invariant), so the wall-clock
        // start and end are safe to combine here.
        return managedEvent with
        {
            LocalStart = record.LocalDate.ToDateTime(record.StartLocalTime!.Value),
            LocalEnd = record.LocalDate.ToDateTime(record.EndLocalTime!.Value),
        };
    }

    /// <summary>
    /// A stable, collision-resistant event id derived from the user and an object identity.
    /// Re-deriving it lets a resumed sync re-insert the same id and have Google reject the
    /// duplicate instead of creating a second event.
    /// </summary>
    /// <param name="identity">
    /// A lesson's stable identity, or an announcement's namespaced identity (ADR-107). The two
    /// share this derivation rather than duplicating it, and cannot collide: a stable identity is
    /// a hex digest produced by a parser profile, while an announcement identity is prefixed with
    /// a literal that no hex digest can spell.
    /// </param>
    public static string DeterministicEventId(Guid userId, string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{userId:N}\n{identity}"));
        return Base32HexEncode(hash);
    }

    private static string Base32HexEncode(ReadOnlySpan<byte> data)
    {
        StringBuilder builder = new((data.Length * 8 / 5) + 1);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                builder.Append(Base32HexAlphabet[(buffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
        {
            builder.Append(Base32HexAlphabet[(buffer << (5 - bitsInBuffer)) & 0x1F]);
        }

        return builder.ToString();
    }
}
