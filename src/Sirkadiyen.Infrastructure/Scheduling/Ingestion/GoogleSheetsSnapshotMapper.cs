using System.Globalization;
using Google.Apis.Sheets.v4.Data;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Contracts.Spreadsheets;
using ContractGridRange = Sirkadiyen.Contracts.Spreadsheets.GridRange;
using GoogleGridRange = Google.Apis.Sheets.v4.Data.GridRange;

namespace Sirkadiyen.Infrastructure.Scheduling.Ingestion;

public sealed class GoogleSheetsSnapshotMapper
{
    public NormalizedSpreadsheetSnapshot Map(
        Spreadsheet spreadsheet,
        AcquireSpreadsheetSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(spreadsheet.SpreadsheetId, request.SpreadsheetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Google Sheets returned a spreadsheet with an unexpected identifier.");
        }

        List<AcquisitionDiagnostic> diagnostics = [];
        List<Sheet> orderedSheets = (spreadsheet.Sheets ?? [])
            .OrderBy(static sheet => sheet.Properties?.Index ?? int.MaxValue)
            .ThenBy(static sheet => sheet.Properties?.SheetId ?? int.MaxValue)
            .ToList();
        Sheet? firstGridSheet = orderedSheets.FirstOrDefault(IsGridSheet);
        List<NormalizedWorksheet> worksheets = orderedSheets
            .Select(sheet => MapWorksheet(
                sheet,
                request.Ranges,
                ReferenceEquals(sheet, firstGridSheet),
                diagnostics))
            .Where(static worksheet => worksheet is not null)
            .Cast<NormalizedWorksheet>()
            .OrderBy(static worksheet => worksheet.Index)
            .ThenBy(static worksheet => worksheet.SheetId, StringComparer.Ordinal)
            .ToList();

        if (worksheets.Count == 0)
        {
            diagnostics.Add(new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "snapshot.no_grid_worksheets",
                Message = "The spreadsheet response did not contain any grid worksheets.",
            });
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

    private static NormalizedWorksheet? MapWorksheet(
        Sheet sheet,
        IReadOnlyList<string> requestedRanges,
        bool isFirstSheet,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        SheetProperties? properties = sheet.Properties;
        GridProperties? grid = properties?.GridProperties;
        if (properties?.SheetId is null || string.IsNullOrWhiteSpace(properties.Title) || grid is null)
        {
            diagnostics.Add(new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "snapshot.unsupported_sheet",
                Message = "A non-grid or malformed sheet was omitted from the normalized snapshot.",
                SheetId = properties?.SheetId?.ToString(CultureInfo.InvariantCulture),
            });
            return null;
        }

        string sheetId = properties.SheetId.Value.ToString(CultureInfo.InvariantCulture);
        Dictionary<(int Row, int Column), NormalizedCell> cells = [];
        HashSet<(int Row, int Column)> conflictingCells = [];
        List<int> hiddenRows = [];
        List<int> hiddenColumns = [];

        foreach (GridData data in sheet.Data ?? [])
        {
            int startRow = data.StartRow ?? 0;
            int startColumn = data.StartColumn ?? 0;

            AddHiddenIndexes(data.RowMetadata, startRow, hiddenRows);
            AddHiddenIndexes(data.ColumnMetadata, startColumn, hiddenColumns);

            for (int rowOffset = 0; rowOffset < (data.RowData?.Count ?? 0); rowOffset++)
            {
                RowData row = data.RowData![rowOffset];
                for (int columnOffset = 0; columnOffset < (row.Values?.Count ?? 0); columnOffset++)
                {
                    CellData cell = row.Values![columnOffset];
                    if (!HasEvidence(cell))
                    {
                        continue;
                    }

                    int rowIndex = startRow + rowOffset;
                    int columnIndex = startColumn + columnOffset;
                    (int Row, int Column) coordinate = (rowIndex, columnIndex);
                    if (conflictingCells.Contains(coordinate))
                    {
                        continue;
                    }

                    NormalizedCell normalizedCell = MapCell(cell, rowIndex, columnIndex);
                    if (cells.TryGetValue(coordinate, out NormalizedCell? existing))
                    {
                        if (existing != normalizedCell)
                        {
                            cells.Remove(coordinate);
                            conflictingCells.Add(coordinate);
                            diagnostics.Add(new AcquisitionDiagnostic
                            {
                                Severity = DiagnosticSeverity.Error,
                                Code = "snapshot.conflicting_cell_data",
                                Message = "Overlapping API ranges returned conflicting data for one cell.",
                                SheetId = sheetId,
                                Range = normalizedCell.A1Address,
                            });
                        }

                        continue;
                    }

                    cells.Add(coordinate, normalizedCell);
                }
            }
        }

        return new NormalizedWorksheet
        {
            SheetId = sheetId,
            Title = properties.Title,
            Index = properties.Index ?? 0,
            RowCount = grid.RowCount ?? 0,
            ColumnCount = grid.ColumnCount ?? 0,
            FrozenRowCount = grid.FrozenRowCount ?? 0,
            FrozenColumnCount = grid.FrozenColumnCount ?? 0,
            Hidden = properties.Hidden ?? false,
            RequestedRanges = SelectRanges(properties.Title, isFirstSheet, requestedRanges),
            MergedRanges = (sheet.Merges ?? [])
                .Where(HasCompleteBounds)
                .Select(MapRange)
                .OrderBy(static range => range.StartRowIndex)
                .ThenBy(static range => range.StartColumnIndex)
                .ThenBy(static range => range.EndRowIndexExclusive)
                .ThenBy(static range => range.EndColumnIndexExclusive)
                .ToList(),
            HiddenRows = CollapseIndexes(hiddenRows),
            HiddenColumns = CollapseIndexes(hiddenColumns),
            Cells = cells.Values
                .OrderBy(static cell => cell.RowIndex)
                .ThenBy(static cell => cell.ColumnIndex)
                .ToList(),
        };
    }

    private static NormalizedCell MapCell(CellData cell, int rowIndex, int columnIndex)
    {
        return new NormalizedCell
        {
            RowIndex = rowIndex,
            ColumnIndex = columnIndex,
            A1Address = ToA1Address(rowIndex, columnIndex),
            UserEnteredValue = MapScalar(cell.UserEnteredValue),
            EffectiveValue = MapScalar(cell.EffectiveValue),
            Formula = cell.UserEnteredValue?.FormulaValue,
            FormattedValue = cell.FormattedValue,
            Note = cell.Note,
            EffectiveFormat = MapFormat(cell.EffectiveFormat),
        };
    }

    private static CellScalar? MapScalar(ExtendedValue? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.StringValue is not null)
        {
            return new CellScalar { Kind = CellScalarKind.Text, TextValue = value.StringValue };
        }

        if (value.NumberValue is double number)
        {
            return new CellScalar
            {
                Kind = CellScalarKind.Number,
                NumberValue = Convert.ToDecimal(number, CultureInfo.InvariantCulture),
            };
        }

        if (value.BoolValue is bool boolean)
        {
            return new CellScalar { Kind = CellScalarKind.Boolean, BooleanValue = boolean };
        }

        if (value.ErrorValue is not null)
        {
            return new CellScalar
            {
                Kind = CellScalarKind.Error,
                ErrorValue = value.ErrorValue.Message ?? value.ErrorValue.Type ?? "Unknown error",
            };
        }

        return null;
    }

    private static NormalizedCellFormat? MapFormat(CellFormat? format)
    {
        if (format is null)
        {
            return null;
        }

        TextFormat? text = format.TextFormat;
        return new NormalizedCellFormat
        {
            BackgroundColor = FormatColor(format.BackgroundColorStyle, format.BackgroundColor),
            ForegroundColor = FormatColor(text?.ForegroundColorStyle, text?.ForegroundColor),
            Bold = text?.Bold ?? false,
            Italic = text?.Italic ?? false,
            Strikethrough = text?.Strikethrough ?? false,
            HorizontalAlignment = format.HorizontalAlignment,
            VerticalAlignment = format.VerticalAlignment,
            NumberFormatType = format.NumberFormat?.Type,
            NumberFormatPattern = format.NumberFormat?.Pattern,
        };
    }

    private static string? FormatColor(ColorStyle? style, Color? legacyColor)
    {
        Color? color = style?.RgbColor ?? legacyColor;
        if (color is null)
        {
            return style?.ThemeColor is null ? null : $"theme:{style.ThemeColor}";
        }

        int red = ToColorByte(color.Red ?? 0);
        int green = ToColorByte(color.Green ?? 0);
        int blue = ToColorByte(color.Blue ?? 0);
        int alpha = ToColorByte(color.Alpha ?? 1);
        return alpha == 255
            ? $"#{red:X2}{green:X2}{blue:X2}"
            : $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";
    }

    private static int ToColorByte(float component) =>
        (int)Math.Round(Math.Clamp(component, 0, 1) * 255, MidpointRounding.AwayFromZero);

    private static void AddHiddenIndexes(
        IList<DimensionProperties>? metadata,
        int startIndex,
        ICollection<int> indexes)
    {
        for (int offset = 0; offset < (metadata?.Count ?? 0); offset++)
        {
            DimensionProperties dimension = metadata![offset];
            if (dimension.HiddenByUser == true || dimension.HiddenByFilter == true)
            {
                indexes.Add(startIndex + offset);
            }
        }
    }

    private static IReadOnlyList<IndexRange> CollapseIndexes(IEnumerable<int> source)
    {
        int[] indexes = source.Distinct().Order().ToArray();
        if (indexes.Length == 0)
        {
            return [];
        }

        List<IndexRange> ranges = [];
        int start = indexes[0];
        int previous = start;
        foreach (int index in indexes.Skip(1))
        {
            if (index == previous + 1)
            {
                previous = index;
                continue;
            }

            ranges.Add(new IndexRange { StartIndex = start, EndIndexExclusive = previous + 1 });
            start = index;
            previous = index;
        }

        ranges.Add(new IndexRange { StartIndex = start, EndIndexExclusive = previous + 1 });
        return ranges;
    }

    private static bool HasEvidence(CellData cell) =>
        cell.UserEnteredValue is not null
        || cell.EffectiveValue is not null
        || cell.FormattedValue is not null
        || cell.Note is not null
        || cell.EffectiveFormat is not null;

    private static bool IsGridSheet(Sheet sheet) =>
        sheet.Properties?.SheetId is not null
        && !string.IsNullOrWhiteSpace(sheet.Properties.Title)
        && sheet.Properties.GridProperties is not null;

    private static bool HasCompleteBounds(GoogleGridRange range) =>
        range.StartRowIndex is not null
        && range.EndRowIndex is not null
        && range.StartColumnIndex is not null
        && range.EndColumnIndex is not null;

    private static ContractGridRange MapRange(GoogleGridRange range) => new()
    {
        StartRowIndex = range.StartRowIndex!.Value,
        EndRowIndexExclusive = range.EndRowIndex!.Value,
        StartColumnIndex = range.StartColumnIndex!.Value,
        EndColumnIndexExclusive = range.EndColumnIndex!.Value,
    };

    private static IReadOnlyList<string> SelectRanges(
        string sheetTitle,
        bool isFirstSheet,
        IReadOnlyList<string> requestedRanges)
    {
        return requestedRanges
            .Where(range => RangeTargetsSheet(range, sheetTitle, isFirstSheet))
            .Select(static range => range.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool RangeTargetsSheet(string range, string sheetTitle, bool isFirstSheet)
    {
        int separator = range.LastIndexOf('!');
        if (separator < 0)
        {
            return isFirstSheet;
        }

        string qualifier = range[..separator].Trim();
        if (qualifier.Length >= 2 && qualifier[0] == '\'' && qualifier[^1] == '\'')
        {
            qualifier = qualifier[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return string.Equals(qualifier, sheetTitle, StringComparison.Ordinal);
    }

    private static string ToA1Address(int rowIndex, int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

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
}
