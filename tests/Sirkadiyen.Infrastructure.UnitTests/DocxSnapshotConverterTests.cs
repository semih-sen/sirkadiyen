using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Xunit;
using OpenXmlTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using OpenXmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OpenXmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class LocalDocxSnapshotConverterTests
{
    private readonly LocalDocxSnapshotConverter _converter = new();

    [Fact]
    public void AnatomyFixtureKeepsTableBoundariesAndVerticalMerges()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g2-anatomy-autumn.docx");

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            path,
            CreateRequest("G2-ANATOMY-AUTUMN"));

        // The rotation runs over two Word tables separated by a page break. They
        // stay two worksheets: merging them would invent a table the document
        // does not contain.
        Assert.Equal(2, snapshot.Worksheets.Count);
        Assert.DoesNotContain(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        NormalizedWorksheet first = snapshot.Worksheets[0];
        Assert.Equal("Table 1", first.Title);
        Assert.Equal(49, first.RowCount);
        Assert.Equal(3, first.ColumnCount);
        Assert.Equal(["A1:C49"], first.RequestedRanges);

        // The heading spans the full width of the table.
        Assert.Contains(
            first.MergedRanges,
            range => range is
            {
                StartRowIndex: 0, EndRowIndexExclusive: 1,
                StartColumnIndex: 0, EndColumnIndexExclusive: 3,
            });

        // The last date is written once and merged down over its three hours.
        Assert.Contains(
            first.MergedRanges,
            range => range is
            {
                StartRowIndex: 46, EndRowIndexExclusive: 49,
                StartColumnIndex: 0, EndColumnIndexExclusive: 1,
            });

        // The three hours of one date each state their own anatomy group.
        Assert.Equal("13:30-14:20", CellText(first, "B47"));
        Assert.Equal("1", CellText(first, "C47"));
        Assert.Equal("3", CellText(first, "C49"));

        // A merged cell states its value once, in the row the merge starts.
        Assert.Null(FindCell(first, "A48"));
    }

    [Fact]
    public void VerticalCorridorFixtureKeepsTheLineStructureOfASlotCell()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g2-vertical-autumn.docx");

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            path,
            CreateRequest("G2-VERTICAL-AUTUMN"));

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        Assert.Equal(60, worksheet.RowCount);
        Assert.Equal(7, worksheet.ColumnCount);

        // A slot cell carries a label, a date and a time range on three lines,
        // exactly as the Grade 2 practice sheet writes them. A profile separates
        // them by line, so the breaks have to survive conversion.
        Assert.Equal("1/1\n8 Eylül 2025 Pazartesi\n08:30-10:20", CellText(worksheet, "A5"));
        Assert.Equal("D", CellText(worksheet, "C16"));

        // A Word cell carries no typed value and no number format, so a date in
        // one of these sources can only ever be read from its text.
        NormalizedCell slot = Assert.IsType<NormalizedCell>(FindCell(worksheet, "A5"));
        Assert.Equal(CellScalarKind.Text, slot.EffectiveValue?.Kind);
        Assert.Null(slot.EffectiveFormat);
        Assert.Null(slot.Formula);

        // Cells holding only a non-breaking space state nothing.
        Assert.Null(FindCell(worksheet, "E5"));
    }

    [Fact]
    public void ConversionOfTheSameDocumentIsDeterministic()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g2-vertical-autumn.docx");
        AcquireSpreadsheetSnapshotRequest request = CreateRequest("G2-VERTICAL-AUTUMN");

        NormalizedSpreadsheetSnapshot first = _converter.Convert(path, request);
        NormalizedSpreadsheetSnapshot second = _converter.Convert(path, request with
        {
            SnapshotId = "another-snapshot-id",
            AcquiredAtUtc = request.AcquiredAtUtc.AddHours(1),
        });

        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void ParagraphsBetweenTablesBecomeTheirOwnWorksheet()
    {
        using MemoryStream stream = BuildDocument(
            new Paragraph(new Run(new Text("Dikey Koridor II uygulama grupları"))),
            new OpenXmlTable(Row("A", "B")),
            new Paragraph(new Run(new Text("Tabloda * ile belirtilen tarihlerde"))));

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            WriteTemporary(stream),
            CreateRequest("TEST-DOCX"));

        Assert.Equal(
            ["Text 1", "Table 1", "Text 2"],
            snapshot.Worksheets.Select(static worksheet => worksheet.Title));
        Assert.Equal(["1", "2", "3"], snapshot.Worksheets.Select(static w => w.SheetId));
        Assert.Equal([0, 1, 2], snapshot.Worksheets.Select(static w => w.Index));

        // The note paragraphs of these documents state which groups a table
        // leaves unstated, so losing them would lose schedule evidence.
        NormalizedWorksheet text = snapshot.Worksheets[0];
        Assert.Equal(1, text.ColumnCount);
        Assert.Equal("Dikey Koridor II uygulama grupları", CellText(text, "A1"));
    }

    [Fact]
    public void ABlankParagraphRunIsReportedRatherThanSilentlyDropped()
    {
        using MemoryStream stream = BuildDocument(
            new OpenXmlTable(Row("A")),
            new Paragraph(),
            new Paragraph(new Run(new Break())));

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            WriteTemporary(stream),
            CreateRequest("TEST-DOCX"));

        Assert.Single(snapshot.Worksheets);
        AcquisitionDiagnostic diagnostic = Assert.Single(
            snapshot.Diagnostics,
            candidate => candidate.Code
                == LocalDocxSnapshotConverter.BlankParagraphBlockDiagnosticCode);
        Assert.Contains("2 paragraph(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("after worksheet 1", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableNestedInACellIsReportedAsAnErrorWithItsAddress()
    {
        OpenXmlTableCell outer = new(
            new Paragraph(new Run(new Text("dış"))),
            new OpenXmlTable(Row("iç")));
        using MemoryStream stream = BuildDocument(
            new OpenXmlTable(new OpenXmlTableRow(Cell("ilk"), outer)));

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            WriteTemporary(stream),
            CreateRequest("TEST-DOCX"));

        // A flat grid has no position for a table inside a cell. Flattening its
        // text into the containing cell would state a single value the document
        // never wrote, so the address is reported instead.
        AcquisitionDiagnostic diagnostic = Assert.Single(
            snapshot.Diagnostics,
            candidate => candidate.Code == LocalDocxSnapshotConverter.NestedTableDiagnosticCode);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("B1", diagnostic.Range);
        Assert.Equal("1", diagnostic.SheetId);
        Assert.Equal("dış", CellText(snapshot.Worksheets[0], "B1"));
    }

    [Fact]
    public void HorizontallySpannedCellsShiftTheColumnsAfterThem()
    {
        OpenXmlTableRow spanned = new(Cell("başlık", gridSpan: 2), Cell("C"));
        using MemoryStream stream = BuildDocument(
            new OpenXmlTable(spanned, Row("1", "2", "3")));

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(
            WriteTemporary(stream),
            CreateRequest("TEST-DOCX"));

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        Assert.Equal(3, worksheet.ColumnCount);
        Assert.Equal("başlık", CellText(worksheet, "A1"));
        // The spanned cell occupies two columns, so the next one starts at C.
        Assert.Equal("C", CellText(worksheet, "C1"));
        Assert.Equal("3", CellText(worksheet, "C2"));
        GridRange merge = Assert.Single(worksheet.MergedRanges);
        Assert.Equal(0, merge.StartColumnIndex);
        Assert.Equal(2, merge.EndColumnIndexExclusive);
    }

    private static string? CellText(NormalizedWorksheet worksheet, string a1Address) =>
        FindCell(worksheet, a1Address)?.FormattedValue;

    private static NormalizedCell? FindCell(NormalizedWorksheet worksheet, string a1Address) =>
        worksheet.Cells.SingleOrDefault(
            cell => string.Equals(cell.A1Address, a1Address, StringComparison.Ordinal));

    private static OpenXmlTableRow Row(params string[] values) =>
        new(values.Select(value => Cell(value)).Cast<OpenXmlElement>().ToArray());

    private static OpenXmlTableCell Cell(string text, int gridSpan = 1)
    {
        OpenXmlTableCell cell = new(new Paragraph(new Run(new Text(text))));
        if (gridSpan > 1)
        {
            cell.TableCellProperties = new TableCellProperties(new GridSpan { Val = gridSpan });
        }

        return cell;
    }

    private static MemoryStream BuildDocument(params OpenXmlElement[] blocks)
    {
        MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
            stream,
            WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(blocks));
        }

        return stream;
    }

    private static string WriteTemporary(MemoryStream stream)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.CreateVersion7():N}.docx");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static AcquireSpreadsheetSnapshotRequest CreateRequest(string sourceId) => new()
    {
        SourceId = sourceId,
        SnapshotId = "fixture:test",
        SpreadsheetId = "document-test",
        AcquiredAtUtc = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero),
    };
}
