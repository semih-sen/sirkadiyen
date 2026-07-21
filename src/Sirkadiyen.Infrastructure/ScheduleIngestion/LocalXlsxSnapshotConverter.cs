using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using ContractGridRange = Sirkadiyen.Contracts.Spreadsheets.GridRange;
using ContractIndexRange = Sirkadiyen.Contracts.Spreadsheets.IndexRange;
using OpenXmlCell = DocumentFormat.OpenXml.Spreadsheet.Cell;
using OpenXmlCellFormat = DocumentFormat.OpenXml.Spreadsheet.CellFormat;
using OpenXmlFont = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace Sirkadiyen.Infrastructure.ScheduleIngestion;

public sealed partial class LocalXlsxSnapshotConverter
{
    public NormalizedSpreadsheetSnapshot Convert(
        string xlsxPath,
        AcquireSpreadsheetSnapshotRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xlsxPath);
        ArgumentNullException.ThrowIfNull(request);

        using SpreadsheetDocument document = SpreadsheetDocument.Open(xlsxPath, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The XLSX file does not contain a workbook part.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The XLSX file does not contain a workbook.");
        Sheets sheets = workbook.Sheets
            ?? throw new InvalidDataException("The XLSX workbook does not contain worksheets.");
        Stylesheet? stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        List<AcquisitionDiagnostic> diagnostics =
        [
            new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Code = "snapshot.local_xlsx_fixture",
                Message = "Snapshot was converted from a local XLSX fixture, not acquired through Google Sheets API.",
            },
        ];

        if (workbook.WorkbookProperties?.Date1904?.Value == true)
        {
            diagnostics.Add(new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "snapshot.xlsx_1904_date_system",
                Message = "The XLSX workbook uses the unsupported 1904 date system.",
            });
        }

        List<NormalizedWorksheet> worksheets = [];
        int sheetIndex = 0;
        foreach (Sheet sheet in sheets.Elements<Sheet>())
        {
            if (sheet.Id?.Value is not { Length: > 0 } relationshipId
                || sheet.Name?.Value is not { Length: > 0 } title
                || sheet.SheetId?.Value is not uint sheetId)
            {
                diagnostics.Add(new AcquisitionDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "snapshot.xlsx_malformed_sheet",
                    Message = "A worksheet with missing identity metadata was omitted.",
                });
                sheetIndex++;
                continue;
            }

            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                diagnostics.Add(new AcquisitionDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "snapshot.xlsx_unsupported_sheet",
                    Message = "A non-worksheet workbook part was omitted.",
                    SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
                });
                sheetIndex++;
                continue;
            }

            worksheets.Add(MapWorksheet(
                worksheetPart,
                sheet,
                sheetIndex,
                title,
                sheetId,
                stylesheet,
                sharedStrings,
                diagnostics));
            sheetIndex++;
        }

        return new NormalizedSpreadsheetSnapshot
        {
            ContractVersion = SpreadsheetContractVersions.V1,
            SourceId = request.SourceId,
            SnapshotId = request.SnapshotId,
            SpreadsheetId = request.SpreadsheetId,
            AcquiredAtUtc = request.AcquiredAtUtc,
            ContentHash = SnapshotContentHasher.Compute(worksheets, diagnostics),
            ContentHashAlgorithm = SnapshotContentHasher.Algorithm,
            Worksheets = worksheets,
            Diagnostics = diagnostics,
        };
    }

    private static NormalizedWorksheet MapWorksheet(
        WorksheetPart worksheetPart,
        Sheet sheet,
        int sheetIndex,
        string title,
        uint sheetId,
        Stylesheet? stylesheet,
        SharedStringTable? sharedStrings,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        Worksheet worksheet = worksheetPart.Worksheet
            ?? throw new InvalidDataException("The worksheet part does not contain a worksheet.");
        IReadOnlyDictionary<string, string> comments = ReadComments(worksheetPart);
        List<NormalizedCell> cells = [];
        List<int> hiddenRows = [];
        List<int> hiddenColumns = [];
        int semanticRowCount = 0;
        int semanticColumnCount = 0;

        foreach (Row row in worksheet.Descendants<Row>())
        {
            if (row.Hidden?.Value == true && row.RowIndex?.Value is uint hiddenRow and > 0)
            {
                hiddenRows.Add(checked((int)hiddenRow - 1));
            }

            foreach (OpenXmlCell cell in row.Elements<OpenXmlCell>())
            {
                if (!TryParseCellReference(cell.CellReference?.Value, out int rowIndex, out int columnIndex))
                {
                    diagnostics.Add(new AcquisitionDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Code = "snapshot.xlsx_cell_without_reference",
                        Message = "An XLSX cell without a valid A1 reference was omitted.",
                        SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
                    });
                    continue;
                }

                NormalizedCell? normalized = MapCell(
                    cell,
                    rowIndex,
                    columnIndex,
                    stylesheet,
                    sharedStrings,
                    comments,
                    sheetId,
                    diagnostics);
                if (normalized is not null)
                {
                    cells.Add(normalized);
                    if (HasContentEvidence(normalized))
                    {
                        semanticRowCount = Math.Max(semanticRowCount, rowIndex + 1);
                        semanticColumnCount = Math.Max(semanticColumnCount, columnIndex + 1);
                    }
                }
            }
        }

        foreach (Column column in worksheet.Descendants<Column>().Where(
            static column => column.Hidden?.Value == true))
        {
            uint? minimum = column.Min?.Value;
            uint? maximum = column.Max?.Value;
            if (minimum is null or 0 || maximum is null or 0)
            {
                continue;
            }

            for (uint index = minimum.Value; index <= maximum.Value; index++)
            {
                hiddenColumns.Add(checked((int)index - 1));
            }
        }

        List<ContractGridRange> mergedRanges = [];
        foreach (MergeCell merge in worksheet.Descendants<MergeCell>())
        {
            if (!TryParseRangeReference(merge.Reference?.Value, out ContractGridRange range))
            {
                diagnostics.Add(new AcquisitionDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "snapshot.xlsx_invalid_merge",
                    Message = "An XLSX merge with an invalid range was omitted.",
                    SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
                    Range = merge.Reference?.Value,
                });
                continue;
            }

            mergedRanges.Add(range);
            semanticRowCount = Math.Max(semanticRowCount, range.EndRowIndexExclusive);
            semanticColumnCount = Math.Max(semanticColumnCount, range.EndColumnIndexExclusive);
        }

        string? dimensionReference = worksheet.SheetDimension?.Reference?.Value;
        if (TryParseRangeReference(dimensionReference, out ContractGridRange dimension))
        {
            if (semanticRowCount == 0 || semanticColumnCount == 0)
            {
                semanticRowCount = dimension.EndRowIndexExclusive;
                semanticColumnCount = dimension.EndColumnIndexExclusive;
            }
            else if (dimension.EndRowIndexExclusive > semanticRowCount
                || dimension.EndColumnIndexExclusive > semanticColumnCount)
            {
                diagnostics.Add(new AcquisitionDiagnostic
                {
                    Severity = DiagnosticSeverity.Information,
                    Code = "snapshot.xlsx_dimension_trimmed",
                    Message = "Formatting-only rows or columns beyond the semantic used range were omitted.",
                    SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
                    Range = dimensionReference,
                });
            }
        }

        string? semanticRange = semanticRowCount > 0 && semanticColumnCount > 0
            ? $"A1:{ToA1Address(semanticRowCount - 1, semanticColumnCount - 1)}"
            : null;

        Pane? pane = worksheet.SheetViews?.Elements<SheetView>()
            .Select(static view => view.Pane)
            .FirstOrDefault(static value => value is not null);
        int frozenRows = IsFrozen(pane) ? ToNonNegativeInt(pane?.VerticalSplit?.Value) : 0;
        int frozenColumns = IsFrozen(pane) ? ToNonNegativeInt(pane?.HorizontalSplit?.Value) : 0;

        return new NormalizedWorksheet
        {
            SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
            Title = title,
            Index = sheetIndex,
            RowCount = semanticRowCount,
            ColumnCount = semanticColumnCount,
            FrozenRowCount = frozenRows,
            FrozenColumnCount = frozenColumns,
            Hidden = sheet.State?.Value == SheetStateValues.Hidden
                || sheet.State?.Value == SheetStateValues.VeryHidden,
            RequestedRanges = semanticRange is null ? [] : [semanticRange],
            MergedRanges = mergedRanges
                .OrderBy(static range => range.StartRowIndex)
                .ThenBy(static range => range.StartColumnIndex)
                .ToList(),
            HiddenRows = CollapseIndexes(hiddenRows.Where(index => index < semanticRowCount)),
            HiddenColumns = CollapseIndexes(hiddenColumns.Where(index => index < semanticColumnCount)),
            Cells = cells
                .Where(cell => cell.RowIndex < semanticRowCount
                    && cell.ColumnIndex < semanticColumnCount)
                .OrderBy(static cell => cell.RowIndex)
                .ThenBy(static cell => cell.ColumnIndex)
                .ToList(),
        };
    }

    private static NormalizedCell? MapCell(
        OpenXmlCell cell,
        int rowIndex,
        int columnIndex,
        Stylesheet? stylesheet,
        SharedStringTable? sharedStrings,
        IReadOnlyDictionary<string, string> comments,
        uint sheetId,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        CellScalar? scalar = MapScalar(cell, sharedStrings, sheetId, diagnostics);
        NormalizedCellFormat? format = MapFormat(cell.StyleIndex?.Value, stylesheet);
        string? formula = cell.CellFormula?.Text;
        comments.TryGetValue(ToA1Address(rowIndex, columnIndex), out string? note);

        if (scalar is null && format is null && formula is null && note is null)
        {
            return null;
        }

        return new NormalizedCell
        {
            RowIndex = rowIndex,
            ColumnIndex = columnIndex,
            A1Address = ToA1Address(rowIndex, columnIndex),
            UserEnteredValue = formula is null ? scalar : null,
            EffectiveValue = scalar,
            Formula = formula,
            FormattedValue = FormatDisplayValue(scalar),
            Note = note,
            EffectiveFormat = format,
        };
    }

    private static CellScalar? MapScalar(
        OpenXmlCell cell,
        SharedStringTable? sharedStrings,
        uint sheetId,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        string? raw = cell.CellValue?.Text;
        CellValues? dataType = cell.DataType?.Value;

        if (dataType == CellValues.InlineString)
        {
            return TextScalar(cell.InlineString?.InnerText ?? string.Empty);
        }

        if (dataType == CellValues.SharedString)
        {
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 0
                && sharedStrings is not null)
            {
                SharedStringItem? item = sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index);
                if (item is not null)
                {
                    return TextScalar(item.InnerText);
                }
            }

            diagnostics.Add(new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "snapshot.xlsx_invalid_shared_string",
                Message = "A cell references a missing shared string.",
                SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
                Range = cell.CellReference?.Value,
            });
            return null;
        }

        if (dataType == CellValues.Boolean)
        {
            return new CellScalar
            {
                Kind = CellScalarKind.Boolean,
                BooleanValue = raw is "1" or "true" or "TRUE",
            };
        }

        if (dataType == CellValues.Error)
        {
            return new CellScalar { Kind = CellScalarKind.Error, ErrorValue = raw ?? "Unknown error" };
        }

        if (dataType == CellValues.String)
        {
            return TextScalar(raw ?? string.Empty);
        }

        if (raw is null)
        {
            return null;
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
        {
            return new CellScalar { Kind = CellScalarKind.Number, NumberValue = number };
        }

        diagnostics.Add(new AcquisitionDiagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = "snapshot.xlsx_invalid_number",
            Message = "A numeric XLSX cell contains an invalid number.",
            SheetId = sheetId.ToString(CultureInfo.InvariantCulture),
            Range = cell.CellReference?.Value,
        });
        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadComments(WorksheetPart worksheetPart)
    {
        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        WorksheetCommentsPart? commentsPart = worksheetPart.WorksheetCommentsPart;
        if (commentsPart is null)
        {
            return comments;
        }

        CommentList? commentList = commentsPart.Comments?.CommentList;
        if (commentList is null)
        {
            return comments;
        }

        foreach (Comment comment in commentList.Elements<Comment>())
        {
            if (comment.Reference?.Value is { Length: > 0 } reference)
            {
                comments[reference] = comment.CommentText?.InnerText ?? string.Empty;
            }
        }

        return comments;
    }

    private static CellScalar TextScalar(string value) => new()
    {
        Kind = CellScalarKind.Text,
        TextValue = value,
    };

    private static string? FormatDisplayValue(CellScalar? scalar) => scalar?.Kind switch
    {
        CellScalarKind.Text => scalar.TextValue,
        CellScalarKind.Boolean => scalar.BooleanValue?.ToString(CultureInfo.InvariantCulture),
        CellScalarKind.Error => scalar.ErrorValue,
        _ => null,
    };

    private static bool HasContentEvidence(NormalizedCell cell) =>
        cell.UserEnteredValue is not null
        || cell.EffectiveValue is not null
        || cell.Formula is not null
        || cell.Note is not null;

    private static NormalizedCellFormat? MapFormat(uint? styleIndex, Stylesheet? stylesheet)
    {
        if (styleIndex is null or 0 || stylesheet?.CellFormats is null)
        {
            return null;
        }

        OpenXmlCellFormat? cellFormat = stylesheet.CellFormats
            .Elements<OpenXmlCellFormat>()
            .ElementAtOrDefault(checked((int)styleIndex.Value));
        if (cellFormat is null)
        {
            return null;
        }

        uint numberFormatId = cellFormat.NumberFormatId?.Value ?? 0;
        string? numberFormatPattern = GetNumberFormatPattern(numberFormatId, stylesheet);
        OpenXmlFont? font = GetIndexedElement<OpenXmlFont>(stylesheet.Fonts, cellFormat.FontId?.Value);
        Fill? fill = GetIndexedElement<Fill>(stylesheet.Fills, cellFormat.FillId?.Value);
        PatternFill? patternFill = fill?.PatternFill;
        Alignment? alignment = cellFormat.Alignment;

        return new NormalizedCellFormat
        {
            BackgroundColor = patternFill?.PatternType?.Value == PatternValues.None
                ? null
                : FormatColor(patternFill?.ForegroundColor),
            ForegroundColor = FormatColor(font?.Color),
            Bold = font?.Bold is not null,
            Italic = font?.Italic is not null,
            Strikethrough = font?.Strike is not null,
            HorizontalAlignment = alignment?.Horizontal?.Value.ToString().ToUpperInvariant(),
            VerticalAlignment = alignment?.Vertical?.Value.ToString().ToUpperInvariant(),
            NumberFormatType = ClassifyNumberFormat(numberFormatId, numberFormatPattern),
            NumberFormatPattern = numberFormatPattern,
        };
    }

    private static T? GetIndexedElement<T>(OpenXmlCompositeElement? collection, uint? index)
        where T : OpenXmlElement => index is null || collection is null
            ? null
            : collection.Elements<T>().ElementAtOrDefault(checked((int)index.Value));

    private static string? GetNumberFormatPattern(uint numberFormatId, Stylesheet stylesheet)
    {
        string? custom = stylesheet.NumberingFormats?
            .Elements<NumberingFormat>()
            .FirstOrDefault(format => format.NumberFormatId?.Value == numberFormatId)
            ?.FormatCode?.Value;
        return custom ?? BuiltInNumberFormat(numberFormatId);
    }

    private static string? BuiltInNumberFormat(uint id) => id switch
    {
        0 => "General",
        1 => "0",
        2 => "0.00",
        9 => "0%",
        10 => "0.00%",
        14 => "mm-dd-yy",
        15 => "d-mmm-yy",
        16 => "d-mmm",
        17 => "mmm-yy",
        18 => "h:mm AM/PM",
        19 => "h:mm:ss AM/PM",
        20 => "h:mm",
        21 => "h:mm:ss",
        22 => "m/d/yy h:mm",
        45 => "mm:ss",
        46 => "[h]:mm:ss",
        47 => "mmss.0",
        49 => "@",
        _ => null,
    };

    private static string ClassifyNumberFormat(uint id, string? pattern)
    {
        if (id == 49 || pattern == "@")
        {
            return "TEXT";
        }

        if (id is 9 or 10 || pattern?.Contains('%', StringComparison.Ordinal) == true)
        {
            return "PERCENT";
        }

        if (pattern is not null
            && (pattern.Contains('$', StringComparison.Ordinal)
                || pattern.Contains('₺', StringComparison.Ordinal)
                || pattern.Contains("[$", StringComparison.Ordinal)))
        {
            return "CURRENCY";
        }

        string normalized = NumberFormatDecorationRegex().Replace(pattern ?? string.Empty, string.Empty)
            .ToLowerInvariant();
        bool hasDate = id is >= 14 and <= 17 or >= 27 and <= 36 or >= 50 and <= 58
            || normalized.Contains('y', StringComparison.Ordinal)
            || normalized.Contains('d', StringComparison.Ordinal);
        bool hasTime = id is >= 18 and <= 21 or >= 45 and <= 47
            || normalized.Contains('h', StringComparison.Ordinal)
            || normalized.Contains('s', StringComparison.Ordinal);

        return (hasDate, hasTime) switch
        {
            (true, true) => "DATE_TIME",
            (true, false) => "DATE",
            (false, true) => "TIME",
            _ => "NUMBER",
        };
    }

    private static string? FormatColor(ColorType? color)
    {
        if (color?.Rgb?.Value is { Length: > 0 } rgb)
        {
            return NormalizeArgb(rgb);
        }

        if (color?.Theme?.Value is uint theme)
        {
            return $"theme:{theme.ToString(CultureInfo.InvariantCulture)}";
        }

        if (color?.Indexed?.Value is uint indexed)
        {
            return $"indexed:{indexed.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    private static string NormalizeArgb(string value)
    {
        string normalized = value.TrimStart('#').ToUpperInvariant();
        if (normalized.Length != 8)
        {
            return $"#{normalized}";
        }

        string alpha = normalized[..2];
        string rgb = normalized[2..];
        return alpha == "FF" ? $"#{rgb}" : $"#{rgb}{alpha}";
    }

    private static bool TryParseRangeReference(string? reference, out ContractGridRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        string[] endpoints = reference.Split(':', 2, StringSplitOptions.TrimEntries);
        if (!TryParseCellReference(endpoints[0], out int startRow, out int startColumn)
            || !TryParseCellReference(
                endpoints.Length == 2 ? endpoints[1] : endpoints[0],
                out int endRow,
                out int endColumn))
        {
            return false;
        }

        range = new ContractGridRange
        {
            StartRowIndex = Math.Min(startRow, endRow),
            EndRowIndexExclusive = Math.Max(startRow, endRow) + 1,
            StartColumnIndex = Math.Min(startColumn, endColumn),
            EndColumnIndexExclusive = Math.Max(startColumn, endColumn) + 1,
        };
        return true;
    }

    private static bool TryParseCellReference(
        string? reference,
        out int rowIndex,
        out int columnIndex)
    {
        rowIndex = 0;
        columnIndex = 0;
        Match match = CellReferenceRegex().Match(reference ?? string.Empty);
        if (!match.Success
            || !int.TryParse(
                match.Groups["row"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int oneBasedRow)
            || oneBasedRow <= 0)
        {
            return false;
        }

        int oneBasedColumn = 0;
        foreach (char letter in match.Groups["column"].ValueSpan)
        {
            oneBasedColumn = checked((oneBasedColumn * 26) + (char.ToUpperInvariant(letter) - 'A' + 1));
        }

        rowIndex = oneBasedRow - 1;
        columnIndex = oneBasedColumn - 1;
        return true;
    }

    private static string ToA1Address(int rowIndex, int columnIndex)
    {
        int column = columnIndex + 1;
        Span<char> buffer = stackalloc char[16];
        int position = buffer.Length;
        while (column > 0)
        {
            column--;
            buffer[--position] = (char)('A' + (column % 26));
            column /= 26;
        }

        return string.Concat(buffer[position..], (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<ContractIndexRange> CollapseIndexes(IEnumerable<int> source)
    {
        int[] indexes = source.Distinct().Order().ToArray();
        if (indexes.Length == 0)
        {
            return [];
        }

        List<ContractIndexRange> ranges = [];
        int start = indexes[0];
        int previous = start;
        foreach (int index in indexes.Skip(1))
        {
            if (index == previous + 1)
            {
                previous = index;
                continue;
            }

            ranges.Add(new ContractIndexRange { StartIndex = start, EndIndexExclusive = previous + 1 });
            start = index;
            previous = index;
        }

        ranges.Add(new ContractIndexRange { StartIndex = start, EndIndexExclusive = previous + 1 });
        return ranges;
    }

    private static bool IsFrozen(Pane? pane) =>
        pane?.State?.Value == PaneStateValues.Frozen
        || pane?.State?.Value == PaneStateValues.FrozenSplit;

    private static int ToNonNegativeInt(double? value) => value is > 0
        ? checked((int)Math.Floor(value.Value))
        : 0;

    [GeneratedRegex("^(?<column>[A-Za-z]+)(?<row>[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CellReferenceRegex();

    [GeneratedRegex("\\[[^]]*]|\"[^\"]*\"|\\\\.", RegexOptions.CultureInvariant)]
    private static partial Regex NumberFormatDecorationRegex();
}
