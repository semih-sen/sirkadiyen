namespace Sirkadiyen.Domain.Scheduling.Sources;

/// <summary>
/// One date an operator has ruled on because the source states it out of
/// sequence (ADR-139).
/// </summary>
/// <remarks>
/// The parser repairs a mistyped year on its own when the dates around it leave
/// exactly one reading. When they do not — the cell contradicts its own weekday,
/// or two years fit equally well — it publishes the date as written and reports
/// what the readings were. This is where the operator's answer to that question
/// lives. That answer is usually a different date (the year was mistyped), but it
/// may be the same one: the operator has read the document and confirmed it
/// states the date correctly even though it sits out of sequence, and that
/// confirmation stops every later parse from reporting it.
/// <para>
/// It is deliberately not an edit to a parsed record. A parse is a pure function
/// of its snapshot, its parser profile and its source context (ADR-017), and
/// editing records afterwards would break that: the next poll would re-parse the
/// same document and undo the correction. A correction is source context instead,
/// so re-parsing applies it again and the pipeline needs no special case.
/// </para>
/// <para>
/// It is keyed by the stated value rather than by a cell address. The document is
/// re-acquired on every poll and its rows move; the value the operator ruled on
/// does not. A correction therefore applies wherever the source writes that date,
/// which is the intended reading of "this document says 2020-11-20 and means
/// 2026-11-20". Where the correction changes the date, the consequence is
/// deliberate and worth stating: if a source ever legitimately states the
/// corrected-from date, that occurrence moves too — but the values a substitution
/// names are dates outside the source's own academic year, so no legitimate row
/// can carry one. A confirmation, where the corrected date equals the original,
/// has no such consequence: it reads the value as itself, so a legitimate row
/// stating it is left exactly as written.
/// </para>
/// </remarks>
public sealed class ScheduleSourceDateCorrection
{
    /// <summary>How much of an operator's note is kept.</summary>
    public const int MaximumNoteLength = 500;

    private ScheduleSourceDateCorrection()
    {
        // Materialization constructor.
        DecidedBy = string.Empty;
        Note = string.Empty;
    }

    public ScheduleSourceDateCorrection(
        SourceId sourceId,
        DateOnly original,
        DateOnly corrected,
        string decidedBy,
        DateTimeOffset decidedAtUtc,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        // Original and corrected may be the same date. That is not a no-op: it is
        // the operator confirming the source states this date correctly even
        // though it sits out of sequence, which stops every later parse from
        // reporting it as an anomaly (ADR-139).

        Id = Guid.CreateVersion7();
        SourceId = sourceId;
        Original = original;
        Corrected = corrected;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        Note = Truncate(note);
    }

    public Guid Id { get; private set; }

    public SourceId SourceId { get; private set; }

    /// <summary>The date the document resolves to today.</summary>
    public DateOnly Original { get; private set; }

    /// <summary>The date it means.</summary>
    public DateOnly Corrected { get; private set; }

    /// <summary>
    /// Who accepted it, so a published date that no document states can always be
    /// traced to a person.
    /// </summary>
    public string DecidedBy { get; private set; }

    public DateTimeOffset DecidedAtUtc { get; private set; }

    /// <summary>Why, in the operator's own words. Empty when they gave none.</summary>
    public string Note { get; private set; }

    private static string Truncate(string? note) =>
        note is null or { Length: 0 }
            ? string.Empty
            : note.Length <= MaximumNoteLength
                ? note
                : note[..MaximumNoteLength];
}
