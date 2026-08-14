using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class LocalXlsxSnapshotConverterTests
{
    private readonly LocalXlsxSnapshotConverter _converter = new();

    [Fact]
    public void AnnualFixturePreservesTypedDatesTimesAndSemanticBounds()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g1-tr-annual.xlsx");
        AcquireSpreadsheetSnapshotRequest request = CreateRequest("G1-TR-ANNUAL", "sheet-annual");

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(path, request);

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        Assert.Equal("DÖNEM 1", worksheet.Title);
        Assert.Equal(942, worksheet.RowCount);
        Assert.Equal(13, worksheet.ColumnCount);
        Assert.Equal(["A1:M942"], worksheet.RequestedRanges);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

        NormalizedCell dateCell = Assert.Single(
            worksheet.Cells,
            cell => cell.A1Address == "B8");
        Assert.Equal(CellScalarKind.Number, dateCell.EffectiveValue?.Kind);
        Assert.Equal(45929m, dateCell.EffectiveValue?.NumberValue);
        Assert.Equal("DATE", dateCell.EffectiveFormat?.NumberFormatType);

        NormalizedCell timeCell = Assert.Single(
            worksheet.Cells,
            cell => cell.A1Address == "C2");
        Assert.Equal(CellScalarKind.Number, timeCell.EffectiveValue?.Kind);
        Assert.Equal("TIME", timeCell.EffectiveFormat?.NumberFormatType);
    }

    [Fact]
    public void PracticeFixturePreservesMergesAndConversionIsDeterministic()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g1-tr-practice.xlsx");
        AcquireSpreadsheetSnapshotRequest request = CreateRequest("G1-TR-PRACTICE", "sheet-practice");

        NormalizedSpreadsheetSnapshot first = _converter.Convert(path, request);
        NormalizedSpreadsheetSnapshot second = _converter.Convert(path, request with
        {
            SnapshotId = "another-snapshot-id",
            AcquiredAtUtc = request.AcquiredAtUtc.AddHours(1),
        });

        NormalizedWorksheet worksheet = Assert.Single(first.Worksheets);
        Assert.Equal("Sayfa1", worksheet.Title);
        Assert.Equal(335, worksheet.RowCount);
        Assert.Equal(14, worksheet.ColumnCount);
        Assert.Equal(["A1:N335"], worksheet.RequestedRanges);
        Assert.Equal(77, worksheet.MergedRanges.Count);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    [Fact]
    public void ALongDateFormatIsADateAndNotACurrency()
    {
        // Excel writes its built-in long date as `[$-F800]dddd, mmmm dd, yyyy`.
        // The `[$-…]` bracket is a locale identifier and carries no currency
        // symbol, but reading it as one classified a real date cell as currency,
        // which left it unresolvable and took a whole rotation row of eight
        // sessions off the Grade 3 calendars.
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "g3-tr-a-faculty.xlsx");
        AcquireSpreadsheetSnapshotRequest request = CreateRequest("G3-TR-A-FACULTY", "sheet-faculty");

        NormalizedSpreadsheetSnapshot snapshot = _converter.Convert(path, request);

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        NormalizedCell longDate = Assert.Single(worksheet.Cells, cell => cell.A1Address == "A287");
        Assert.Equal(CellScalarKind.Number, longDate.EffectiveValue?.Kind);
        Assert.StartsWith("[$-F800]", longDate.EffectiveFormat?.NumberFormatPattern);
        Assert.Equal("DATE", longDate.EffectiveFormat?.NumberFormatType);

        // A bracket that does state a symbol is still a currency.
        Assert.DoesNotContain(
            worksheet.Cells,
            cell => cell.EffectiveFormat?.NumberFormatType == "CURRENCY");
    }

    private static AcquireSpreadsheetSnapshotRequest CreateRequest(
        string sourceId,
        string spreadsheetId) => new()
        {
            SourceId = sourceId,
            SnapshotId = "fixture:test",
            SpreadsheetId = spreadsheetId,
            AcquiredAtUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero),
        };
}
