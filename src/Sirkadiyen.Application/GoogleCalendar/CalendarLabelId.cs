using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Derives the deterministic UUID of a calendar-scoped Google event label from its category key
/// (ADR-072).
/// </summary>
/// <remarks>
/// Extracted so schedule presentation and announcement presentation cannot drift apart. The hash
/// material is fixed forever: changing it would give every already-written event a label id its
/// calendar does not define, so every managed event on every calendar would need re-labelling.
/// </remarks>
public static class CalendarLabelId
{
    public static string For(string categoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);

        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"sirkadiyen-label\n{categoryKey}"))[..16];

        // Version 5 / RFC 4122 variant bits, so the value is a well-formed UUID.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}
