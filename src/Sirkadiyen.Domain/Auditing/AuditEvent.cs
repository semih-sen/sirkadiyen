namespace Sirkadiyen.Domain.Auditing;

/// <summary>
/// One append-only record of an account-access or account-activity event.
/// </summary>
/// <remarks>
/// This is the cross-cutting activity log that the per-domain audits (license, revision, diff,
/// freeze, department colour, source upload) never covered — most importantly sign-in, which was
/// previously invisible because <c>User.LastSignedInAtUtc</c> is overwritten each time.
/// <para>
/// The client IP is recorded in two forms: <see cref="MaskedIp"/> is stored and shown by default
/// (AI_GUIDELINE §15), while <see cref="ProtectedIp"/> holds the full address encrypted at rest, so
/// revealing it is a deliberate, separately audited action rather than an always-visible field.
/// </para>
/// </remarks>
public sealed class AuditEvent
{
    public const int MaximumActorEmailLength = 320;

    public const int MaximumSubjectTypeLength = 100;

    public const int MaximumSubjectIdLength = 200;

    public const int MaximumCorrelationIdLength = 100;

    public const int MaximumMaskedIpLength = 64;

    public const int MaximumUserAgentLength = 512;

    public const int MaximumReasonLength = 2000;

    private AuditEvent()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public AuditEventCategory Category { get; private init; }

    public DateTimeOffset OccurredAtUtc { get; private init; }

    /// <summary>The acting local account, or <see langword="null"/> for an anonymous/system event.</summary>
    public Guid? ActorUserId { get; private init; }

    public string? ActorEmail { get; private init; }

    /// <summary>What the event is about (for example <c>License</c>, <c>Revision</c>), when relevant.</summary>
    public string? SubjectType { get; private init; }

    public string? SubjectId { get; private init; }

    public string? CorrelationId { get; private init; }

    /// <summary>The masked client IP shown by default (for example <c>203.0.113.0</c>).</summary>
    public string? MaskedIp { get; private init; }

    /// <summary>The full client IP encrypted at rest; never returned unless explicitly unmasked.</summary>
    public string? ProtectedIp { get; private init; }

    public string? UserAgent { get; private init; }

    public string? Reason { get; private init; }

    /// <summary>Structured, event-specific detail serialized as JSON, or <see langword="null"/>.</summary>
    public string? Metadata { get; private init; }

    public static AuditEvent Create(
        AuditEventCategory category,
        DateTimeOffset occurredAtUtc,
        Guid? actorUserId,
        string? actorEmail,
        string? subjectType,
        string? subjectId,
        string? correlationId,
        string? maskedIp,
        string? protectedIp,
        string? userAgent,
        string? reason,
        string? metadata) => new()
        {
            Id = Guid.CreateVersion7(),
            Category = category,
            OccurredAtUtc = occurredAtUtc,
            ActorUserId = actorUserId,
            ActorEmail = OptionalBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail)),
            SubjectType = OptionalBounded(
                subjectType,
                MaximumSubjectTypeLength,
                nameof(subjectType)),
            SubjectId = OptionalBounded(subjectId, MaximumSubjectIdLength, nameof(subjectId)),
            CorrelationId = OptionalBounded(
                correlationId,
                MaximumCorrelationIdLength,
                nameof(correlationId)),
            MaskedIp = OptionalBounded(maskedIp, MaximumMaskedIpLength, nameof(maskedIp)),
            ProtectedIp = string.IsNullOrWhiteSpace(protectedIp) ? null : protectedIp,
            UserAgent = OptionalBounded(userAgent, MaximumUserAgentLength, nameof(userAgent)),
            Reason = OptionalBounded(reason, MaximumReasonLength, nameof(reason)),
            Metadata = string.IsNullOrWhiteSpace(metadata) ? null : metadata,
        };

    private static string? OptionalBounded(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

/// <summary>
/// The kind of account-access or activity event recorded. Stored as a string so new categories
/// can be added without a data migration; only the categories actually written today are listed.
/// </summary>
public enum AuditEventCategory
{
    /// <summary>A successful Google sign-in that started a local session.</summary>
    SignIn,

    /// <summary>A user asked for an on-demand reconciliation of their own calendar.</summary>
    ReconcileRequested,

    /// <summary>An administrator revealed the full client IP behind a masked audit record.</summary>
    IpUnmasked,

    /// <summary>A SuperAdmin hard-deleted a finance transaction (AI_GUIDELINE §15, ADR-093).</summary>
    FinanceTransactionDeleted,

    /// <summary>A SuperAdmin executed a profit distribution (ADR-093).</summary>
    FinanceDistributionExecuted,

    /// <summary>
    /// A student created or replaced their own academic profile. Recorded because a profile change
    /// can retire calendar events the previous audience received (ADR-096), so "why did these
    /// lessons disappear" would otherwise be answerable only from the mapping ledger and the worker
    /// log (AI_GUIDELINE §19).
    /// </summary>
    ProfileUpdated,

    /// <summary>
    /// A SuperAdmin confirmed an administrator-authored calendar announcement, freezing its
    /// recipient set and queueing it for delivery (ADR-107).
    /// </summary>
    AnnouncementQueued,

    /// <summary>
    /// A SuperAdmin changed what a queued or delivered announcement says, which patches every
    /// copy already written to a calendar.
    /// </summary>
    AnnouncementUpdated,

    /// <summary>
    /// A SuperAdmin cancelled an announcement, which removes every copy already written. This is
    /// a calendar deletion an operator authorized directly rather than one a published revision
    /// derived, so it is recorded here in its own right (AI_GUIDELINE §19).
    /// </summary>
    AnnouncementCancelled,
}
