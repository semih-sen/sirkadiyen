namespace Sirkadiyen.Contracts.Spreadsheets;

public static class SpreadsheetContractVersions
{
    public const string V1 = "1.0";
}

public sealed record NormalizedSpreadsheetSnapshot
{
    public required string ContractVersion { get; init; }

    public required string SourceId { get; init; }

    public required string SnapshotId { get; init; }

    public required string SpreadsheetId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }

    public required string ContentHash { get; init; }

    public required string ContentHashAlgorithm { get; init; }

    public IReadOnlyList<NormalizedWorksheet> Worksheets { get; init; } = [];

    public IReadOnlyList<AcquisitionDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record NormalizedWorksheet
{
    public required string SheetId { get; init; }

    public required string Title { get; init; }

    public required int Index { get; init; }

    public required int RowCount { get; init; }

    public required int ColumnCount { get; init; }

    public int FrozenRowCount { get; init; }

    public int FrozenColumnCount { get; init; }

    public bool Hidden { get; init; }

    public IReadOnlyList<string> RequestedRanges { get; init; } = [];

    public IReadOnlyList<GridRange> MergedRanges { get; init; } = [];

    public IReadOnlyList<IndexRange> HiddenRows { get; init; } = [];

    public IReadOnlyList<IndexRange> HiddenColumns { get; init; } = [];

    public IReadOnlyList<NormalizedCell> Cells { get; init; } = [];
}

public sealed record NormalizedCell
{
    public required int RowIndex { get; init; }

    public required int ColumnIndex { get; init; }

    public required string A1Address { get; init; }

    public CellScalar? UserEnteredValue { get; init; }

    public CellScalar? EffectiveValue { get; init; }

    public string? Formula { get; init; }

    public string? FormattedValue { get; init; }

    public string? Note { get; init; }

    public NormalizedCellFormat? EffectiveFormat { get; init; }
}

public sealed record CellScalar
{
    public required CellScalarKind Kind { get; init; }

    public string? TextValue { get; init; }

    public decimal? NumberValue { get; init; }

    public bool? BooleanValue { get; init; }

    public string? ErrorValue { get; init; }
}

public enum CellScalarKind
{
    Text,
    Number,
    Boolean,
    Error,
}

public sealed record NormalizedCellFormat
{
    public string? BackgroundColor { get; init; }

    public string? ForegroundColor { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Strikethrough { get; init; }

    public string? HorizontalAlignment { get; init; }

    public string? VerticalAlignment { get; init; }

    public string? NumberFormatType { get; init; }

    public string? NumberFormatPattern { get; init; }
}

public sealed record GridRange
{
    public required int StartRowIndex { get; init; }

    public required int EndRowIndexExclusive { get; init; }

    public required int StartColumnIndex { get; init; }

    public required int EndColumnIndexExclusive { get; init; }
}

public sealed record IndexRange
{
    public required int StartIndex { get; init; }

    public required int EndIndexExclusive { get; init; }
}

public sealed record AcquisitionDiagnostic
{
    public required DiagnosticSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? SheetId { get; init; }

    public string? Range { get; init; }
}

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}
