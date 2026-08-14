"""Unit tests for the Grade 3 faculty-practice rotation profile.

The golden tests prove the profile against the real workbooks. These tests pin
the two rules that decide what reaches a calendar — how a hyphen is read, and
what a self-contradicting row publishes — with small, labelled blocks.
"""

from datetime import time
from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import (
    AudienceScope,
    ParserResultStatus,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ScheduleEventType,
)
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers import get_parser
from sirkadiyen_parser.parsers.faculty_practice import (
    METRIC_BLOCKS_READ,
    REASON_AMBIGUOUS_COHORT,
    REASON_COHORT_NOT_STATED,
    REASON_MIXED_COHORT_LETTERS,
    REASON_NO_COHORT_STATED,
    REASON_UNRESOLVED_COHORT,
    parse_faculty_practice_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade3_faculty_practice_v1",
    "1.0.0",
    "facultyPractice",
    NumericDateOrder.UNDECLARED,
    ("curriculumGroup", "facultyPracticeGroup"),
)

BLOCK_TITLE = "DÖNEM-3 HAREKET 2 DİLİMİ - UYGULAMA PROGRAMI (11.10 - 12.10 Uygulaması)"

DEPARTMENTS = [
    "FİZİK TEDAVİ",
    "FİZİK TEDAVİ",
    "ORTOPEDİ",
    "ORTOPEDİ",
    "ROMATOLOJİ",
    "ROMATOLOJİ",
    "SPOR HEKİMLİĞİ",
    "ÇOCUK SAĞLIĞI",
]

#: 2026-10-05, as a spreadsheet date serial.
DATE_SERIAL = 46300

COLUMN_COUNT = 9


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def date_cell(row: int, serial: float = DATE_SERIAL) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": 0,
        "a1Address": f"R{row}C0",
        "effectiveValue": {"kind": "number", "numberValue": serial},
        "effectiveFormat": {"numberFormatType": "DATE"},
    }


def block(
    cohort_rows: list[list[str]],
    *,
    title: str = BLOCK_TITLE,
    department_header: str = "Anabilim/Bilim Dalları",
    merges: list[dict[str, int]] | None = None,
) -> dict[str, Any]:
    """One rotation block: a title, a department header, `TARİH` and date rows."""
    cells: list[dict[str, Any]] = [text_cell(0, 0, title)]
    cells.append(text_cell(1, 0, department_header))
    cells.extend(
        text_cell(1, column + 1, department) for column, department in enumerate(DEPARTMENTS)
    )
    cells.append(text_cell(2, 0, "TARİH"))
    for offset, cohorts in enumerate(cohort_rows):
        row = 3 + offset
        cells.append(date_cell(row, DATE_SERIAL + offset))
        cells.extend(
            text_cell(row, column + 1, cohort) for column, cohort in enumerate(cohorts) if cohort
        )

    return {
        "sheetId": "1",
        "title": "Sayfa1",
        "index": 0,
        "rowCount": 3 + len(cohort_rows),
        "columnCount": COLUMN_COUNT,
        "mergedRanges": merges or [],
        "cells": cells,
    }


def parse(
    worksheets: list[dict[str, Any]],
    *,
    profile: ParserProfileDefinition = PROFILE,
) -> ParseSnapshotResponse:
    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "unit-test",
            "parserProfile": {"name": profile.name, "version": profile.version},
            "sourceContext": {
                "academicYear": "2026-2027",
                "classYear": 3,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "TEST-SOURCE",
                "snapshotId": "test-snapshot",
                "spreadsheetId": "test-spreadsheet",
                "acquiredAtUtc": "2026-08-15T09:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "SHA-256",
                "worksheets": worksheets,
            },
        }
    )
    return parse_faculty_practice_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def cohorts_of(response: ParseSnapshotResponse) -> set[str]:
    return {
        selector.value
        for candidate in response.candidates
        for selector in candidate.audience.selectors
        if selector.dimension == "facultyPracticeGroup"
    }


def test_the_registered_profile_is_the_faculty_practice_implementation() -> None:
    profile = get_profile("grade3_faculty_practice_v1", "1.0.0")

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_faculty_practice_snapshot


def test_a_full_rotation_row_publishes_one_session_per_cohort() -> None:
    response = parse([block([["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])])

    assert response.status is ParserResultStatus.COMPLETED
    assert len(response.candidates) == 8
    assert cohorts_of(response) == {f"A{index}" for index in range(1, 9)}
    assert metrics(response)[METRIC_BLOCKS_READ] == 1

    candidate = response.candidates[0]
    assert candidate.event_type is ScheduleEventType.FACULTY_PRACTICE
    assert candidate.curriculum_block == "HAREKET 2 DİLİMİ"
    assert candidate.start_local_time == time(11, 10)
    assert candidate.end_local_time == time(12, 10)
    assert candidate.departments == ["FİZİK TEDAVİ"]
    # The room is stated in a separate workbook this parse never sees.
    assert candidate.location is None


def test_the_curriculum_group_comes_from_the_cohort_letter() -> None:
    """The workbook says which half of the class it is, so the catalog need not."""
    turkish_a = parse([block([["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])])
    turkish_b = parse([block([["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8"]])])

    def groups(response: ParseSnapshotResponse) -> set[str]:
        return {
            selector.value
            for candidate in response.candidates
            for selector in candidate.audience.selectors
            if selector.dimension == "curriculumGroup"
        }

    assert groups(turkish_a) == {"3-A"}
    assert groups(turkish_b) == {"3-B"}
    assert all(
        candidate.audience.scope is AudienceScope.SELECTED_GROUPS
        for candidate in turkish_a.candidates
    )


def test_a_hyphen_between_cohorts_enumerates_rather_than_spans() -> None:
    """`A1-A5` is two cohorts sitting together, not the run A1 through A5.

    The workbooks prove it: read as a run, this row would state eleven cohorts
    across eight departments. Read as a list, it states each of the eight once.
    """
    response = parse([block([["A1-A5", "A2", "A3", "A4", "A6", "A7", "A8", ""]])])

    assert cohorts_of(response) == {f"A{index}" for index in range(1, 9)}
    assert len(response.candidates) == 8

    together = [
        candidate
        for candidate in response.candidates
        if any(selector.value in {"A1", "A5"} for selector in candidate.audience.selectors)
    ]
    assert {candidate.departments[0] for candidate in together} == {"FİZİK TEDAVİ"}


def test_a_merged_pair_of_columns_is_read_once() -> None:
    """A merged cell states one session for two cohorts, not the same one twice.

    Reading it per covered column would count each cohort twice and make an
    ordinary two-cohort session look like the contradiction the profile refuses.
    """
    merges = [
        {
            "startRowIndex": 3,
            "endRowIndexExclusive": 4,
            "startColumnIndex": 6,
            "endColumnIndexExclusive": 8,
        }
    ]
    response = parse(
        [
            block(
                [["A4", "A5", "A6", "A7", "A8", "A1-A2", "", "A3"]],
                merges=merges,
            )
        ]
    )

    assert response.status is ParserResultStatus.COMPLETED
    assert cohorts_of(response) == {f"A{index}" for index in range(1, 9)}
    assert len(response.candidates) == 8


def test_a_cohort_stated_twice_is_refused_without_refusing_the_row() -> None:
    """The real workbook's one faulty row, in miniature.

    `A4` appears in two departments and `A8` in none. One of the two `A4` cells
    is the missing cohort, but nothing in the document says which, so both are
    refused. The six cohorts stated once each still reach their calendars:
    refusing the row entirely would punish six correct sessions for one typo.
    """
    response = parse([block([["A4", "A1", "A2", "A3", "A4", "A5", "A6", "A7"]])])

    assert len(response.candidates) == 6
    assert cohorts_of(response) == {"A1", "A2", "A3", "A5", "A6", "A7"}

    counts = metrics(response)
    assert counts[f"cells.ignored.{REASON_AMBIGUOUS_COHORT}"] == 2
    assert counts[f"cells.ignored.{REASON_COHORT_NOT_STATED}"] == 1
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS

    # Both addresses are named, because a reviewer has to see both to fix it.
    ambiguous = next(
        warning for warning in response.warnings if "more than one department" in warning.message
    )
    assert "A4" in ambiguous.message


def test_the_rotation_pattern_is_not_used_to_repair_a_faulty_row() -> None:
    """The neighbouring rows rotate by one, so the typo is guessable — and is not.

    Inferring which of two contradictory cells the source meant is inventing a
    fact it does not state, so the cohort is left without a session instead.
    """
    rows = [
        ["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"],
        ["A2", "A3", "A4", "A5", "A6", "A7", "A8", "A1"],
        # Rotating once more would make this row start at A3.
        ["A4", "A4", "A5", "A6", "A7", "A8", "A1", "A2"],
    ]

    response = parse([block(rows)])

    assert cohorts_of(response) == {f"A{index}" for index in range(1, 9)}
    assert len(response.candidates) == 8 + 8 + 6
    assert "A3" not in {
        selector.value
        for candidate in response.candidates
        if candidate.local_date.isoformat() == "2026-10-07"
        for selector in candidate.audience.selectors
    }


def test_a_row_naming_both_curriculum_groups_publishes_nothing() -> None:
    # Two curriculum groups in one rotation row is a structural change, not a
    # typo, and there is no safe half of it to publish.
    response = parse([block([["A1", "A2", "A3", "A4", "B5", "B6", "B7", "B8"]])])

    assert response.candidates == []
    assert metrics(response)[f"rows.ignored.{REASON_MIXED_COHORT_LETTERS}"] == 1


def test_a_dash_states_that_no_cohort_sits_with_a_department() -> None:
    # The source writes a dash deliberately, so it is counted rather than
    # reported as something the reader failed to understand.
    response = parse([block([["A1", "A2", "A3", "A4", "A5", "A6", "A7", "-"]])])

    counts = metrics(response)
    assert counts[f"cells.ignored.{REASON_NO_COHORT_STATED}"] == 1
    assert counts[f"cells.ignored.{REASON_COHORT_NOT_STATED}"] == 1
    assert cohorts_of(response) == {f"A{index}" for index in range(1, 8)}


def test_a_cell_naming_something_other_than_a_cohort_is_refused() -> None:
    response = parse([block([["SINAV", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])])

    assert metrics(response)[f"cells.ignored.{REASON_UNRESOLVED_COHORT}"] == 1
    assert cohorts_of(response) == {f"A{index}" for index in range(2, 9)}


def test_a_cohort_outside_the_bounded_alphabet_is_not_invented() -> None:
    # `A9` is not one of the eight cohorts, and publishing it would address a
    # session to a group no student can declare.
    response = parse([block([["A9", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])])

    assert metrics(response)[f"cells.ignored.{REASON_UNRESOLVED_COHORT}"] == 1
    assert "A9" not in cohorts_of(response)


@pytest.mark.parametrize("header", ("Anabilim/Bilim Dalları", "Anabilim\\Bilim Dalları"))
def test_either_spelling_of_the_department_header_is_recognized(header: str) -> None:
    """The two workbooks separate the words differently, and mean the same row."""
    response = parse(
        [
            block(
                [["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]],
                department_header=header,
            )
        ]
    )

    assert len(response.candidates) == 8


def test_a_block_that_states_no_practice_hour_publishes_nothing() -> None:
    """Without an hour there is no session, only the knowledge that one exists."""
    response = parse(
        [
            block(
                [["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]],
                title="DÖNEM-3 HAREKET 2 DİLİMİ - UYGULAMA PROGRAMI",
            )
        ]
    )

    assert response.candidates == []
    assert any(warning.code == "unresolvedBlockPracticeHours" for warning in response.warnings)


def test_a_snapshot_without_any_block_is_rejected() -> None:
    worksheet = {
        "sheetId": "1",
        "title": "Sayfa1",
        "index": 0,
        "rowCount": 1,
        "columnCount": 2,
        "cells": [text_cell(0, 0, "PRATİK ADI"), text_cell(0, 1, "PRATİK YERİ")],
    }

    response = parse([worksheet])

    assert response.status is ParserResultStatus.REJECTED
    assert any(warning.code == "noFacultyPracticeBlock" for warning in response.warnings)


def test_every_row_of_a_block_is_accounted_for() -> None:
    """Nothing is dropped silently: scanned equals published plus ignored."""
    response = parse([block([["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])])

    counts = metrics(response)
    # A title row, a department header, the `TARİH` marker and one date row.
    assert counts["rows.scanned"] == 4
    assert counts["rows.ignored"] == 1


def test_a_rotation_row_with_an_unreadable_date_is_reported_not_counted_as_prose() -> None:
    """An unreadable date inside a block takes eight sessions off eight calendars.

    One real row did: its date was a serial the workbook had labelled with a
    currency number format. Falling through to the topic-list count made that
    invisible, so a row that names cohorts is a rotation row whatever its first
    cell says.
    """
    worksheet = block([["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"]])
    worksheet["cells"] = [
        text_cell(3, 0, "belirlenecek")
        if cell["rowIndex"] == 3 and cell["columnIndex"] == 0
        else cell
        for cell in worksheet["cells"]
    ]

    response = parse([worksheet])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedDate"] == 1
    unresolved = next(
        warning for warning in response.warnings if "could not be read as a date" in warning.message
    )
    assert "HAREKET 2 DİLİMİ" in unresolved.message
