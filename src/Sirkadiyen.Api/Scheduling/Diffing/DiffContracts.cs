namespace Sirkadiyen.Api.Scheduling.Diffing;

/// <summary>
/// Why the authenticated SuperAdmin is returning a failed diff to the dispatch queue.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record RetryDiffRequest
{
    /// <example>The Calendar outage that exhausted the attempts is over; Google is answering again.</example>
    public required string? RetryReason { get; init; }
}

public sealed record RetryDiffResponse
{
    public required Guid ScheduleDiffId { get; init; }

    public required bool Retried { get; init; }

    public DateTimeOffset? RetriedAtUtc { get; init; }

    /// <summary>How many times this diff has now been retried, including this one.</summary>
    public required int DispatchRetryCount { get; init; }
}

/// <summary>
/// Why the authenticated SuperAdmin is releasing a held diff.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record ReleaseDiffRequest
{
    /// <example>Checked the source: the 38 deleted lessons are the ended anatomy block.</example>
    public required string? ReleaseReason { get; init; }
}

public sealed record ReleaseDiffResponse
{
    public required Guid ScheduleDiffId { get; init; }

    public required bool Released { get; init; }

    public DateTimeOffset? ReleasedAtUtc { get; init; }
}
