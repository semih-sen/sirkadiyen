"""Unit tests for the Grade 3 microbiology/pathology practice profile.

The golden test proves the profile against the real document. These tests pin
the individual rules with small, labelled worksheets, so a failure names the rule
that broke rather than pointing at a large diff.
"""

from datetime import time
from typing import Any

from sirkadiyen_parser.contracts.parsing import (
    AudienceScope,
    ParserResultStatus,
    ParserWarningSeverity,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ScheduleEventType,
)
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers.micropathology_practice import (
    METRIC_INSTRUCTORS_UNKNOWN,
    METRIC_SUBJECTS_UNKNOWN,
    WARNING_NO_DEFAULT_TIME,
    WARNING_UNKNOWN_INSTRUCTOR,
    WARNING_UNKNOWN_SUBJECT,
    WARNING_UNSUPPORTED_GROUP,
    parse_micropathology_practice_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition

PROFILE = ParserProfileDefinition(
    "grade3_micropathology_practice_v1",
    "1.0.0",
    "micropathologyPractice",
    NumericDateOrder.DAY_FIRST,
    ("microPathologyGroup",),
)

DATE_COLUMN = 3
MICRO_COLUMN = 4
PATHOLOGY_COLUMN = 5
COLUMN_COUNT = 6


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def row_cells(row: int, values: dict[int, str]) -> list[dict[str, Any]]:
    return [text_cell(row, column, value) for column, value in values.items()]


def worksheet(rows: list[dict[str, Any]]) -> dict[str, Any]:
    highest_row = max((cell["rowIndex"] for cell in rows), default=0)
    return {
        "sheetId": "1",
        "title": "Table 1",
        "index": 0,
        "rowCount": highest_row + 1,
        "columnCount": COLUMN_COUNT,
        "mergedRanges": [],
        "cells": rows,
    }


def build(
    data_rows: list[dict[int, str]],
    *,
    default_time: str | None = "14.30-16.20",
) -> list[dict[str, Any]]:
    """Build the header block and the given data rows below it."""
    cells: list[dict[str, Any]] = row_cells(
        0,
        {
            DATE_COLUMN: "UYGULAMA TARİHLERİ",
            MICRO_COLUMN: "Mikrobiyoloji",
            PATHOLOGY_COLUMN: "Tıbbi Patoloji",
        },
    )
    if default_time is not None:
        cells.extend(row_cells(1, {MICRO_COLUMN: default_time}))

    for offset, values in enumerate(data_rows):
        cells.extend(row_cells(2 + offset, values))

    return [worksheet(cells)]


def parse(
    worksheets: list[dict[str, Any]],
    *,
    program_language: str = "turkish",
) -> ParseSnapshotResponse:
    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "test",
            "parserProfile": {"name": PROFILE.name, "version": PROFILE.version},
            "sourceContext": {
                "academicYear": "2026-2027",
                "classYear": 3,
                "programLanguage": program_language,
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "G3-TR-MICROPATHO-PRACTICE",
                "snapshotId": "snapshot",
                "spreadsheetId": "spreadsheet",
                "acquiredAtUtc": "2026-09-05T00:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "sha256",
                "worksheets": worksheets,
                "diagnostics": [],
            },
        }
    )
    return parse_micropathology_practice_snapshot(request, PROFILE)


def metric(response: ParseSnapshotResponse, name: str) -> float | None:
    return next((entry.value for entry in response.metrics if entry.name == name), None)


def test_publishes_both_tracks_of_a_dated_row() -> None:
    response = parse(
        build(
            [
                {
                    DATE_COLUMN: "13.10.2026",
                    MICRO_COLUMN: "B1- (KL 1)",
                    PATHOLOGY_COLUMN: "A1- (H) (BB-GÜ)",
                }
            ]
        )
    )

    assert response.status is ParserResultStatus.COMPLETED
    assert len(response.candidates) == 2

    micro = next(c for c in response.candidates if c.departments == ["Mikrobiyoloji"])
    pathology = next(c for c in response.candidates if c.departments == ["Tıbbi Patoloji"])

    # The two tracks are crossed: microbiology teaches Kan-Lenfoid to B1 while
    # pathology teaches Hareket to A1 on the very same date.
    assert micro.audience.selectors[0].dimension == "microPathologyGroup"
    assert micro.audience.selectors[0].value == "B1"
    assert micro.display_title == "Mikrobiyoloji Uygulama - Kan-Lenfoid 1"
    assert micro.instructor is None
    assert micro.event_type is ScheduleEventType.PRACTICE
    assert micro.audience.scope is AudienceScope.SELECTED_GROUPS

    assert pathology.audience.selectors[0].value == "A1"
    assert pathology.display_title == "Tıbbi Patoloji Uygulama - Hareket"
    assert pathology.instructor == "Mebrure Bilge Bilgiç (BB), Gökçen Ünverengil (GÜ)"
    assert pathology.start_local_time == time(14, 30)
    assert pathology.end_local_time == time(16, 20)


def test_program_language_comes_from_the_source_context() -> None:
    # The document never states the language; the same file serves both programs.
    turkish = parse(build([{DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "A1- (H)"}]))
    english = parse(
        build([{DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "A1- (H)"}]),
        program_language="english",
    )

    assert turkish.candidates[0].program_language.value == "turkish"
    assert english.candidates[0].program_language.value == "english"
    # Only the language differs, so the stable identity differs and neither would
    # collide with the other on a calendar.
    assert turkish.candidates[0].stable_identity != english.candidates[0].stable_identity


def test_inline_time_override_wins_over_the_default() -> None:
    response = parse(
        build(
            [
                {
                    DATE_COLUMN: "25.05.2027 (13.30-15.20)",
                    PATHOLOGY_COLUMN: "A1- (SND 2) (ŞÖS-BYE)",
                }
            ]
        )
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2027-05-25"
    assert candidate.start_local_time == time(13, 30)
    assert candidate.end_local_time == time(15, 20)


def test_unknown_subject_is_kept_verbatim_with_a_warning() -> None:
    response = parse(build([{DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "A1- (ZZZ 1)"}]))

    # The lesson is still published — losing a subject label loses nothing the
    # group value protects — but the anomaly is surfaced.
    assert len(response.candidates) == 1
    assert response.candidates[0].display_title == "Mikrobiyoloji Uygulama - ZZZ 1"
    assert metric(response, METRIC_SUBJECTS_UNKNOWN) == 1
    assert any(w.code == WARNING_UNKNOWN_SUBJECT for w in response.warnings)


def test_unknown_instructor_token_is_kept_verbatim_with_a_warning() -> None:
    response = parse(build([{DATE_COLUMN: "06.10.2026", PATHOLOGY_COLUMN: "A1- (H) (BB-ZZ)"}]))

    candidate = response.candidates[0]
    assert candidate.instructor == "Mebrure Bilge Bilgiç (BB), ZZ"
    assert metric(response, METRIC_INSTRUCTORS_UNKNOWN) == 1
    assert any(w.code == WARNING_UNKNOWN_INSTRUCTOR for w in response.warnings)


def test_unsupported_group_is_refused_not_published() -> None:
    response = parse(build([{DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "C3- (H)"}]))

    # The group is the audience; publishing to a guessed one is the failure this
    # source exists to avoid, so the cell is refused rather than kept verbatim.
    assert response.candidates == []
    assert metric(response, "cells.ignored.unsupportedPracticeGroupValue") == 1
    assert any(w.code == WARNING_UNSUPPORTED_GROUP for w in response.warnings)


def test_marker_and_spacer_rows_are_ignored_without_warnings() -> None:
    response = parse(
        build(
            [
                {DATE_COLUMN: "", MICRO_COLUMN: "HAREKET", PATHOLOGY_COLUMN: "KAN-LENFOİD"},
                {MICRO_COLUMN: "DİLİM SINAVI"},
                {DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "A1- (H)"},
            ]
        )
    )

    assert len(response.candidates) == 1
    assert response.status is ParserResultStatus.COMPLETED
    assert response.warnings == []
    assert metric(response, "rows.ignored") is None


def test_missing_default_hour_rejects_rather_than_inventing_a_time() -> None:
    response = parse(
        build([{DATE_COLUMN: "06.10.2026", MICRO_COLUMN: "A1- (H)"}], default_time=None)
    )

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []
    assert any(w.code == WARNING_NO_DEFAULT_TIME for w in response.warnings)


def test_a_bad_date_with_digits_is_reported_but_a_wordless_marker_is_not() -> None:
    response = parse(
        build(
            [
                {DATE_COLUMN: "32.13.2026", MICRO_COLUMN: "A1- (H)"},
                {DATE_COLUMN: "YARIYIL TATİLİ"},
            ]
        )
    )

    # The impossible date is an anomaly worth a warning; the wordless holiday
    # marker is a structural row and stays silent.
    assert response.candidates == []
    assert metric(response, "rows.ignored") == 1
    assert any(w.severity is ParserWarningSeverity.WARNING for w in response.warnings)
