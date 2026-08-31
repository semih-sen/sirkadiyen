namespace Sirkadiyen.Domain.Scheduling.Sources;

/// <summary>
/// One date an operator has decided a source states wrongly (ADR-139).
/// </summary>
/// <remarks>
/// The parser repairs a mistyped year on its own when the dates around it leave
/// exactly one reading. When they do not — the cell contradicts its own weekday,
/// or two years fit equally well — it publishes the date as written and reports
/// what the readings were. This is where the operator's answer to that question
/// lives.
/// <para>
/// It is deliberately not an edit to a parsed record. A parse is a pure function
/// of its snapshot, its parser profile and its source context (ADR-017), and
/// editing records afterwards would break that: the next poll would re-parse the
/// same document and undo the correction. A correction is source context instead,
/// so re-parsing applies it again and the pipeline needs no special case.
/// </para>
/// <para>
/// It is keyed by the wrong value rather than by a cell address. The document is
/// re-acquired on every poll and its rows move; the mistyped value does not. A
/// correction therefore applies wherever the source writes that date, which is
/// the intended reading of "this document says 2020-11-20 and means 2026-11-20".
/// The consequence is deliberate and worth stating: if a source ever legitimately
/// states the corrected-from date, that occurrence moves too. The values these
/// corrections name are dates outside the source's own academic year, so no
/// legitimate row can carry one.
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

        if (original == corrected)
        {
            // A correction that changes nothing is a configuration mistake rather
            // than a harmless no-op: it would sit in the catalog claiming a typo
            // that is not there, and every parse would report it.
            throw new ArgumentException(
                "A date correction must change the date it corrects.",
                nameof(corrected));
        }

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
