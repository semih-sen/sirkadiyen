using Google.Apis.Sheets.v4.Data;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleSheetsSnapshotMapperTests
{
    private readonly GoogleSheetsSnapshotMapper _mapper = new();

    [Fact]
    public void MapPreservesValuesStructureFormattingAndEvidence()
    {
        Spreadsheet spreadsheet = CreateSpreadsheet(45678.5);

        NormalizedSpreadsheetSnapshot snapshot = _mapper.Map(
            spreadsheet,
            CreateRequest(
                "snapshot-001",
                new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(SpreadsheetContractVersions.V1, snapshot.ContractVersion);
        Assert.Equal("G2-TR-ANNUAL", snapshot.SourceId);
        Assert.Equal("spreadsheet-001", snapshot.SpreadsheetId);
        Assert.Equal(SnapshotContentHasher.Algorithm, snapshot.ContentHashAlgorithm);
        Assert.StartsWith("sha256:", snapshot.ContentHash, StringComparison.Ordinal);
        Assert.Empty(snapshot.Diagnostics);

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        Assert.Equal("42", worksheet.SheetId);
        Assert.Equal("Ders ' A", worksheet.Title);
        Assert.Equal(1, worksheet.Index);
        Assert.Equal(1200, worksheet.RowCount);
        Assert.Equal(30, worksheet.ColumnCount);
        Assert.Equal(2, worksheet.FrozenRowCount);
        Assert.Equal(1, worksheet.FrozenColumnCount);
        Assert.True(worksheet.Hidden);
        Assert.Equal(["'Ders '' A'!A1:AD100", "A1:B2"], worksheet.RequestedRanges);

        Sirkadiyen.Contracts.Spreadsheets.GridRange merge = Assert.Single(worksheet.MergedRanges);
        Assert.Equal(10, merge.StartRowIndex);
        Assert.Equal(12, merge.EndRowIndexExclusive);
        Assert.Equal(25, merge.StartColumnIndex);
        Assert.Equal(27, merge.EndColumnIndexExclusive);

        IndexRange hiddenRows = Assert.Single(worksheet.HiddenRows);
        Assert.Equal(11, hiddenRows.StartIndex);
        Assert.Equal(13, hiddenRows.EndIndexExclusive);
        IndexRange hiddenColumn = Assert.Single(worksheet.HiddenColumns);
        Assert.Equal(26, hiddenColumn.StartIndex);
        Assert.Equal(27, hiddenColumn.EndIndexExclusive);

        Assert.Equal(3, worksheet.Cells.Count);
        NormalizedCell formulaCell = worksheet.Cells[0];
        Assert.Equal(10, formulaCell.RowIndex);
        Assert.Equal(25, formulaCell.ColumnIndex);
        Assert.Equal("Z11", formulaCell.A1Address);
        Assert.Null(formulaCell.UserEnteredValue);
        Assert.Equal("=DATE(2025,1,21)+TIME(12,0,0)", formulaCell.Formula);
        Assert.Equal(CellScalarKind.Number, formulaCell.EffectiveValue?.Kind);
        Assert.Equal(45678.5m, formulaCell.EffectiveValue?.NumberValue);
        Assert.Equal("21.01.2025 12:00", formulaCell.FormattedValue);
        Assert.Equal("source note", formulaCell.Note);
        Assert.Equal("#1A80E6", formulaCell.EffectiveFormat?.BackgroundColor);
        Assert.Equal("#FF000080", formulaCell.EffectiveFormat?.ForegroundColor);
        Assert.True(formulaCell.EffectiveFormat?.Bold);
        Assert.Equal("DATE_TIME", formulaCell.EffectiveFormat?.NumberFormatType);

        NormalizedCell errorCell = worksheet.Cells[1];
        Assert.Equal("AA11", errorCell.A1Address);
        Assert.Equal(CellScalarKind.Error, errorCell.EffectiveValue?.Kind);
        Assert.Equal("Division by zero", errorCell.EffectiveValue?.ErrorValue);

        NormalizedCell formattedBlankCell = worksheet.Cells[2];
        Assert.Equal("AB11", formattedBlankCell.A1Address);
        Assert.Null(formattedBlankCell.EffectiveValue);
        Assert.Equal("theme:ACCENT1", formattedBlankCell.EffectiveFormat?.BackgroundColor);
    }

    [Fact]
    public void MapProducesSameHashWhenAcquisitionMetadataAndResponseOrderChange()
    {
        Spreadsheet first = CreateSpreadsheet(45678.5);
        Spreadsheet second = CreateSpreadsheet(45678.5);
        second.Sheets = second.Sheets!.Reverse().ToList();
        second.Sheets[0].Data = second.Sheets[0].Data!.Reverse().ToList();
        second.Sheets[0].Merges = second.Sheets[0].Merges!.Reverse().ToList();

        NormalizedSpreadsheetSnapshot firstSnapshot = _mapper.Map(
            first,
            CreateRequest(
                "snapshot-001",
                new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero)));
        NormalizedSpreadsheetSnapshot secondSnapshot = _mapper.Map(
            second,
            CreateRequest(
                "snapshot-002",
                new DateTimeOffset(2026, 7, 21, 13, 0, 0, TimeSpan.Zero)) with
            {
                SourceId = "ANOTHER-SOURCE",
                Ranges = ["'Ders '' A'!A1:AD100", "'Ders '' A'!A1:AD100", "A1:B2"],
            });

        Assert.Equal(firstSnapshot.ContentHash, secondSnapshot.ContentHash);
    }

    [Fact]
    public void MapChangesHashWhenEffectiveCellContentChanges()
    {
        NormalizedSpreadsheetSnapshot first = _mapper.Map(
            CreateSpreadsheet(45678.5),
            CreateRequest("snapshot-001", DateTimeOffset.UnixEpoch));
        NormalizedSpreadsheetSnapshot changed = _mapper.Map(
            CreateSpreadsheet(45679.5),
            CreateRequest("snapshot-002", DateTimeOffset.UnixEpoch));

        Assert.NotEqual(first.ContentHash, changed.ContentHash);
    }

    [Fact]
    public void MapReportsAndOmitsNonGridSheets()
    {
        Spreadsheet spreadsheet = new()
        {
            SpreadsheetId = "spreadsheet-001",
            Sheets =
            [
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        SheetId = 7,
                        Title = "Unsupported",
                        SheetType = "OBJECT",
                    },
                },
            ],
        };

        NormalizedSpreadsheetSnapshot snapshot = _mapper.Map(
            spreadsheet,
            CreateRequest("snapshot-001", DateTimeOffset.UnixEpoch));

        Assert.Empty(snapshot.Worksheets);
        Assert.Collection(
            snapshot.Diagnostics,
            diagnostic =>
            {
                Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
                Assert.Equal("snapshot.unsupported_sheet", diagnostic.Code);
                Assert.Equal("7", diagnostic.SheetId);
            },
            diagnostic =>
            {
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Equal("snapshot.no_grid_worksheets", diagnostic.Code);
            });
    }

    [Fact]
    public void MapOmitsConflictingOverlapAndReportsAnError()
    {
        Spreadsheet spreadsheet = CreateSpreadsheet(45678.5);
        spreadsheet.Sheets![0].Data!.Add(new GridData
        {
            StartRow = 10,
            StartColumn = 25,
            RowData =
            [
                new RowData
                {
                    Values =
                    [
                        new CellData
                        {
                            EffectiveValue = new ExtendedValue { StringValue = "conflict" },
                        },
                    ],
                },
            ],
        });

        NormalizedSpreadsheetSnapshot snapshot = _mapper.Map(
            spreadsheet,
            CreateRequest("snapshot-001", DateTimeOffset.UnixEpoch));

        NormalizedWorksheet worksheet = Assert.Single(snapshot.Worksheets);
        Assert.DoesNotContain(worksheet.Cells, cell => cell.A1Address == "Z11");
        AcquisitionDiagnostic diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("snapshot.conflicting_cell_data", diagnostic.Code);
        Assert.Equal("42", diagnostic.SheetId);
        Assert.Equal("Z11", diagnostic.Range);
    }

    [Fact]
    public void MapRejectsUnexpectedSpreadsheetIdentifier()
    {
        Spreadsheet spreadsheet = CreateSpreadsheet(45678.5);
        spreadsheet.SpreadsheetId = "unexpected";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            _mapper.Map(
                spreadsheet,
                CreateRequest("snapshot-001", DateTimeOffset.UnixEpoch)));

        Assert.Contains("unexpected identifier", exception.Message, StringComparison.Ordinal);
    }

    private static AcquireSpreadsheetSnapshotRequest CreateRequest(
        string snapshotId,
        DateTimeOffset acquiredAtUtc) => new()
        {
            SourceId = "G2-TR-ANNUAL",
            SnapshotId = snapshotId,
            SpreadsheetId = "spreadsheet-001",
            AcquiredAtUtc = acquiredAtUtc,
            Ranges = ["A1:B2", "'Ders '' A'!A1:AD100"],
        };

    private static Spreadsheet CreateSpreadsheet(double effectiveNumber) => new()
    {
        SpreadsheetId = "spreadsheet-001",
        Sheets =
        [
            new Sheet
            {
                Properties = new SheetProperties
                {
                    SheetId = 42,
                    Title = "Ders ' A",
                    Index = 1,
                    Hidden = true,
                    SheetType = "GRID",
                    GridProperties = new GridProperties
                    {
                        RowCount = 1200,
                        ColumnCount = 30,
                        FrozenRowCount = 2,
                        FrozenColumnCount = 1,
                    },
                },
                Merges =
                [
                    new Google.Apis.Sheets.v4.Data.GridRange
                    {
                        SheetId = 42,
                        StartRowIndex = 10,
                        EndRowIndex = 12,
                        StartColumnIndex = 25,
                        EndColumnIndex = 27,
                    },
                ],
                Data =
                [
                    new GridData
                    {
                        StartRow = 10,
                        StartColumn = 25,
                        RowMetadata =
                        [
                            new DimensionProperties(),
                            new DimensionProperties { HiddenByUser = true },
                            new DimensionProperties { HiddenByFilter = true },
                        ],
                        ColumnMetadata =
                        [
                            new DimensionProperties(),
                            new DimensionProperties { HiddenByUser = true },
                        ],
                        RowData =
                        [
                            new RowData
                            {
                                Values =
                                [
                                    new CellData
                                    {
                                        UserEnteredValue = new ExtendedValue
                                        {
                                            FormulaValue = "=DATE(2025,1,21)+TIME(12,0,0)",
                                        },
                                        EffectiveValue = new ExtendedValue
                                        {
                                            NumberValue = effectiveNumber,
                                        },
                                        FormattedValue = "21.01.2025 12:00",
                                        Note = "source note",
                                        EffectiveFormat = new CellFormat
                                        {
                                            BackgroundColorStyle = new ColorStyle
                                            {
                                                RgbColor = new Color
                                                {
                                                    Red = 0.1f,
                                                    Green = 0.5f,
                                                    Blue = 0.9f,
                                                },
                                            },
                                            TextFormat = new TextFormat
                                            {
                                                Bold = true,
                                                ForegroundColorStyle = new ColorStyle
                                                {
                                                    RgbColor = new Color
                                                    {
                                                        Red = 1,
                                                        Green = 0,
                                                        Blue = 0,
                                                        Alpha = 0.5f,
                                                    },
                                                },
                                            },
                                            NumberFormat = new NumberFormat
                                            {
                                                Type = "DATE_TIME",
                                                Pattern = "dd.mm.yyyy hh:mm",
                                            },
                                            HorizontalAlignment = "CENTER",
                                            VerticalAlignment = "MIDDLE",
                                        },
                                    },
                                    new CellData
                                    {
                                        EffectiveValue = new ExtendedValue
                                        {
                                            ErrorValue = new ErrorValue
                                            {
                                                Type = "DIVIDE_BY_ZERO",
                                                Message = "Division by zero",
                                            },
                                        },
                                    },
                                    new CellData
                                    {
                                        EffectiveFormat = new CellFormat
                                        {
                                            BackgroundColorStyle = new ColorStyle
                                            {
                                                ThemeColor = "ACCENT1",
                                            },
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
