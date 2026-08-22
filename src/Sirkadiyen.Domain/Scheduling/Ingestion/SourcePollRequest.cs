using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Scheduling.Ingestion;

/// <summary>
/// An operator's request to poll one schedule source now, drained by the worker on its next cycle
/// (ADR-127).
/// </summary>
/// <remarks>
/// Acquisition lives in the worker — that is where the Google and Drive clients and their
/// credentials are — so the admin API cannot poll a source itself. Instead it enqueues a request
/// here, and the worker claims and executes it. This keeps one place responsible for reading a
/// document, makes a manual poll reflect in the same "next poll" heartbeat as the scheduled one, and
/// lets an operator ask for a <see cref="Force"/> reparse of an unchanged document after a profile
/// or configuration change.
/// </remarks>
public sealed class SourcePollRequest
{
    public const int MaximumRequestedByLength = 200;

    private SourcePollRequest()
    {
        // Materialization constructor.
    }

    public static SourcePollRequest Create(
        SourceId sourceId,
        bool force,
        string requestedBy,
        DateTimeOffset requestedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        requestedBy = requestedBy.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            requestedBy.Length,
            MaximumRequestedByLength,
            nameof(requestedBy));

        return new SourcePollRequest
        {
            Id = Guid.CreateVersion7(),
            SourceId = sourceId,
            Force = force,
            RequestedBy = requestedBy,
            RequestedAtUtc = Utc(requestedAtUtc, nameof(requestedAtUtc)),
        };
    }

    public Guid Id { get; private init; }

    public SourceId SourceId { get; private init; }

    /// <summary>Whether the worker should reparse even an unchanged document (ADR-127).</summary>
    public bool Force { get; private init; }

    public string RequestedBy { get; private init; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; private init; }

    /// <summary>When the worker took ownership of this request; null while it is still pending.</summary>
    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    /// <summary>Records the worker taking ownership so the request is polled exactly once.</summary>
    public void Claim(DateTimeOffset atUtc)
    {
        if (ClaimedAtUtc is not null)
        {
            throw new InvalidOperationException("This poll request has already been claimed.");
        }

        ClaimedAtUtc = Utc(atUtc, nameof(atUtc));
    }

    private static DateTimeOffset Utc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be expressed in UTC.", parameterName);
        }

        return value;
    }
}
