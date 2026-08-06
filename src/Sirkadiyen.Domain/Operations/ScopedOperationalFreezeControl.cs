using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Operations;

/// <summary>
/// An audited operational freeze for one class-year/program-language pipeline.
/// The existing singleton remains the global emergency stop; either switch freezes a scope.
/// </summary>
public sealed class ScopedOperationalFreezeControl
{
    private ScopedOperationalFreezeControl() { }

    public static ScopedOperationalFreezeControl Create(
        int classYear,
        ProgramLanguage programLanguage) => new()
        {
            Id = Guid.CreateVersion7(),
            ClassYear = classYear,
            ProgramLanguage = programLanguage,
        };

    public Guid Id { get; private init; }

    public int ClassYear { get; private init; }

    public ProgramLanguage ProgramLanguage { get; private init; }

    public bool IsFrozen { get; private set; }

    public string? Reason { get; private set; }

    public string? ChangedBy { get; private set; }

    public DateTimeOffset? ChangedAtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    public uint RowVersion { get; private set; }

    public ScopedOperationalFreezeAudit Change(
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc)
    {
        changedBy = RequiredBounded(changedBy, OperationalFreezeControl.MaximumActorLength, nameof(changedBy));
        reason = RequiredBounded(reason, OperationalFreezeControl.MaximumReasonLength, nameof(reason));
        correlationId = RequiredBounded(correlationId, OperationalFreezeControl.MaximumCorrelationIdLength, nameof(correlationId));

        if (isFrozen == IsFrozen)
        {
            throw new InvalidOperationException("The scoped operational freeze is already in the requested state.");
        }

        IsFrozen = isFrozen;
        ChangedBy = changedBy;
        Reason = reason;
        CorrelationId = correlationId;
        ChangedAtUtc = changedAtUtc;

        return ScopedOperationalFreezeAudit.Create(Id, isFrozen, changedBy, reason, correlationId, changedAtUtc);
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}
