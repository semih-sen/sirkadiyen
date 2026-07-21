using System.Text.Json;
using Sirkadiyen.Contracts.Parsing;
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

    [Fact]
    public void SharedParserRequestFixtureIsAccepted()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "contracts",
            "v1",
            "parse-request.json");
        string json = File.ReadAllText(fixturePath);

        ParseSnapshotRequest? request = JsonSerializer.Deserialize<ParseSnapshotRequest>(
            json,
            ContractJson.CreateOptions());

        Assert.NotNull(request);
        Assert.Equal("G2-ANATOMY-AUTUMN", request.Snapshot.SourceId);
        Assert.Equal("grade2_anatomy_autumn_v1", request.ParserProfile.Name);
        Assert.Equal(
            CellScalarKind.Number,
            Assert.Single(Assert.Single(request.Snapshot.Worksheets).Cells).EffectiveValue?.Kind);
    }

    [Fact]
    public void ParseRequestCarriesSourceContextTheSpreadsheetDoesNotState()
    {
        ParseSnapshotRequest request = new()
        {
            ContractVersion = ParserContractVersions.V1,
            CorrelationId = "correlation-001",
            ParserProfile = new ParserProfileDescriptor
            {
                Name = "grade1_yearly_v1",
                Version = "1.0.0",
            },
            SourceContext = new ParseSourceContext
            {
                AcademicYear = "2025-2026",
                ClassYear = 1,
                ProgramLanguage = ProgramLanguage.English,
                TimeZoneId = "Europe/Istanbul",
            },
            Snapshot = CreateSnapshot(),
        };
        JsonSerializerOptions options = ContractJson.CreateOptions();

        string json = JsonSerializer.Serialize(request, options);
        ParseSnapshotRequest? roundTripped =
            JsonSerializer.Deserialize<ParseSnapshotRequest>(json, options);

        Assert.Contains("\"sourceContext\":", json, StringComparison.Ordinal);
        Assert.Contains("\"programLanguage\":\"english\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.Equal("2025-2026", roundTripped.SourceContext.AcademicYear);
        Assert.Equal(1, roundTripped.SourceContext.ClassYear);
        Assert.Equal("Europe/Istanbul", roundTripped.SourceContext.TimeZoneId);
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
