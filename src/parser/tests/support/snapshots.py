"""Builders for inbound snapshot models.

Snapshot models accept camel-case aliases only, matching the transport contract.
These builders construct them from the same camel-case payload a producer would
send, so tests exercise the real validation path instead of bypassing it.
"""

from typing import Any

from sirkadiyen_parser.contracts.snapshot import (
    GridRange,
    IndexRange,
    NormalizedCell,
    NormalizedWorksheet,
)
from sirkadiyen_parser.normalization.grid import a1_address


def text_cell(row: int, column: int, value: str, *, note: str | None = None) -> NormalizedCell:
    """Build a cell holding text, formatted exactly as entered."""
    payload: dict[str, Any] = {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": a1_address(row, column),
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }
    if note is not None:
        payload["note"] = note
    return NormalizedCell.model_validate(payload)


def number_cell(
    row: int,
    column: int,
    value: float,
    *,
    formatted: str | None = None,
    number_format_type: str | None = None,
) -> NormalizedCell:
    """Build a cell holding a number, optionally declaring a number format.

    ``number_format_type`` is what distinguishes a date or time serial from an
    ordinary number, so it is set explicitly per test rather than assumed.
    """
    payload: dict[str, Any] = {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": a1_address(row, column),
        "effectiveValue": {"kind": "number", "numberValue": value},
    }
    if formatted is not None:
        payload["formattedValue"] = formatted
    if number_format_type is not None:
        payload["effectiveFormat"] = {"numberFormatType": number_format_type}
    return NormalizedCell.model_validate(payload)


def merged_range(
    start_row: int,
    start_column: int,
    end_row_exclusive: int,
    end_column_exclusive: int,
) -> GridRange:
    """Build a merged range using zero-based, end-exclusive coordinates."""
    return GridRange.model_validate(
        {
            "startRowIndex": start_row,
            "endRowIndexExclusive": end_row_exclusive,
            "startColumnIndex": start_column,
            "endColumnIndexExclusive": end_column_exclusive,
        }
    )


def index_range(start: int, end_exclusive: int) -> IndexRange:
    """Build a hidden-dimension range."""
    return IndexRange.model_validate({"startIndex": start, "endIndexExclusive": end_exclusive})


def worksheet(
    cells: list[NormalizedCell],
    *,
    merged_ranges: list[GridRange] | None = None,
    hidden_rows: list[IndexRange] | None = None,
    title: str = "Test Sheet",
) -> NormalizedWorksheet:
    """Build a worksheet around the supplied cells."""
    return NormalizedWorksheet.model_validate(
        {
            "sheetId": "sheet-1",
            "title": title,
            "index": 0,
            "rowCount": 10,
            "columnCount": 5,
            "mergedRanges": merged_ranges or [],
            "hiddenRows": hidden_rows or [],
            "cells": cells,
        }
    )
