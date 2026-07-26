using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using ContractGridRange = Sirkadiyen.Contracts.Spreadsheets.GridRange;
using OpenXmlTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using OpenXmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OpenXmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

namespace Sirkadiyen.Infrastructure.ScheduleIngestion;

/// <summary>
/// Converts a Word document into the same normalized snapshot the spreadsheet
/// sources produce.
/// </summary>
/// <remarks>
/// <para>
/// Several schedules are published as DOCX rather than as a sheet: the Grade 2
/// anatomy group lists, the Grade 2 vertical-corridor calendar and the Grade 3
/// bedside programs. They are grids all the same — a Word table is rows, columns
/// and merges — so they are converted onto
/// <see cref="NormalizedSpreadsheetSnapshot"/> instead of gaining a second
/// document contract. Everything downstream (immutable snapshot storage, the
/// unchanged-snapshot short circuit, the parser's grid primitives, A1 evidence,
/// golden files) then works unchanged (ADR-076).
/// </para>
/// <para>
/// A Word document is a sequence of blocks, not a set of named sheets, so the
/// mapping is stated explicitly: each body-level table becomes one worksheet,
/// and each run of paragraphs between tables becomes one single-column
/// worksheet, both in document order. Worksheet titles are synthetic, because
/// Word gives its tables no names; the snapshot says so in its own diagnostics
/// rather than leaving a reader to infer it.
/// </para>
/// <para>
/// What a Word cell cannot carry matters as much as what it can. There are no
/// typed values and no number formats, so every cell is text and a profile
/// reading one of these sources resolves dates and times from text alone. That
/// is a property of the source, not a loss in conversion.
/// </para>
/// </remarks>
public sealed class DocxSnapshotConverter
{
    /// <summary>Diagnostic marking a snapshot converted from a local file.</summary>
    public const string LocalFixtureDiagnosticCode = "snapshot.local_docx_fixture";

    /// <summary>Diagnostic marking a snapshot converted from an administrator's upload.</summary>
    public const string AdministrativeUploadDiagnosticCode = "snapshot.administrative_upload";

    /// <summary>Diagnostic stating that worksheet titles were not read from the document.</summary>
    public const string SyntheticTitleDiagnosticCode = "snapshot.docx_synthetic_worksheet_titles";

    /// <summary>Diagnostic for a run of paragraphs that carried no text.</summary>
    public const string BlankParagraphBlockDiagnosticCode = "snapshot.docx_blank_paragraph_block";

    /// <summary>Diagnostic for a table nested inside a table cell.</summary>
    public const string NestedTableDiagnosticCode = "snapshot.docx_nested_table";

    /// <summary>Diagnostic for a body-level element this converter does not model.</summary>
    public const string UnsupportedBlockDiagnosticCode = "snapshot.docx_unsupported_block";

    /// <summary>Diagnostic for page headers and footers, which are not schedule data.</summary>
    public const string HeaderFooterDiagnosticCode = "snapshot.docx_header_footer_omitted";

    /// <summary>Title prefix of a worksheet converted from a Word table.</summary>
    public const string TableTitlePrefix = "Table ";

    /// <summary>Title prefix of a worksheet converted from a run of paragraphs.</summary>
    public const string TextTitlePrefix = "Text ";

    /// <summary>
    /// Converts a document read from the repository, for fixture development.
    /// </summary>
    public NormalizedSpreadsheetSnapshot Convert(
        string docxPath,
        AcquireSpreadsheetSnapshotRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        using WordprocessingDocument document = WordprocessingDocument.Open(docxPath, false);
        return Convert(document, request, LocalFixtureOrigin());
    }

    /// <summary>
    /// Converts a document an administrator uploaded for a source that is handed
    /// out rather than published (ADR-079, ADR-080).
    /// </summary>
    /// <remarks>
    /// The bytes are copied into a seekable stream because the OpenXML reader
    /// seeks, and because one upload is converted once per source it serves.
    /// </remarks>
    public NormalizedSpreadsheetSnapshot ConvertUpload(
        ReadOnlyMemory<byte> content,
        AcquireSpreadsheetSnapshotRequest request)
    {
        using MemoryStream stream = new(content.Length);
        stream.Write(content.Span);
        stream.Position = 0;

        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return Convert(document, request, AdministrativeUploadOrigin());
    }

    private static AcquisitionDiagnostic LocalFixtureOrigin() => new()
    {
        Severity = DiagnosticSeverity.Information,
        Code = LocalFixtureDiagnosticCode,
        Message = "Snapshot was converted from a local DOCX fixture, not acquired through an API.",
    };

    /// <summary>
    /// States how the document arrived, because an uploaded source has no location
    /// to be traced back to and the snapshot is the only place that can say so.
    /// </summary>
    private static AcquisitionDiagnostic AdministrativeUploadOrigin() => new()
    {
        Severity = DiagnosticSeverity.Information,
        Code = AdministrativeUploadDiagnosticCode,
        Message = "Snapshot was converted from a DOCX an administrator uploaded, not fetched from a "
            + "published location. The upload audit records who supplied it.",
    };

    private static NormalizedSpreadsheetSnapshot Convert(
        WordprocessingDocument document,
        AcquireSpreadsheetSnapshotRequest request,
        AcquisitionDiagnostic origin)
    {
        ArgumentNullException.ThrowIfNull(request);

        MainDocumentPart mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("The DOCX file does not contain a main document part.");
        Body body = mainPart.Document?.Body
            ?? throw new InvalidDataException("The DOCX file does not contain a document body.");

        List<AcquisitionDiagnostic> diagnostics =
        [
            origin,
            new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Code = SyntheticTitleDiagnosticCode,
                Message = "A Word document does not name its tables. Worksheet titles are assigned by "
                    + "the converter in document order and are not source text.",
            },
        ];

        ReportHeadersAndFooters(mainPart, diagnostics);

        List<NormalizedWorksheet> worksheets = [];
        List<Paragraph> pending = [];
        int tableNumber = 0;

        foreach (OpenXmlElement block in body.ChildElements)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    pending.Add(paragraph);
                    break;

                case OpenXmlTable table:
                    FlushParagraphs(pending, worksheets, diagnostics);
                    worksheets.Add(MapTable(table, worksheets.Count, ++tableNumber, diagnostics));
                    break;

                // Section properties describe page geometry, and a bookmark marks
                // a position rather than carrying content. Neither is schedule data.
                case SectionProperties:
                case BookmarkStart:
                case BookmarkEnd:
                    break;

                default:
                    diagnostics.Add(new AcquisitionDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Code = UnsupportedBlockDiagnosticCode,
                        Message = $"A body-level '{block.LocalName}' element is not modelled by this "
                            + "converter and contributed no worksheet. Any text it holds is absent "
                            + "from the snapshot.",
                    });
                    break;
            }
        }

        FlushParagraphs(pending, worksheets, diagnostics);

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

    private static void ReportHeadersAndFooters(
        MainDocumentPart mainPart,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        int withText = mainPart.HeaderParts.Count(
                static part => HasText(part.Header?.InnerText))
            + mainPart.FooterParts.Count(
                static part => HasText(part.Footer?.InnerText));
        if (withText == 0)
        {
            return;
        }

        diagnostics.Add(new AcquisitionDiagnostic
        {
            Severity = DiagnosticSeverity.Information,
            Code = HeaderFooterDiagnosticCode,
            Message = $"{withText.ToString(CultureInfo.InvariantCulture)} page header or footer "
                + "part(s) hold text and were not converted. They repeat on every page and carry "
                + "page numbering rather than schedule data.",
        });
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Turns the paragraphs collected since the previous table into a worksheet.
    /// </summary>
    /// <remarks>
    /// The paragraphs between tables are not decoration: the Grade 3 bedside
    /// documents write their practice topics that way. A run that holds no text
    /// at all produces no worksheet, because empty paragraphs are Word's spacing;
    /// the run is still reported so the omission is visible in the snapshot
    /// rather than inferred from a gap in the worksheet numbering.
    /// </remarks>
    private static void FlushParagraphs(
        List<Paragraph> pending,
        List<NormalizedWorksheet> worksheets,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        if (pending.Count == 0)
        {
            return;
        }

        List<NormalizedCell> cells = [];
        for (int index = 0; index < pending.Count; index++)
        {
            string text = ReadText(pending[index]);
            if (!HasText(text))
            {
                continue;
            }

            cells.Add(CreateCell(index, 0, text));
        }

        int sheetNumber = worksheets.Count + 1;
        if (cells.Count == 0)
        {
            // Positioned by what precedes it, because what follows may not exist:
            // a document usually ends with a run of empty paragraphs.
            string position = worksheets.Count == 0
                ? "before the first worksheet"
                : $"after worksheet {worksheets.Count.ToString(CultureInfo.InvariantCulture)}";
            diagnostics.Add(new AcquisitionDiagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Code = BlankParagraphBlockDiagnosticCode,
                Message = $"{pending.Count.ToString(CultureInfo.InvariantCulture)} paragraph(s) "
                    + $"{position} hold no text and produced no worksheet.",
            });
            pending.Clear();
            return;
        }

        int textNumber = worksheets.Count(
            static worksheet => worksheet.Title.StartsWith(TextTitlePrefix, StringComparison.Ordinal))
            + 1;
        worksheets.Add(new NormalizedWorksheet
        {
            SheetId = sheetNumber.ToString(CultureInfo.InvariantCulture),
            Title = $"{TextTitlePrefix}{textNumber.ToString(CultureInfo.InvariantCulture)}",
            Index = worksheets.Count,
            RowCount = pending.Count,
            ColumnCount = 1,
            RequestedRanges = [$"A1:A{pending.Count.ToString(CultureInfo.InvariantCulture)}"],
            Cells = cells,
        });
        pending.Clear();
    }

    private static NormalizedWorksheet MapTable(
        OpenXmlTable table,
        int worksheetIndex,
        int tableNumber,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        string sheetId = (worksheetIndex + 1).ToString(CultureInfo.InvariantCulture);
        List<OpenXmlTableRow> rows = [.. table.Elements<OpenXmlTableRow>()];
        List<NormalizedCell> cells = [];
        List<ContractGridRange> merges = [];

        // A vertically merged cell states its value once, in the row where the
        // merge restarts, and the rows below it repeat the cell with no value.
        // The open merge is tracked by its starting column so the continuation
        // rows extend the range instead of producing empty cells.
        Dictionary<int, ContractGridRange> openVerticalMerges = [];
        int columnCount = CountGridColumns(table);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            int column = 0;
            foreach (OpenXmlTableCell cell in rows[rowIndex].Elements<OpenXmlTableCell>())
            {
                TableCellProperties? properties = cell.TableCellProperties;
                int span = ReadGridSpan(properties);
                MergedCellValues? verticalMerge = ReadVerticalMerge(properties);
                bool continuesMerge = verticalMerge == MergedCellValues.Continue;

                ReportNestedTables(cell, sheetId, rowIndex, column, diagnostics);

                if (continuesMerge && openVerticalMerges.TryGetValue(column, out ContractGridRange? open))
                {
                    openVerticalMerges[column] = open with { EndRowIndexExclusive = rowIndex + 1 };
                }
                else
                {
                    if (verticalMerge == MergedCellValues.Restart)
                    {
                        CloseVerticalMerge(openVerticalMerges, merges, column);
                        openVerticalMerges[column] = new ContractGridRange
                        {
                            StartRowIndex = rowIndex,
                            EndRowIndexExclusive = rowIndex + 1,
                            StartColumnIndex = column,
                            EndColumnIndexExclusive = column + span,
                        };
                    }
                    else
                    {
                        CloseVerticalMerge(openVerticalMerges, merges, column);
                        if (span > 1)
                        {
                            merges.Add(new ContractGridRange
                            {
                                StartRowIndex = rowIndex,
                                EndRowIndexExclusive = rowIndex + 1,
                                StartColumnIndex = column,
                                EndColumnIndexExclusive = column + span,
                            });
                        }
                    }

                    // A cell holding only spacing — a stray non-breaking space,
                    // an empty paragraph — states nothing, and the grid position
                    // it would occupy is already implied by the table's row and
                    // column count. The same test decides whether a paragraph
                    // opens a worksheet.
                    string text = ReadCellText(cell);
                    if (HasText(text))
                    {
                        cells.Add(CreateCell(rowIndex, column, text));
                    }
                }

                column += span;
            }

            columnCount = Math.Max(columnCount, column);
        }

        foreach (int column in openVerticalMerges.Keys.Order().ToArray())
        {
            CloseVerticalMerge(openVerticalMerges, merges, column);
        }

        return new NormalizedWorksheet
        {
            SheetId = sheetId,
            Title = $"{TableTitlePrefix}{tableNumber.ToString(CultureInfo.InvariantCulture)}",
            Index = worksheetIndex,
            RowCount = rows.Count,
            ColumnCount = columnCount,
            // A Word table repeats its header rows on every page. That is the
            // same statement a frozen spreadsheet row makes.
            FrozenRowCount = CountRepeatedHeaderRows(rows),
            RequestedRanges = rows.Count > 0 && columnCount > 0
                ? [$"A1:{ToA1Address(rows.Count - 1, columnCount - 1)}"]
                : [],
            MergedRanges = merges
                .OrderBy(static range => range.StartRowIndex)
                .ThenBy(static range => range.StartColumnIndex)
                .ToList(),
            Cells = cells,
        };
    }

    private static void CloseVerticalMerge(
        Dictionary<int, ContractGridRange> open,
        ICollection<ContractGridRange> merges,
        int column)
    {
        if (!open.Remove(column, out ContractGridRange? range))
        {
            return;
        }

        // A merge that never continued and spans one column is not a merge.
        if (range.EndRowIndexExclusive - range.StartRowIndex > 1
            || range.EndColumnIndexExclusive - range.StartColumnIndex > 1)
        {
            merges.Add(range);
        }
    }

    private static void ReportNestedTables(
        OpenXmlTableCell cell,
        string sheetId,
        int rowIndex,
        int columnIndex,
        ICollection<AcquisitionDiagnostic> diagnostics)
    {
        int nested = cell.Elements<OpenXmlTable>().Count();
        if (nested == 0)
        {
            return;
        }

        // A table inside a cell has no position in a flat grid, and placing its
        // text into the containing cell would state a single value the document
        // never wrote. The cell is reported by address instead, so a profile
        // reading this source can be corrected against real evidence.
        diagnostics.Add(new AcquisitionDiagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = NestedTableDiagnosticCode,
            Message = $"{nested.ToString(CultureInfo.InvariantCulture)} table(s) nested inside a cell "
                + "cannot be represented in a flat grid and were not converted.",
            SheetId = sheetId,
            Range = ToA1Address(rowIndex, columnIndex),
        });
    }

    private static int CountGridColumns(OpenXmlTable table) =>
        table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0;

    private static int ReadGridSpan(TableCellProperties? properties)
    {
        int span = properties?.GridSpan?.Val?.Value ?? 1;
        return span > 0 ? span : 1;
    }

    private static MergedCellValues? ReadVerticalMerge(TableCellProperties? properties)
    {
        VerticalMerge? merge = properties?.VerticalMerge;
        if (merge is null)
        {
            return null;
        }

        // An omitted value means "continue", which is what Word writes for every
        // row of a merge below the first.
        return merge.Val?.Value ?? MergedCellValues.Continue;
    }

    private static int CountRepeatedHeaderRows(IReadOnlyList<OpenXmlTableRow> rows)
    {
        int count = 0;
        foreach (OpenXmlTableRow row in rows)
        {
            if (row.TableRowProperties?.Elements<TableHeader>().Any() != true)
            {
                break;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads a table cell as one string, keeping the line structure the document
    /// shows.
    /// </summary>
    /// <remarks>
    /// The line breaks are load-bearing. The vertical-corridor calendar writes a
    /// slot label, a date and a time range as three lines of one cell, exactly as
    /// the Grade 2 practice sheet does, and a profile separates them by line.
    /// Both a paragraph boundary and an explicit break become a newline, because
    /// the document renders them the same way.
    /// </remarks>
    private static string ReadCellText(OpenXmlTableCell cell) => string.Join(
        '\n',
        cell.Elements<Paragraph>().Select(ReadText));

    private static string ReadText(Paragraph paragraph)
    {
        StringBuilder builder = new();
        foreach (OpenXmlElement element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case Break:
                    builder.Append('\n');
                    break;
                case TabChar:
                    builder.Append('\t');
                    break;
                case NoBreakHyphen:
                    builder.Append('-');
                    break;
            }
        }

        // Deliberately untrimmed. The converter transcribes; collapsing ragged
        // whitespace is the parser's normalization step, and it already does it
        // for the spreadsheet sources.
        return builder.ToString();
    }

    private static NormalizedCell CreateCell(int rowIndex, int columnIndex, string text)
    {
        CellScalar scalar = new() { Kind = CellScalarKind.Text, TextValue = text };
        return new NormalizedCell
        {
            RowIndex = rowIndex,
            ColumnIndex = columnIndex,
            A1Address = ToA1Address(rowIndex, columnIndex),
            // A Word cell holds no typed value and no formula, so what the reader
            // sees and what the document stores are the same string.
            UserEnteredValue = scalar,
            EffectiveValue = scalar,
            FormattedValue = text,
        };
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
}
