using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Reads and writes the dates an operator has decided a source states wrongly
/// (ADR-139).
/// </summary>
/// <remarks>
/// A correction is source context, not an edit to a parsed record, so the poller
/// reads it on every cycle and sends it with the parse request. Re-parsing the
/// same snapshot with the same corrections therefore produces the same records,
/// which is what keeps a parse a pure function of its inputs (ADR-017).
/// </remarks>
public interface IScheduleSourceDateCorrectionStore
{
    /// <summary>
    /// The corrections accepted for one source, ordered by the date they correct.
    /// </summary>
    /// <remarks>
    /// The order is fixed in the database rather than left to the row order,
    /// because the list travels into the parse-run key: an unordered set would
    /// make a run's fingerprint differ from itself and re-parse forever, the same
    /// trap ADR-126's coverage read documents.
    /// </remarks>
    Task<IReadOnlyList<ScheduleSourceDateCorrection>> ListForSourceAsync(
        SourceId sourceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Accepts a correction, replacing any the source already has for the same
    /// original date.
    /// </summary>
    /// <remarks>
    /// Replacing rather than refusing is deliberate: an operator who reads the
    /// suggestion again and picks the other candidate is correcting their own
    /// earlier decision, and making them delete it first would be a step with no
    /// meaning. The replacement is a new row with its own decider and timestamp,
    /// so who decided what and when stays readable.
    /// </remarks>
    Task<ScheduleSourceDateCorrection> AcceptAsync(
        ScheduleSourceDateCorrection correction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retires a correction, returning whether one was removed.
    /// </summary>
    /// <remarks>
    /// Used when the faculty fixes the document: the source no longer states the
    /// wrong date, and a correction that matches nothing should not stay in the
    /// catalog claiming a typo that is gone.
    /// </remarks>
    Task<bool> RetireAsync(
        SourceId sourceId,
        Guid correctionId,
        CancellationToken cancellationToken);
}
