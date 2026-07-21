"""Worksheet grid access with merged-cell expansion.

The transport snapshot stores a sparse cell collection plus merge, hidden
dimension, and formatting metadata. Parser profiles need positional access that
understands merges, because a merged heading carries its value for every
coordinate it covers while the value itself is stored only in the anchor cell.

Coordinates are zero-based and range ends are exclusive, matching the contract.
A1 addresses are produced only as human-readable evidence.
"""

from dataclasses import dataclass

from sirkadiyen_parser.contracts.parsing import SourceEvidence
from sirkadiyen_parser.contracts.snapshot import (
    CellScalar,
    CellScalarKind,
    GridRange,
    IndexRange,
    NormalizedCell,
    NormalizedWorksheet,
)
from sirkadiyen_parser.normalization.text import normalize_text


class MalformedSnapshotError(ValueError):
    """Raised when a snapshot violates a structural invariant of the contract.

    This is an acquisition defect, not a parsing ambiguity. It must surface as a
    rejected parse rather than as silently dropped source data.
    """


def column_letter(column_index: int) -> str:
    """Return the spreadsheet column letters for a zero-based column index."""
    if column_index < 0:
        raise ValueError("column_index must not be negative")

    letters = ""
    remaining = column_index + 1
    while remaining > 0:
        remaining, remainder = divmod(remaining - 1, 26)
        letters = chr(ord("A") + remainder) + letters
    return letters


def a1_address(row_index: int, column_index: int) -> str:
    """Return the A1 address for zero-based coordinates."""
    if row_index < 0:
        raise ValueError("row_index must not be negative")
    return f"{column_letter(column_index)}{row_index + 1}"


def a1_range(
    start_row_index: int,
    start_column_index: int,
    end_row_index_exclusive: int,
    end_column_index_exclusive: int,
) -> str:
    """Return the A1 range for a zero-based, end-exclusive rectangle."""
    if (
        end_row_index_exclusive <= start_row_index
        or end_column_index_exclusive <= start_column_index
    ):
        raise ValueError("range end indexes must be greater than their start indexes")

    start = a1_address(start_row_index, start_column_index)
    end = a1_address(end_row_index_exclusive - 1, end_column_index_exclusive - 1)
    return start if start == end else f"{start}:{end}"


def merged_range_a1(merged_range: GridRange) -> str:
    """Return the A1 representation of a merged range."""
    return a1_range(
        merged_range.start_row_index,
        merged_range.start_column_index,
        merged_range.end_row_index_exclusive,
        merged_range.end_column_index_exclusive,
    )


def format_number(value: float) -> str:
    """Render a numeric cell value deterministically.

    Integral values lose the decimal part so a group number captured as ``1.0``
    reads as ``"1"``. Everything else keeps full float precision.
    """
    return str(int(value)) if value.is_integer() else repr(value)


def scalar_text(scalar: CellScalar | None) -> str | None:
    """Return the readable text of a typed cell scalar, if it has any."""
    if scalar is None:
        return None
    if scalar.kind is CellScalarKind.TEXT:
        return scalar.text_value
    if scalar.kind is CellScalarKind.NUMBER:
        return None if scalar.number_value is None else format_number(scalar.number_value)
    if scalar.kind is CellScalarKind.BOOLEAN:
        return None if scalar.boolean_value is None else str(scalar.boolean_value)
    return scalar.error_value


def cell_display_text(cell: NormalizedCell | None) -> str | None:
    """Return the text a human would read in the cell.

    The formatted value is preferred because it reflects the source number
    format. Typed values are the fallback when no formatted value was captured.
    """
    if cell is None:
        return None
    if cell.formatted_value is not None:
        return cell.formatted_value
    return scalar_text(cell.effective_value) or scalar_text(cell.user_entered_value)


def cell_number(cell: NormalizedCell | None) -> float | None:
    """Return the numeric effective value of a cell, if it holds one."""
    if cell is None:
        return None
    for scalar in (cell.effective_value, cell.user_entered_value):
        if scalar is not None and scalar.kind is CellScalarKind.NUMBER:
            return scalar.number_value
    return None


@dataclass(frozen=True, slots=True)
class ResolvedCell:
    """A coordinate together with the cell that supplies its value.

    For a coordinate inside a merged range the value comes from the anchor cell,
    and ``merged_range`` records why. Evidence must cite ``value_a1_address`` so
    a reviewer can find the cell that actually held the content.
    """

    row_index: int
    column_index: int
    value_row_index: int
    value_column_index: int
    cell: NormalizedCell | None
    merged_range: GridRange | None

    @property
    def is_merge_expanded(self) -> bool:
        """Whether the value was inherited from another coordinate."""
        return (self.value_row_index, self.value_column_index) != (
            self.row_index,
            self.column_index,
        )

    @property
    def a1_address(self) -> str:
        """The A1 address of the requested coordinate."""
        return a1_address(self.row_index, self.column_index)

    @property
    def value_a1_address(self) -> str:
        """The A1 address of the coordinate that supplied the value."""
        return a1_address(self.value_row_index, self.value_column_index)

    @property
    def display_text(self) -> str | None:
        """The raw readable text of the supplying cell."""
        return cell_display_text(self.cell)

    @property
    def text(self) -> str:
        """The normalized readable text, empty when the cell carries none."""
        return normalize_text(self.display_text or "")

    @property
    def number(self) -> float | None:
        """The numeric effective value of the supplying cell, if any."""
        return cell_number(self.cell)


class WorksheetGrid:
    """Positional, merge-aware access to one normalized worksheet."""

    def __init__(self, worksheet: NormalizedWorksheet) -> None:
        self._worksheet = worksheet
        self._cells = _index_cells(worksheet)
        self._merges = _index_merges(worksheet)
        self._hidden_rows = _index_hidden(worksheet.hidden_rows)
        self._hidden_columns = _index_hidden(worksheet.hidden_columns)

    @property
    def worksheet(self) -> NormalizedWorksheet:
        """The underlying worksheet."""
        return self._worksheet

    def raw_cell(self, row_index: int, column_index: int) -> NormalizedCell | None:
        """Return the cell stored exactly at the coordinate, ignoring merges."""
        return self._cells.get((row_index, column_index))

    def resolve(self, row_index: int, column_index: int) -> ResolvedCell:
        """Return the coordinate's value, expanding merged ranges."""
        merged_range = self._merges.get((row_index, column_index))
        value_row = row_index
        value_column = column_index
        if merged_range is not None:
            value_row = merged_range.start_row_index
            value_column = merged_range.start_column_index

        return ResolvedCell(
            row_index=row_index,
            column_index=column_index,
            value_row_index=value_row,
            value_column_index=value_column,
            cell=self._cells.get((value_row, value_column)),
            merged_range=merged_range,
        )

    def text(self, row_index: int, column_index: int) -> str:
        """Return normalized text for the coordinate, expanding merged ranges."""
        return self.resolve(row_index, column_index).text

    def is_row_hidden(self, row_index: int) -> bool:
        """Whether the source hid the row."""
        return row_index in self._hidden_rows

    def is_column_hidden(self, column_index: int) -> bool:
        """Whether the source hid the column."""
        return column_index in self._hidden_columns

    def occupied_rows(self) -> tuple[int, ...]:
        """Ascending row indexes that hold at least one stored cell."""
        return tuple(sorted({row for row, _ in self._cells}))

    def occupied_columns(self) -> tuple[int, ...]:
        """Ascending column indexes that hold at least one stored cell."""
        return tuple(sorted({column for _, column in self._cells}))

    def covered_coordinates(self) -> tuple[tuple[int, int], ...]:
        """Every coordinate that holds a cell or is covered by a merged range.

        Ordered by row then column so callers remain deterministic.
        """
        return tuple(sorted(set(self._cells) | set(self._merges)))

    def evidence(
        self,
        row_index: int,
        column_index: int,
        *,
        extraction_rule: str,
    ) -> SourceEvidence:
        """Build source evidence for a coordinate.

        The range cites the merged range when the value was inherited, so the
        evidence points at the cell a reviewer must open.
        """
        resolved = self.resolve(row_index, column_index)
        if resolved.merged_range is not None:
            cited_range = merged_range_a1(resolved.merged_range)
        else:
            cited_range = resolved.a1_address

        return SourceEvidence(
            sheet_id=self._worksheet.sheet_id,
            sheet_title=self._worksheet.title,
            range=cited_range,
            raw_text=resolved.display_text,
            extraction_rule=extraction_rule,
        )

    def range_evidence(
        self,
        start_row_index: int,
        start_column_index: int,
        end_row_index_exclusive: int,
        end_column_index_exclusive: int,
        *,
        extraction_rule: str,
        raw_text: str | None = None,
    ) -> SourceEvidence:
        """Build source evidence for a rectangular region."""
        return SourceEvidence(
            sheet_id=self._worksheet.sheet_id,
            sheet_title=self._worksheet.title,
            range=a1_range(
                start_row_index,
                start_column_index,
                end_row_index_exclusive,
                end_column_index_exclusive,
            ),
            raw_text=raw_text,
            extraction_rule=extraction_rule,
        )


def _index_cells(worksheet: NormalizedWorksheet) -> dict[tuple[int, int], NormalizedCell]:
    cells: dict[tuple[int, int], NormalizedCell] = {}
    for cell in worksheet.cells:
        coordinate = (cell.row_index, cell.column_index)
        if coordinate in cells:
            raise MalformedSnapshotError(
                f"Worksheet '{worksheet.sheet_id}' repeats cell {a1_address(*coordinate)}."
            )
        cells[coordinate] = cell
    return cells


def _index_merges(worksheet: NormalizedWorksheet) -> dict[tuple[int, int], GridRange]:
    merges: dict[tuple[int, int], GridRange] = {}
    for merged_range in worksheet.merged_ranges:
        if (
            merged_range.end_row_index_exclusive <= merged_range.start_row_index
            or merged_range.end_column_index_exclusive <= merged_range.start_column_index
        ):
            raise MalformedSnapshotError(
                f"Worksheet '{worksheet.sheet_id}' contains an empty merged range."
            )

        rows = range(merged_range.start_row_index, merged_range.end_row_index_exclusive)
        columns = range(merged_range.start_column_index, merged_range.end_column_index_exclusive)
        for row_index in rows:
            for column_index in columns:
                coordinate = (row_index, column_index)
                if coordinate in merges:
                    raise MalformedSnapshotError(
                        f"Worksheet '{worksheet.sheet_id}' has overlapping merged ranges at "
                        f"{a1_address(*coordinate)}."
                    )
                merges[coordinate] = merged_range
    return merges


def _index_hidden(ranges: list[IndexRange]) -> frozenset[int]:
    hidden: set[int] = set()
    for index_range in ranges:
        hidden.update(range(index_range.start_index, index_range.end_index_exclusive))
    return frozenset(hidden)
