namespace Sirkadiyen.Application.StudentRosters;

/// <summary>What one roster document turned out to say.</summary>
/// <remarks>
/// A reading reports accepted entries, refused rows and warnings together. A row
/// the reader could not use is never dropped quietly: it appears in
/// <see cref="RefusedRows"/> with the reason, so an operator can see that a list
/// of 384 students yielded 383 and why (AI_GUIDELINE §9).
/// </remarks>
public sealed record StudentRosterReading
{
    public required string RosterId { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required Domain.Scheduling.Sources.ProgramLanguage ProgramLanguage { get; init; }

    public IReadOnlyList<StudentRosterEntry> Entries { get; init; } = [];

    public IReadOnlyList<StudentRosterRefusedRow> RefusedRows { get; init; } = [];

    public IReadOnlyList<StudentRosterWarning> Warnings { get; init; } = [];
}

/// <summary>One student the roster states.</summary>
public sealed record StudentRosterEntry
{
    public required string StudentNumber { get; init; }

    /// <summary>
    /// The name the list writes, which two of the four lists publish masked.
    /// </summary>
    /// <remarks>
    /// Held only for the duration of a lookup response. It is never written to
    /// the database, copied into a profile or logged (ADR-085).
    /// </remarks>
    public required string GivenName { get; init; }

    public required string FamilyName { get; init; }

    /// <summary>
    /// The profile selectors this list states for the student, keyed by
    /// dimension. A dimension the list does not state is absent, not empty.
    /// </summary>
    public IReadOnlyDictionary<string, string> Selectors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The one-based worksheet row, for operator diagnostics.</summary>
    public required int RowNumber { get; init; }
}

public sealed record StudentRosterRefusedRow
{
    public required int RowNumber { get; init; }

    public required StudentRosterWarningCode Code { get; init; }

    public required string Message { get; init; }
}

public sealed record StudentRosterWarning
{
    public required StudentRosterWarningCode Code { get; init; }

    public required string Message { get; init; }

    /// <summary>The cell the warning is about, when it is about one.</summary>
    public string? A1Address { get; init; }
}

public enum StudentRosterWarningCode
{
    /// <summary>
    /// The worksheet the layout names is not in the document.
    /// </summary>
    WorksheetMissing,

    /// <summary>
    /// A column the layout names is not on the header row.
    /// </summary>
    ColumnMissing,

    /// <summary>
    /// The cell holds a number rather than text, so the spreadsheet dropped the
    /// student number's leading zero. It is restored by left-padding to ten
    /// digits, which is unambiguous because every valid number is exactly ten
    /// digits long, and reported because the document is wrong even though the
    /// value is recoverable.
    /// </summary>
    StudentNumberLeadingZeroRestored,

    /// <summary>
    /// The value is not ten digits and no rule recovers it, so the row states no
    /// student and is refused.
    /// </summary>
    StudentNumberMalformed,

    /// <summary>The row states no student number at all.</summary>
    StudentNumberMissing,

    /// <summary>
    /// The same student number is written on two rows of one list. Both rows are
    /// kept and the number is unusable for lookup, because choosing one silently
    /// is exactly what ADR-085 forbids.
    /// </summary>
    DuplicateStudentNumber,

    /// <summary>
    /// The dimension column holds a value its map does not list. The selector is
    /// not suggested; the rest of the row still is.
    /// </summary>
    UnmappedDimensionValue,

    /// <summary>
    /// The dimension column's cell is empty and no merged range covers it, so
    /// the document states no group for this student. Nothing is carried down
    /// from the row above.
    /// </summary>
    DimensionValueUnstated,
}
