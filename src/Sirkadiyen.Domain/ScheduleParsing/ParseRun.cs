namespace Sirkadiyen.Domain.ScheduleParsing;

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
    }

    public ParseRun(
        Guid sourceSnapshotId,
        string parserProfile,
        string parserProfileVersion,
        string correlationId,
        DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Id = Guid.CreateVersion7();
        SourceSnapshotId = sourceSnapshotId;
        ParserProfile = parserProfile;
        ParserProfileVersion = parserProfileVersion;
        CorrelationId = correlationId;
        StartedAtUtc = startedAtUtc;
        Status = ParseRunStatus.Running;
        AttemptCount = 1;
    }

    public Guid Id { get; private set; }

    public Guid SourceSnapshotId { get; private set; }

    public string ParserProfile { get; private set; }

    public string ParserProfileVersion { get; private set; }

    public string CorrelationId { get; private set; }

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
