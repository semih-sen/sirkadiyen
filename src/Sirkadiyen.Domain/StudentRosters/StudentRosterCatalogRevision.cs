namespace Sirkadiyen.Domain.StudentRosters;

/// <summary>
/// One append-only, full-content image of the student roster catalog as it stood after an
/// administrative edit (ADR-134).
/// </summary>
/// <remarks>
/// The roster catalog states which published list belongs to which cohort and which column of it
/// means what. Nothing is parsed into canonical records from it and nothing is published, so an
/// edit here cannot move a lesson — but it decides what a student's profile is filled in with
/// during onboarding, and a wrong column mapping puts a whole cohort into the wrong practice
/// group without any lookup failing. "What did the file say before, who changed it, and why" must
/// therefore be answerable without a server login and without a repository checkout, exactly as it
/// is for the schedule source catalog (ADR-114).
/// <para>
/// The whole document is stored rather than a diff, for the reason
/// <see cref="Scheduling.Sources.ScheduleSourceCatalogRevision"/> gives: a diff is only readable
/// against the exact text it was computed from, and the text on disk is editable by anything with
/// a shell. The row is never updated or deleted — a rollback is a new revision whose content
/// happens to equal an older one.
/// </para>
/// </remarks>
public sealed class StudentRosterCatalogRevision
{
    public const int MaximumContentHashLength = 64;

    public const int MaximumActorEmailLength = 320;

    public const int MaximumReasonLength = 2000;

    public const int MaximumCorrelationIdLength = 100;

    private StudentRosterCatalogRevision()
    {
        // Materialization constructor.
        Content = string.Empty;
        ContentHash = string.Empty;
    }

    public Guid Id { get; private init; }

    public StudentRosterCatalogRevisionKind Kind { get; private init; }

    public DateTimeOffset RecordedAtUtc { get; private init; }

    /// <summary>The full catalog document as it stood after this revision.</summary>
    public string Content { get; private init; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Content"/>.</summary>
    public string ContentHash { get; private init; }

    /// <summary>The hash this edit was made against, or <see langword="null"/> for a baseline.</summary>
    public string? PreviousContentHash { get; private init; }

    /// <summary>How many rosters the document declares, for a listing that need not parse it.</summary>
    public int RosterCount { get; private init; }

    /// <summary>The acting SuperAdmin, or <see langword="null"/> for a baseline the system wrote.</summary>
    public Guid? ActorUserId { get; private init; }

    public string? ActorEmail { get; private init; }

    /// <summary>Why the change was made. Required for an edit, absent on a baseline.</summary>
    public string? Reason { get; private init; }

    public string? CorrelationId { get; private init; }

    /// <summary>A compact JSON summary of what changed, as shown to the operator who confirmed it.</summary>
    public string? ChangeSummary { get; private init; }

    public static StudentRosterCatalogRevision Baseline(
        DateTimeOffset recordedAtUtc,
        string content,
        string contentHash,
        int rosterCount) => new()
        {
            Id = Guid.CreateVersion7(),
            Kind = StudentRosterCatalogRevisionKind.Baseline,
            RecordedAtUtc = recordedAtUtc,
            Content = Required(content, nameof(content)),
            ContentHash = Hash(contentHash),
            RosterCount = rosterCount,
        };

    public static StudentRosterCatalogRevision Edit(
        DateTimeOffset recordedAtUtc,
        string content,
        string contentHash,
        string? previousContentHash,
        int rosterCount,
        Guid actorUserId,
        string actorEmail,
        string reason,
        string? correlationId,
        string? changeSummary) => new()
        {
            Id = Guid.CreateVersion7(),
            Kind = StudentRosterCatalogRevisionKind.Edit,
            RecordedAtUtc = recordedAtUtc,
            Content = Required(content, nameof(content)),
            ContentHash = Hash(contentHash),
            PreviousContentHash = previousContentHash is null ? null : Hash(previousContentHash),
            RosterCount = rosterCount,
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

public enum StudentRosterCatalogRevisionKind
{
    /// <summary>The content that was on disk before the first administrative edit.</summary>
    Baseline,

    /// <summary>A content change an administrator confirmed.</summary>
    Edit,
}
