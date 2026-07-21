from datetime import datetime
from enum import StrEnum
from typing import Literal

from pydantic import Field

from sirkadiyen_parser.contracts.base import ContractModel


class CellScalarKind(StrEnum):
    TEXT = "text"
    NUMBER = "number"
    BOOLEAN = "boolean"
    ERROR = "error"


class DiagnosticSeverity(StrEnum):
    INFORMATION = "information"
    WARNING = "warning"
    ERROR = "error"


class CellScalar(ContractModel):
    kind: CellScalarKind
    text_value: str | None = None
    number_value: float | None = None
    boolean_value: bool | None = None
    error_value: str | None = None


class NormalizedCellFormat(ContractModel):
    background_color: str | None = None
    foreground_color: str | None = None
    bold: bool = False
    italic: bool = False
    strikethrough: bool = False
    horizontal_alignment: str | None = None
    vertical_alignment: str | None = None
    number_format_type: str | None = None
    number_format_pattern: str | None = None


class GridRange(ContractModel):
    start_row_index: int = Field(ge=0)
    end_row_index_exclusive: int = Field(gt=0)
    start_column_index: int = Field(ge=0)
    end_column_index_exclusive: int = Field(gt=0)


class IndexRange(ContractModel):
    start_index: int = Field(ge=0)
    end_index_exclusive: int = Field(gt=0)


class NormalizedCell(ContractModel):
    row_index: int = Field(ge=0)
    column_index: int = Field(ge=0)
    a1_address: str = Field(min_length=1)
    user_entered_value: CellScalar | None = None
    effective_value: CellScalar | None = None
    formula: str | None = None
    formatted_value: str | None = None
    note: str | None = None
    effective_format: NormalizedCellFormat | None = None


class NormalizedWorksheet(ContractModel):
    sheet_id: str = Field(min_length=1)
    title: str = Field(min_length=1)
    index: int = Field(ge=0)
    row_count: int = Field(ge=0)
    column_count: int = Field(ge=0)
    frozen_row_count: int = Field(default=0, ge=0)
    frozen_column_count: int = Field(default=0, ge=0)
    hidden: bool = False
    requested_ranges: list[str] = Field(default_factory=list)
    merged_ranges: list[GridRange] = Field(default_factory=list)
    hidden_rows: list[IndexRange] = Field(default_factory=list)
    hidden_columns: list[IndexRange] = Field(default_factory=list)
    cells: list[NormalizedCell] = Field(default_factory=list)


class AcquisitionDiagnostic(ContractModel):
    severity: DiagnosticSeverity
    code: str = Field(min_length=1)
    message: str = Field(min_length=1)
    sheet_id: str | None = None
    range: str | None = None


class NormalizedSpreadsheetSnapshot(ContractModel):
    contract_version: Literal["1.0"]
    source_id: str = Field(min_length=1)
    snapshot_id: str = Field(min_length=1)
    spreadsheet_id: str = Field(min_length=1)
    acquired_at_utc: datetime
    content_hash: str = Field(min_length=1)
    content_hash_algorithm: str = Field(min_length=1)
    worksheets: list[NormalizedWorksheet] = Field(default_factory=list)
    diagnostics: list[AcquisitionDiagnostic] = Field(default_factory=list)
