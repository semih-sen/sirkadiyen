import pytest

from sirkadiyen_parser.normalization.grid import (
    MalformedSnapshotError,
    WorksheetGrid,
    a1_address,
    a1_range,
    column_letter,
    format_number,
)
from tests.support.snapshots import index_range, merged_range, text_cell, worksheet


@pytest.mark.parametrize(
    ("index", "expected"),
    [(0, "A"), (25, "Z"), (26, "AA"), (27, "AB"), (51, "AZ"), (52, "BA")],
)
def test_column_letter(index: int, expected: str) -> None:
    assert column_letter(index) == expected


def test_a1_address_is_one_based_for_rows() -> None:
    assert a1_address(0, 0) == "A1"
    assert a1_address(4, 2) == "C5"


def test_a1_range_collapses_a_single_cell() -> None:
    assert a1_range(1, 0, 2, 1) == "A2"
    assert a1_range(1, 0, 4, 1) == "A2:A4"


def test_a1_range_rejects_an_empty_rectangle() -> None:
    with pytest.raises(ValueError, match="greater than"):
        a1_range(2, 0, 2, 1)


def test_format_number_drops_the_decimal_part_of_integral_values() -> None:
    assert format_number(1.0) == "1"
    assert format_number(45973.0) == "45973"
    assert format_number(0.375) == "0.375"


def test_merged_range_supplies_the_value_for_every_covered_coordinate() -> None:
    grid = WorksheetGrid(
        worksheet(
            [text_cell(1, 0, "12.11.2025")],
            merged_ranges=[merged_range(1, 0, 4, 1)],
        )
    )

    anchor = grid.resolve(1, 0)
    inherited = grid.resolve(3, 0)

    assert anchor.text == "12.11.2025"
    assert not anchor.is_merge_expanded
    assert inherited.text == "12.11.2025"
    assert inherited.is_merge_expanded
    assert inherited.a1_address == "A4"
    assert inherited.value_a1_address == "A2"


def test_evidence_cites_the_merged_range_when_the_value_was_inherited() -> None:
    grid = WorksheetGrid(
        worksheet(
            [text_cell(1, 0, "12.11.2025")],
            merged_ranges=[merged_range(1, 0, 4, 1)],
        )
    )

    evidence = grid.evidence(3, 0, extraction_rule="dateColumn")

    assert evidence.range == "A2:A4"
    assert evidence.raw_text == "12.11.2025"
    assert evidence.extraction_rule == "dateColumn"
    assert evidence.sheet_title == "Test Sheet"


def test_evidence_cites_the_cell_when_no_merge_applies() -> None:
    grid = WorksheetGrid(worksheet([text_cell(2, 1, "09:00-10:50")]))

    assert grid.evidence(2, 1, extraction_rule="timeColumn").range == "B3"


def test_uncovered_coordinate_resolves_to_an_empty_cell() -> None:
    resolved = WorksheetGrid(worksheet([])).resolve(5, 5)

    assert resolved.cell is None
    assert resolved.text == ""
    assert resolved.number is None


def test_hidden_rows_are_reported_rather_than_removed() -> None:
    grid = WorksheetGrid(
        worksheet(
            [text_cell(6, 2, "İPTAL")],
            hidden_rows=[index_range(6, 7)],
        )
    )

    assert grid.is_row_hidden(6)
    assert not grid.is_row_hidden(5)
    assert grid.text(6, 2) == "İPTAL"


def test_covered_coordinates_include_merge_covered_blanks_in_reading_order() -> None:
    grid = WorksheetGrid(
        worksheet(
            [text_cell(0, 0, "Anchor"), text_cell(0, 1, "Other")],
            merged_ranges=[merged_range(0, 0, 2, 1)],
        )
    )

    assert grid.covered_coordinates() == ((0, 0), (0, 1), (1, 0))


def test_repeated_cell_coordinates_are_rejected() -> None:
    with pytest.raises(MalformedSnapshotError, match="repeats cell A1"):
        WorksheetGrid(worksheet([text_cell(0, 0, "A"), text_cell(0, 0, "B")]))


def test_overlapping_merged_ranges_are_rejected() -> None:
    overlapping = [merged_range(0, 0, 3, 1), merged_range(2, 0, 4, 1)]

    with pytest.raises(MalformedSnapshotError, match="overlapping merged ranges"):
        WorksheetGrid(worksheet([], merged_ranges=overlapping))
