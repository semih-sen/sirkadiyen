using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.GoogleCalendar;

/// <summary>
/// A durable link between one logical lesson on a user's calendar and the Google Calendar
/// event that represents it (ADR-024, ADR-058).
/// </summary>
/// <remarks>
/// The mapping is keyed by <see cref="StableIdentity"/> — the identity that survives a room
/// or instructor change (ADR-018) — so a later incremental sync patches the same event
/// instead of deleting and recreating it. It is also the idempotency ledger for the initial
/// sync: a record with a mapping has already been written, so a resumed run skips it. The
/// stored <see cref="ContentHash"/> lets a future incremental sync decide whether a patch is
/// even needed.
/// </remarks>
public sealed class UserCalendarEventMapping
{
    public const int MaximumStableIdentityLength = 512;

    public const int MaximumGoogleCalendarIdLength = 1024;

    /// <summary>Google's own upper bound for an event id.</summary>
    public const int MaximumGoogleEventIdLength = 1024;

    public const int MaximumContentHashLength = 512;

    private UserCalendarEventMapping()
    {
        // Materialization constructor.
        StableIdentity = string.Empty;
        GoogleCalendarId = string.Empty;
        GoogleEventId = string.Empty;
        ContentHash = string.Empty;
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    /// <summary>The logical lesson identity this event represents; unique per user.</summary>
    public string StableIdentity { get; private set; }

    /// <summary>The source the lesson came from, kept for scoping and later cleanup.</summary>
    public SourceId SourceId { get; private init; }

    /// <summary>The specific canonical record last written, for traceability.</summary>
    public Guid CanonicalRecordId { get; private set; }

    /// <summary>The dedicated Sirkadiyen calendar the event lives in (ADR-024).</summary>
    public string GoogleCalendarId { get; private init; }

    /// <summary>The Google Calendar event id, deterministic for this user and lesson.</summary>
    public string GoogleEventId { get; private init; }

    /// <summary>The content hash last written, so a future sync knows if a patch is needed.</summary>
    public string ContentHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public static UserCalendarEventMapping Create(
        Guid userId,
        string stableIdentity,
        SourceId sourceId,
        Guid canonicalRecordId,
        string googleCalendarId,
        string googleEventId,
        string contentHash,
        DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A mapping owner is required.", nameof(userId));
        }

        if (canonicalRecordId == Guid.Empty)
        {
            throw new ArgumentException(
                "A mapping must reference a canonical record.",
                nameof(canonicalRecordId));
        }

        return new UserCalendarEventMapping
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            StableIdentity = RequiredBounded(
                stableIdentity,
                MaximumStableIdentityLength,
                nameof(stableIdentity)),
            SourceId = sourceId,
            CanonicalRecordId = canonicalRecordId,
            GoogleCalendarId = RequiredBounded(
                googleCalendarId,
                MaximumGoogleCalendarIdLength,
                nameof(googleCalendarId)),
            GoogleEventId = RequiredBounded(
                googleEventId,
                MaximumGoogleEventIdLength,
                nameof(googleEventId)),
            ContentHash = RequiredBounded(
                contentHash,
                MaximumContentHashLength,
                nameof(contentHash)),
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>
    /// Records that the event now reflects a newer canonical record with different content.
    /// Reserved for incremental sync; the event id and logical identity never change.
    /// </summary>
    public void UpdateContent(Guid canonicalRecordId, string contentHash, DateTimeOffset atUtc)
    {
        if (canonicalRecordId == Guid.Empty)
        {
            throw new ArgumentException(
                "A mapping must reference a canonical record.",
                nameof(canonicalRecordId));
        }

        CanonicalRecordId = canonicalRecordId;
        ContentHash = RequiredBounded(
            contentHash,
            MaximumContentHashLength,
            nameof(contentHash));
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Moves the ledger identity after a semantic secondary match recognized a retimed lesson as
    /// the same logical event. The Google event id deliberately stays unchanged, so Calendar sees
    /// an in-place patch rather than a delete-and-create.
    /// </summary>
    public void Reidentify(
        string stableIdentity,
        Guid canonicalRecordId,
        string contentHash,
        DateTimeOffset atUtc)
    {
        StableIdentity = RequiredBounded(
            stableIdentity,
            MaximumStableIdentityLength,
            nameof(stableIdentity));
        UpdateContent(canonicalRecordId, contentHash, atUtc);
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}
