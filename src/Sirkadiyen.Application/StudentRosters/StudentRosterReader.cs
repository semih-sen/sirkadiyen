using System.Globalization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Reads one acquired student list into the entries a lookup can search.
/// </summary>
/// <remarks>
/// This is a backend concern and deliberately not a parser-service one. The
/// Python parser interprets schedule documents and must never receive student
/// identities (ADR-085). What the two share is the acquisition layer: a roster
/// arrives as the same <see cref="NormalizedSpreadsheetSnapshot"/> a schedule
/// source does, so no second spreadsheet reader exists.
/// <para>
/// The reader states what the document states. It restores a leading zero the
/// spreadsheet dropped, because that recovers a value the file lost rather than
/// inventing one, and it refuses everything else it does not recognize.
/// </para>
/// </remarks>
public sealed class StudentRosterReader
{
    public StudentRosterReading Read(
        StudentRosterDefinition definition,
        NormalizedSpreadsheetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(snapshot);

        List<StudentRosterWarning> warnings = [];

        NormalizedWorksheet? worksheet = snapshot.Worksheets.FirstOrDefault(
            sheet => string.Equals(
                Collapse(sheet.Title),
                Collapse(definition.Layout.WorksheetTitle),
                StringComparison.Ordinal));

        if (worksheet is null)
        {
            string present = string.Join(
                ", ",
                snapshot.Worksheets.Select(static sheet => $"'{sheet.Title}'"));

            warnings.Add(new StudentRosterWarning
            {
                Code = StudentRosterWarningCode.WorksheetMissing,
                Message = $"Worksheet '{definition.Layout.WorksheetTitle}' is not in the document, "
                    + $"which holds {present}.",
            });

            return Empty(definition, warnings);
        }

        Dictionary<(int Row, int Column), NormalizedCell> cells = worksheet.Cells
            .ToDictionary(static cell => (cell.RowIndex, cell.ColumnIndex));

        int headerRowIndex = definition.Layout.HeaderRow - 1;
        Dictionary<string, int> columns = HeaderColumns(worksheet, headerRowIndex, cells);

        if (!TryResolveColumns(definition, columns, warnings, out ResolvedColumns resolved))
        {
            return Empty(definition, warnings);
        }

        List<StudentRosterEntry> entries = [];
        List<StudentRosterRefusedRow> refused = [];

        for (int rowIndex = headerRowIndex + 1; rowIndex < worksheet.RowCount; rowIndex++)
        {
            ReadRow(
                worksheet,
                cells,
                resolved,
                definition.StudentNumberProgramPrefix,
                rowIndex,
                entries,
                refused,
                warnings);
        }

        FlagDuplicates(entries, warnings);

        return new StudentRosterReading
        {
            RosterId = definition.RosterId,
            AcademicYear = definition.AcademicYear,
            ClassYear = definition.ClassYear,
            ProgramLanguage = definition.ProgramLanguage,
            Entries = entries,
            RefusedRows = refused,
            Warnings = warnings,
        };
    }

    private static void ReadRow(
        NormalizedWorksheet worksheet,
        IReadOnlyDictionary<(int Row, int Column), NormalizedCell> cells,
        ResolvedColumns resolved,
        string? programPrefix,
        int rowIndex,
        List<StudentRosterEntry> entries,
        List<StudentRosterRefusedRow> refused,
        List<StudentRosterWarning> warnings)
    {
        int rowNumber = rowIndex + 1;
        NormalizedCell? numberCell = Cell(cells, rowIndex, resolved.StudentNumber);
        string givenName = Text(Cell(cells, rowIndex, resolved.GivenName));
        string familyName = Text(Cell(cells, rowIndex, resolved.FamilyName));

        bool statesAnything = Text(numberCell).Length > 0
            || givenName.Length > 0
            || familyName.Length > 0
            || resolved.Dimensions.Any(dimension =>
                Text(Cell(cells, rowIndex, dimension.ColumnIndex)).Length > 0);

        if (!statesAnything)
        {
            // A row the document left entirely blank states no student, so there
            // is nothing in it to refuse. Every grouped list ends in a run of
            // them, because the merged group cell stretches past the last name.
            return;
        }

        if (!TryReadStudentNumber(
                numberCell,
                rowNumber,
                warnings,
                out string studentNumber,
                out StudentRosterRefusedRow? refusal))
        {
            refused.Add(refusal!);
            return;
        }

        if (programPrefix is { Length: > 0 } prefix
            && !studentNumber.StartsWith(prefix, StringComparison.Ordinal))
        {
            // A shared document holds another program's students too. A row whose
            // number is not this program's is not this list's to state — a sibling
            // roster claims it — so it is skipped silently rather than refused
            // (ADR-145).
            return;
        }

        Dictionary<string, string> selectors = new(StringComparer.Ordinal);
        foreach (ResolvedDimension dimension in resolved.Dimensions)
        {
            if (TryReadDimension(worksheet, cells, dimension, rowIndex, warnings, out string value))
            {
                selectors[dimension.Column.Dimension] = value;
            }
        }

        entries.Add(new StudentRosterEntry
        {
            StudentNumber = studentNumber,
            GivenName = givenName,
            FamilyName = familyName,
            Selectors = selectors,
            RowNumber = rowNumber,
        });
    }

    /// <summary>
    /// Reads the student number, restoring a leading zero the spreadsheet dropped
    /// by storing the value as a number.
    /// </summary>
    /// <remarks>
    /// Padding is unambiguous only because every valid number is exactly ten
    /// digits, so a nine-digit numeric value is missing exactly one zero. A value
    /// that is still not ten digits after padding is refused rather than adjusted
    /// further.
    /// </remarks>
    private static bool TryReadStudentNumber(
        NormalizedCell? cell,
        int rowNumber,
        List<StudentRosterWarning> warnings,
        out string studentNumber,
        out StudentRosterRefusedRow? refusal)
    {
        studentNumber = string.Empty;
        refusal = null;

        CellScalar? value = cell?.EffectiveValue ?? cell?.UserEnteredValue;
        string raw = Text(cell);

        if (raw.Length == 0)
        {
            refusal = new StudentRosterRefusedRow
            {
                RowNumber = rowNumber,
                Code = StudentRosterWarningCode.StudentNumberMissing,
                Message = $"Row {rowNumber} states a student but no student number.",
            };
            return false;
        }

        if (value is { Kind: CellScalarKind.Number, NumberValue: { } number })
        {
            if (number != decimal.Truncate(number) || number < 0)
            {
                refusal = new StudentRosterRefusedRow
                {
                    RowNumber = rowNumber,
                    Code = StudentRosterWarningCode.StudentNumberMalformed,
                    Message = $"Row {rowNumber} writes '{raw}' where a student number belongs.",
                };
                return false;
            }

            string digits = number.ToString("F0", CultureInfo.InvariantCulture);
            if (digits.Length < StudentProfile.StudentNumberLength)
            {
                raw = digits.PadLeft(StudentProfile.StudentNumberLength, '0');
                warnings.Add(new StudentRosterWarning
                {
                    Code = StudentRosterWarningCode.StudentNumberLeadingZeroRestored,
                    Message = $"Row {rowNumber} stores its student number as a number, so the "
                        + $"spreadsheet dropped its leading zero; '{digits}' is read as '{raw}'.",
                    A1Address = cell?.A1Address,
                });
            }
            else
            {
                raw = digits;
            }
        }

        if (raw.Length != StudentProfile.StudentNumberLength || !raw.All(char.IsAsciiDigit))
        {
            refusal = new StudentRosterRefusedRow
            {
                RowNumber = rowNumber,
                Code = StudentRosterWarningCode.StudentNumberMalformed,
                Message = $"Row {rowNumber} writes '{raw}', which is not "
                    + $"{StudentProfile.StudentNumberLength} digits.",
            };
            return false;
        }

        studentNumber = raw;
        return true;
    }

    private static bool TryReadDimension(
        NormalizedWorksheet worksheet,
        IReadOnlyDictionary<(int Row, int Column), NormalizedCell> cells,
        ResolvedDimension dimension,
        int rowIndex,
        List<StudentRosterWarning> warnings,
        out string mapped)
    {
        mapped = string.Empty;
        NormalizedCell? cell = Cell(cells, rowIndex, dimension.ColumnIndex);
        string stated = Text(cell);

        if (stated.Length == 0 && dimension.Column.StatedOncePerMergedRun)
        {
            GridRange? run = worksheet.MergedRanges.FirstOrDefault(range =>
                range.StartColumnIndex <= dimension.ColumnIndex
                && dimension.ColumnIndex < range.EndColumnIndexExclusive
                && range.StartRowIndex <= rowIndex
                && rowIndex < range.EndRowIndexExclusive);

            if (run is not null)
            {
                cell = Cell(cells, run.StartRowIndex, run.StartColumnIndex);
                stated = Text(cell);
            }
        }

        if (stated.Length == 0)
        {
            warnings.Add(new StudentRosterWarning
            {
                Code = StudentRosterWarningCode.DimensionValueUnstated,
                Message = $"Row {rowIndex + 1} states no '{dimension.Column.Dimension}', and no "
                    + "merged run covers the cell, so none is suggested for that student.",
                A1Address = A1(rowIndex, dimension.ColumnIndex),
            });
            return false;
        }

        if (!dimension.Column.ValueMap.TryGetValue(stated, out string? value))
        {
            warnings.Add(new StudentRosterWarning
            {
                Code = StudentRosterWarningCode.UnmappedDimensionValue,
                Message = $"'{stated}' is not a value the '{dimension.Column.Dimension}' column is "
                    + "declared to write, so it is not suggested.",
                A1Address = cell?.A1Address ?? A1(rowIndex, dimension.ColumnIndex),
            });
            return false;
        }

        mapped = value;
        return true;
    }

    /// <summary>
    /// Reports a student number two rows share, which makes it unusable for
    /// lookup rather than resolvable to one of them (ADR-085).
    /// </summary>
    private static void FlagDuplicates(
        IReadOnlyList<StudentRosterEntry> entries,
        List<StudentRosterWarning> warnings)
    {
        foreach (IGrouping<string, StudentRosterEntry> group in entries
            .GroupBy(static entry => entry.StudentNumber, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            string rows = string.Join(", ", group.Select(static entry => entry.RowNumber));
            warnings.Add(new StudentRosterWarning
            {
                Code = StudentRosterWarningCode.DuplicateStudentNumber,
                Message = $"Student number '{group.Key}' is written on rows {rows}, so the list "
                    + "does not say which student it means.",
            });
        }
    }

    private static bool TryResolveColumns(
        StudentRosterDefinition definition,
        IReadOnlyDictionary<string, int> columns,
        List<StudentRosterWarning> warnings,
        out ResolvedColumns resolved)
    {
        resolved = default!;
        StudentRosterLayout layout = definition.Layout;

        int? number = Find(layout.StudentNumberHeader);
        int? given = Find(layout.GivenNameHeader);
        int? family = Find(layout.FamilyNameHeader);

        List<ResolvedDimension> dimensions = [];
        foreach (StudentRosterDimensionColumn column in layout.DimensionColumns)
        {
            // A column the document leaves without a header is addressed by its
            // letter instead; every other column is found by its header text so it
            // survives a reorder (ADR-145).
            int? index = column.ColumnLetter is { Length: > 0 } letter
                ? ColumnIndexOf(letter)
                : Find(column.Header!);
            if (index is { } resolvedIndex)
            {
                dimensions.Add(new ResolvedDimension(column, resolvedIndex));
            }
        }

        if (number is null
            || given is null
            || family is null
            || dimensions.Count != layout.DimensionColumns.Count)
        {
            return false;
        }

        resolved = new ResolvedColumns(number.Value, given.Value, family.Value, dimensions);
        return true;

        int? Find(string header)
        {
            if (columns.TryGetValue(Collapse(header), out int index))
            {
                return index;
            }

            warnings.Add(new StudentRosterWarning
            {
                Code = StudentRosterWarningCode.ColumnMissing,
                Message = $"Column '{header}' is not on row {layout.HeaderRow} of worksheet "
                    + $"'{layout.WorksheetTitle}'.",
            });
            return null;
        }
    }

    private static Dictionary<string, int> HeaderColumns(
        NormalizedWorksheet worksheet,
        int headerRowIndex,
        IReadOnlyDictionary<(int Row, int Column), NormalizedCell> cells)
    {
        Dictionary<string, int> columns = new(StringComparer.Ordinal);
        for (int column = 0; column < worksheet.ColumnCount; column++)
        {
            string header = Collapse(Text(Cell(cells, headerRowIndex, column)));
            if (header.Length > 0)
            {
                columns.TryAdd(header, column);
            }
        }

        return columns;
    }

    private static StudentRosterReading Empty(
        StudentRosterDefinition definition,
        IReadOnlyList<StudentRosterWarning> warnings) => new()
        {
            RosterId = definition.RosterId,
            AcademicYear = definition.AcademicYear,
            ClassYear = definition.ClassYear,
            ProgramLanguage = definition.ProgramLanguage,
            Warnings = warnings,
        };

    private static NormalizedCell? Cell(
        IReadOnlyDictionary<(int Row, int Column), NormalizedCell> cells,
        int row,
        int column) => cells.TryGetValue((row, column), out NormalizedCell? cell) ? cell : null;

    /// <summary>
    /// The cell's text. A number is rendered without a decimal point, because a
    /// spreadsheet that stored a student number numerically hands back
    /// <c>101240072</c> and not <c>101240072.0</c>.
    /// </summary>
    private static string Text(NormalizedCell? cell)
    {
        CellScalar? value = cell?.EffectiveValue ?? cell?.UserEnteredValue;
        string? text = value?.Kind switch
        {
            CellScalarKind.Text => value.TextValue,
            CellScalarKind.Number =>
                value.NumberValue?.ToString("0.############", CultureInfo.InvariantCulture),
            CellScalarKind.Boolean => value.BooleanValue?.ToString(),
            _ => cell?.FormattedValue,
        };

        return (text ?? cell?.FormattedValue ?? string.Empty).Trim();
    }

    /// <summary>
    /// Trims and collapses internal whitespace runs, because the published lists
    /// write <c>Ad </c> with a trailing space and <c>Genel  Alt Grup</c> with two.
    /// </summary>
    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>The zero-based column index of a spreadsheet column letter.</summary>
    private static int ColumnIndexOf(string letter)
    {
        int index = 0;
        foreach (char character in letter.Trim().ToUpperInvariant())
        {
            index = (index * 26) + (character - 'A' + 1);
        }

        return index - 1;
    }

    private static string A1(int rowIndex, int columnIndex)
    {
        int column = columnIndex + 1;
        string letters = string.Empty;
        while (column > 0)
        {
            column--;
            letters = (char)('A' + (column % 26)) + letters;
            column /= 26;
        }

        return letters + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
    }

    private sealed record ResolvedColumns(
        int StudentNumber,
        int GivenName,
        int FamilyName,
        IReadOnlyList<ResolvedDimension> Dimensions);

    private sealed record ResolvedDimension(StudentRosterDimensionColumn Column, int ColumnIndex);
}
