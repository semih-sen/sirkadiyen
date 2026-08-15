namespace Sirkadiyen.Domain.Scheduling.Parsing;

/// <summary>
/// One execution of one parser profile version against one snapshot.
/// </summary>
/// <remarks>
/// Parse runs are kept even when they fail or produce warnings. A revision can
/// only be judged against the run that produced it, and "the parser saw nothing
/// unusual" and "the parser was never run" must never look the same.
/// </remarks>
public sealed class ParseRun
{
    private ParseRun()
    {
        // Materialization constructor.
        ParserProfile = string.Empty;
        ParserProfileVersion = string.Empty;
        CorrelationId = string.Empty;
        CompanionFingerprint = string.Empty;
    }

    public ParseRun(
        Guid sourceSnapshotId,
        string parserProfile,
        string parserProfileVersion,
        string correlationId,
        DateTimeOffset startedAtUtc,
        string companionFingerprint = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(companionFingerprint);

        Id = Guid.CreateVersion7();
        SourceSnapshotId = sourceSnapshotId;
        ParserProfile = parserProfile;
        ParserProfileVersion = parserProfileVersion;
        CorrelationId = correlationId;
        CompanionFingerprint = companionFingerprint;
        StartedAtUtc = startedAtUtc;
        Status = ParseRunStatus.Running;
        AttemptCount = 1;
    }

    public Guid Id { get; private set; }

    public Guid SourceSnapshotId { get; private set; }

    public string ParserProfile { get; private set; }

    public string ParserProfileVersion { get; private set; }

    public string CorrelationId { get; private set; }

    /// <summary>
    /// The companion evidence this run read besides its own snapshot, as one
    /// deterministic value, or the empty string when it read none (ADR-102).
    /// </summary>
    /// <remarks>
    /// A run is the execution of one profile version against one set of inputs,
    /// and a companion document is an input. Without this the annual program
    /// would keep the parse it already has when only the bedside document
    /// changed, so a corrected topic would never reach a student's calendar.
    /// It is a fingerprint rather than the list itself because it belongs to a
    /// uniqueness key, and an unbounded list is not an index column.
    /// </remarks>
    public string CompanionFingerprint { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public ParseRunStatus Status { get; private set; }

    /// <summary>
    /// Number of transport attempts made for this deterministic snapshot and
    /// parser-profile pair. The unique parse run remains the logical execution;
    /// a failed HTTP attempt may safely resume it.
    /// </summary>
    public int AttemptCount { get; private set; }

    public int CandidateCount { get; private set; }

    public int WarningCount { get; private set; }

    public int ErrorCount { get; private set; }

    /// <summary>The parser response document, absent while the run is open.</summary>
    public string? Response { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// When this run was last recovered from a <see cref="ParseRunStatus.Running"/>
    /// state that no live parse was still working on, or <c>null</c> if it never was.
    /// </summary>
    /// <remarks>
    /// A worker killed mid-parse leaves the run running forever, and the source
    /// would then never be parsed again from that snapshot. Recovery is recorded
    /// rather than silent, because a run that needed recovering is evidence about
    /// the deployment and not just a retry. The reason is not stored: it is a
    /// fixed sentence derived from <see cref="StartedAtUtc"/> and the configured
    /// timeout, and <see cref="AttemptCount"/> already carries how often it
    /// happened.
    /// </remarks>
    public DateTimeOffset? LastStaleRecoveryAtUtc { get; private set; }

    public void Complete(
        ParseRunStatus status,
        DateTimeOffset completedAtUtc,
        string response,
        int candidateCount,
        int warningCount,
        int errorCount)
    {
        if (status is ParseRunStatus.Running)
        {
            throw new ArgumentException(
                "A completed parse run cannot be left running.",
                nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        Status = status;
        CompletedAtUtc = completedAtUtc;
        Response = response;
        CandidateCount = candidateCount;
        WarningCount = warningCount;
        ErrorCount = errorCount;
    }

    public void Fail(DateTimeOffset completedAtUtc, string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        Status = ParseRunStatus.Failed;
        CompletedAtUtc = completedAtUtc;
        FailureReason = failureReason;
    }

    public void Resume(string correlationId, DateTimeOffset startedAtUtc)
    {
        if (Status is not ParseRunStatus.Failed)
        {
            throw new InvalidOperationException("Only a failed parse run can be resumed.");
        }

        Restart(correlationId, startedAtUtc);
    }

    /// <summary>
    /// Whether this run has been open longer than a parse may legitimately take,
    /// which means the worker that started it is gone.
    /// </summary>
    /// <remarks>
    /// The timeout has to exceed the parser transport timeout, otherwise a slow
    /// but healthy parse would be recovered underneath itself. That is a
    /// composition concern, so it is not asserted here; the caller supplies the
    /// configured value.
    /// </remarks>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(staleAfter, TimeSpan.Zero);

        return Status is ParseRunStatus.Running && now - StartedAtUtc >= staleAfter;
    }

    /// <summary>
    /// Reopens a run that was left running by an abrupt shutdown so the same
    /// snapshot can be parsed again.
    /// </summary>
    /// <remarks>
    /// Recovery reuses this run instead of creating a second one: the run is the
    /// logical execution of one profile version against one snapshot, and a
    /// duplicate would break that uniqueness and could produce a second revision
    /// for the same snapshot.
    /// </remarks>
    public void RecoverStale(
        string correlationId,
        DateTimeOffset startedAtUtc,
        TimeSpan staleAfter)
    {
        if (!IsStale(startedAtUtc, staleAfter))
        {
            throw new InvalidOperationException(
                "Only a parse run left running past its stale timeout can be recovered.");
        }

        LastStaleRecoveryAtUtc = startedAtUtc;
        Restart(correlationId, startedAtUtc);
    }

    private void Restart(string correlationId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        CorrelationId = correlationId;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = null;
        Status = ParseRunStatus.Running;
        CandidateCount = 0;
        WarningCount = 0;
        ErrorCount = 0;
        Response = null;
        FailureReason = null;
        AttemptCount++;
    }
}

public enum ParseRunStatus
{
    Running,
    Completed,
    CompletedWithWarnings,
    Rejected,
    Failed,
}
