using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Owns the transactional persistence boundary between a parser response and
/// the candidate revision it produced.
/// </summary>
public interface IScheduleParseResultStore
{
    /// <param name="staleRunTimeout">
    /// How long a run may stay open before it is treated as abandoned by a worker
    /// that no longer exists. The policy is the caller's, not the store's.
    /// </param>
    /// <param name="companionFingerprint">
    /// The companion evidence this parse will read besides <paramref name="snapshot"/>,
    /// reduced to one value by <see cref="ParseRunCompanionFingerprint"/>, or
    /// <see cref="ParseRunCompanionFingerprint.None"/> when it will read none. It
    /// is part of the run's identity, so a changed companion opens a new run
    /// instead of being reported as already parsed (ADR-102).
    /// </param>
    Task<BeginParseRunResult> BeginOrResumeAsync(
        SourceSnapshot snapshot,
        ScheduleSource source,
        string correlationId,
        DateTimeOffset startedAtUtc,
        TimeSpan staleRunTimeout,
        string companionFingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes a running parse and persists the revision it produced, if it
    /// produced one.
    /// </summary>
    /// <remarks>
    /// A successful parse does not always deserve a revision. When the records it
    /// produced say exactly what the source's previous revision already said, no
    /// revision is created and nothing downstream runs: no validation, no
    /// publication, no diff, no calendar dispatch. The parse run itself is still
    /// completed and still records what the parser returned, because the evidence
    /// that the document was read is worth keeping either way.
    /// </remarks>
    Task<ParseCompletion> CompleteAsync(
        Guid parseRunId,
        ParseSnapshotResponse response,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid parseRunId,
        DateTimeOffset completedAtUtc,
        string failureReason,
        CancellationToken cancellationToken);
}

public sealed record BeginParseRunResult
{
    public required Guid ParseRunId { get; init; }

    public required ParseRunStatus Status { get; init; }

    public required bool ShouldInvokeParser { get; init; }

    /// <summary>How this run came to be open, for logging and poll reporting.</summary>
    public ParseRunStartKind StartKind { get; init; } = ParseRunStartKind.Started;
}

/// <summary>What completing a parse run left behind.</summary>
/// <remarks>
/// The three outcomes are deliberately distinct rather than "a revision or null".
/// A parse that was refused and a parse that changed nothing are opposite
/// situations — one needs an operator, the other needs nobody — and reporting
/// both as an absent revision is how a pipeline hides the fact that most of its
/// work is redundant.
/// </remarks>
public sealed record ParseCompletion
{
    public required ParseCompletionOutcome Outcome { get; init; }

    /// <summary>The revision created, when one was.</summary>
    public ScheduleRevision? Revision { get; init; }

    /// <summary>
    /// The revision this parse turned out to repeat, when it repeated one. Kept so
    /// that "nothing happened" can always name what it matched.
    /// </summary>
    public Guid? UnchangedFromRevisionId { get; init; }
}

public enum ParseCompletionOutcome
{
    /// <summary>A candidate revision was created and awaits validation.</summary>
    RevisionCreated,

    /// <summary>
    /// The parser refused the document, so there are no records and no revision.
    /// </summary>
    ParserRejected,

    /// <summary>
    /// The parse succeeded and produced exactly the record set the source's most
    /// recent revision already holds, so no revision was created.
    /// </summary>
    UnchangedRecordSet,
}

public enum ParseRunStartKind
{
    /// <summary>A new run, or one resumed after a recorded failure.</summary>
    Started,

    /// <summary>A run reopened after being left running by a vanished worker.</summary>
    RecoveredStale,
}
