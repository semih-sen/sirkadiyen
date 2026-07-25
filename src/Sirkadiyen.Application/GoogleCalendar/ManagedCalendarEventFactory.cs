using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Domain.SchedulePublication;

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

    // RFC 4648 base32hex alphabet, lowercase. Every symbol is in Google Calendar's allowed
    // event-id set (a-v and 0-9), so a hash encoded with it is always a valid id.
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    public static ManagedCalendarEvent ToManagedEvent(Guid userId, CanonicalScheduleRecord record)
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
            Label = CalendarEventPresentationPolicy.EventLabel(record),
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
    /// A stable, collision-resistant event id derived from the user and the lesson identity.
    /// Re-deriving it lets a resumed sync re-insert the same id and have Google reject the
    /// duplicate instead of creating a second event.
    /// </summary>
    public static string DeterministicEventId(Guid userId, string stableIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{userId:N}\n{stableIdentity}"));
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
