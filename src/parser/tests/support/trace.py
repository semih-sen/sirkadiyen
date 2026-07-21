"""Normalization trace builder.

A trace records what the shared primitives made of every meaningful coordinate
in a snapshot, before any parser profile applies its own rules. Capturing it as
a golden file means a change to merge expansion, date reading, time reading,
group parsing or instructor extraction cannot slip through unnoticed while the
profiles are still being written.

Every resolver is attempted on every coordinate on purpose: the trace shows both
what resolved and what did not.
"""

from typing import Any

from sirkadiyen_parser.contracts.snapshot import (
    NormalizedSpreadsheetSnapshot,
    NormalizedWorksheet,
)
from sirkadiyen_parser.normalization.courses import normalize_course_title
from sirkadiyen_parser.normalization.dates import resolve_cell_date
from sirkadiyen_parser.normalization.grid import ResolvedCell, WorksheetGrid, merged_range_a1
from sirkadiyen_parser.normalization.groups import parse_group_expression
from sirkadiyen_parser.normalization.instructors import extract_instructors
from sirkadiyen_parser.normalization.times import resolve_cell_time_range

#: The dimension used when tracing group parsing. Real profiles pass their own.
TRACE_GROUP_DIMENSION = "traceGroup"


def build_normalization_trace(snapshot: NormalizedSpreadsheetSnapshot) -> dict[str, Any]:
    """Build a JSON-serializable trace of the shared primitives for a snapshot."""
    return {
        "sourceId": snapshot.source_id,
        "snapshotId": snapshot.snapshot_id,
        "worksheets": [_trace_worksheet(worksheet) for worksheet in snapshot.worksheets],
    }


def _trace_worksheet(worksheet: NormalizedWorksheet) -> dict[str, Any]:
    grid = WorksheetGrid(worksheet)
    return {
        "sheetId": worksheet.sheet_id,
        "title": worksheet.title,
        "mergedRanges": [merged_range_a1(merge) for merge in worksheet.merged_ranges],
        "hiddenRows": [index for index in range(worksheet.row_count) if grid.is_row_hidden(index)],
        "cells": [
            _trace_cell(grid, row_index, column_index)
            for row_index, column_index in grid.covered_coordinates()
        ],
    }


def _trace_cell(grid: WorksheetGrid, row_index: int, column_index: int) -> dict[str, Any]:
    resolved = grid.resolve(row_index, column_index)
    cell = resolved.cell

    trace: dict[str, Any] = {
        "address": resolved.a1_address,
        "valueAddress": resolved.value_a1_address,
        "mergeExpanded": resolved.is_merge_expanded,
        "rowHidden": grid.is_row_hidden(row_index),
        "text": resolved.text,
        "date": _trace_date(resolved),
        "timeRange": _trace_time_range(resolved),
        "group": _trace_group(resolved),
        "title": _trace_title(resolved),
        "instructors": _trace_instructors(resolved),
    }
    if cell is not None and cell.note is not None:
        trace["note"] = cell.note
    return trace


def _trace_date(resolved: ResolvedCell) -> dict[str, Any]:
    resolution = resolve_cell_date(resolved.cell)
    return {
        "value": resolution.value.isoformat() if resolution.value else None,
        "rule": resolution.rule,
        "confidence": resolution.confidence,
        "reason": resolution.reason,
        "weekdayText": resolution.weekday_text,
        "weekdayMatches": resolution.weekday_matches,
    }


def _trace_time_range(resolved: ResolvedCell) -> dict[str, Any]:
    resolution = resolve_cell_time_range(resolved.cell)
    return {
        "start": resolution.start.isoformat() if resolution.start else None,
        "end": resolution.end.isoformat() if resolution.end else None,
        "rule": resolution.rule,
        "confidence": resolution.confidence,
        "reason": resolution.reason,
    }


def _trace_group(resolved: ResolvedCell) -> dict[str, Any]:
    expression = parse_group_expression(resolved.text, dimension=TRACE_GROUP_DIMENSION)
    return {
        "values": list(expression.values),
        "coversAll": expression.covers_all,
        "rule": expression.rule,
        "confidence": expression.confidence,
        "reason": expression.reason,
    }


def _trace_title(resolved: ResolvedCell) -> dict[str, Any]:
    title = normalize_course_title(resolved.display_text or "")
    return {
        "displayTitle": title.display_title,
        "courseIdentity": title.course_identity,
        "rule": title.rule,
        "confidence": title.confidence,
        "lineCount": title.line_count,
    }


def _trace_instructors(resolved: ResolvedCell) -> dict[str, Any]:
    extraction = extract_instructors(resolved.display_text or "")
    return {
        "instructors": list(extraction.instructors),
        "remainder": extraction.remainder,
        "rule": extraction.rule,
        "confidence": extraction.confidence,
    }
