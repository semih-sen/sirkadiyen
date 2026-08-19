namespace Sirkadiyen.Domain.Scheduling.Sources;

/// <summary>
/// One append-only, full-content image of the schedule source catalog as it stood after an
/// administrative edit (ADR-114).
/// </summary>
/// <remarks>
/// The catalog file is the single statement of what every source is, which parser reads it, and
/// which students it belongs to. An edit to it can retarget a whole program's published lessons,
/// so "what did the file say before, who changed it, and why" must be answerable without a server
/// login and without a repository checkout.
/// <para>
/// The whole document is stored, not a diff. A diff is only readable against the exact text it
/// was computed from, and the text on disk is editable by anything with a shell; keeping the full
/// image means a restore is a copy of a known-good document rather than a reconstruction. The
/// row is never updated or deleted — a rollback is a new revision whose content happens to equal
/// an older one.
/// </para>
/// <para>
/// A <see cref="ScheduleSourceCatalogRevisionKind.Baseline"/> row is written by the first edit,
/// recording the content that was already on disk before anyone edited it. Without it the oldest
/// restorable state would be the result of the first edit, and the state the system actually
/// started from would exist nowhere.
/// </para>
/// </remarks>
public sealed class ScheduleSourceCatalogRevision
{
    public const int MaximumContentHashLength = 64;

    public const int MaximumActorEmailLength = 320;

    public const int MaximumReasonLength = 2000;

    public const int MaximumCorrelationIdLength = 100;

    private ScheduleSourceCatalogRevision()
    {
        // Materialization constructor.
        Content = string.Empty;
        ContentHash = string.Empty;
    }

    public Guid Id { get; private init; }

    public ScheduleSourceCatalogRevisionKind Kind { get; private init; }

    public DateTimeOffset RecordedAtUtc { get; private init; }

    /// <summary>The full catalog document as it stood after this revision.</summary>
    public string Content { get; private init; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Content"/>.</summary>
    public string ContentHash { get; private init; }

    /// <summary>The hash this edit was made against, or <see langword="null"/> for a baseline.</summary>
    public string? PreviousContentHash { get; private init; }

    /// <summary>How many sources the document declares, for a listing that need not parse it.</summary>
    public int SourceCount { get; private init; }

    /// <summary>The acting SuperAdmin, or <see langword="null"/> for a baseline the system wrote.</summary>
    public Guid? ActorUserId { get; private init; }

    public string? ActorEmail { get; private init; }

    /// <summary>Why the change was made. Required for an edit, absent on a baseline.</summary>
    public string? Reason { get; private init; }

    public string? CorrelationId { get; private init; }

    /// <summary>A compact JSON summary of what changed, as shown to the operator who confirmed it.</summary>
    public string? ChangeSummary { get; private init; }

    public static ScheduleSourceCatalogRevision Baseline(
        DateTimeOffset recordedAtUtc,
        string content,
        string contentHash,
        int sourceCount) => new()
        {
            Id = Guid.CreateVersion7(),
            Kind = ScheduleSourceCatalogRevisionKind.Baseline,
            RecordedAtUtc = recordedAtUtc,
            Content = Required(content, nameof(content)),
            ContentHash = Hash(contentHash),
            SourceCount = sourceCount,
        };

    public static ScheduleSourceCatalogRevision Edit(
        DateTimeOffset recordedAtUtc,
        string content,
        string contentHash,
        string? previousContentHash,
        int sourceCount,
        Guid actorUserId,
        string actorEmail,
        string reason,
        string? correlationId,
        string? changeSummary) => new()
        {
            Id = Guid.CreateVersion7(),
            Kind = ScheduleSourceCatalogRevisionKind.Edit,
            RecordedAtUtc = recordedAtUtc,
            Content = Required(content, nameof(content)),
            ContentHash = Hash(contentHash),
            PreviousContentHash = previousContentHash is null ? null : Hash(previousContentHash),
            SourceCount = sourceCount,
            ActorUserId = actorUserId,
            ActorEmail = Bounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail)),
            Reason = Bounded(reason, MaximumReasonLength, nameof(reason)),
            CorrelationId = Optional(correlationId, MaximumCorrelationIdLength, nameof(correlationId)),
            ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary,
        };

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaximumContentHashLength);
        return value;
    }

    private static string Bounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? Optional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Bounded(value, maximumLength, parameterName);
    }
}

public enum ScheduleSourceCatalogRevisionKind
{
    /// <summary>The content that was on disk before the first administrative edit.</summary>
    Baseline,

    /// <summary>A content change an administrator confirmed.</summary>
    Edit,
}
