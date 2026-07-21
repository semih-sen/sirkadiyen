using System.Text.Json;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Xunit;

namespace Sirkadiyen.Contracts.UnitTests;

public sealed class ContractJsonTests
{
    [Fact]
    public void SnapshotRoundTripPreservesTypedAndFormattedCellValues()
    {
        NormalizedSpreadsheetSnapshot snapshot = CreateSnapshot();
        JsonSerializerOptions options = ContractJson.CreateOptions();

        string json = JsonSerializer.Serialize(snapshot, options);
        NormalizedSpreadsheetSnapshot? roundTripped =
            JsonSerializer.Deserialize<NormalizedSpreadsheetSnapshot>(json, options);

        Assert.NotNull(roundTripped);
        NormalizedCell cell = Assert.Single(Assert.Single(roundTripped.Worksheets).Cells);
        Assert.Equal(CellScalarKind.Number, cell.EffectiveValue?.Kind);
        Assert.Equal(45901.0m, cell.EffectiveValue?.NumberValue);
        Assert.Equal("2025-09-01", cell.FormattedValue);
    }

    [Fact]
    public void SerializerUsesCamelCasePropertiesAndEnums()
    {
        string json = JsonSerializer.Serialize(CreateSnapshot(), ContractJson.CreateOptions());

        Assert.Contains("\"contractVersion\":\"1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"number\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ContractVersion\"", json, StringComparison.Ordinal);
    }

    private static NormalizedSpreadsheetSnapshot CreateSnapshot() => new()
    {
        ContractVersion = SpreadsheetContractVersions.V1,
        SourceId = "G3-TR-A-ANNUAL",
        SnapshotId = "snapshot-001",
        SpreadsheetId = "spreadsheet-001",
        AcquiredAtUtc = new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero),
        ContentHash = "sha256:example",
        ContentHashAlgorithm = "SHA-256",
        Worksheets =
        [
            new NormalizedWorksheet
            {
                SheetId = "sheet-001",
                Title = "A GRUBU",
                Index = 0,
                RowCount = 1283,
                ColumnCount = 7,
                Cells =
                [
                    new NormalizedCell
                    {
                        RowIndex = 1,
                        ColumnIndex = 1,
                        A1Address = "B2",
                        EffectiveValue = new CellScalar
                        {
                            Kind = CellScalarKind.Number,
                            NumberValue = 45901.0m,
                        },
                        FormattedValue = "2025-09-01",
                    },
                ],
            },
        ],
    };
}
