"""Unit tests for the anatomy dissection group-list profile.

The golden tests prove the profile against both real documents. These pin the
individual rules with small tables, above all the day-block rule: the same
document states a day as a vertical merge in some rows and as a date typed into
the middle of three rows in others.
"""

from datetime import time
from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import (
    AudienceScope,
    ParserResultStatus,
    ParserWarningSeverity,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ScheduleEventType,
)
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers import get_parser, implemented_profiles
from sirkadiyen_parser.parsers.anatomy import (
    CONFIDENCE_DATE_FROM_DAY_BLOCK,
    CONFIDENCE_REASON_DAY_BLOCK,
    parse_anatomy_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade2_anatomy_autumn_v1",
    "1.2.0",
    "anatomy",
    NumericDateOrder.UNDECLARED,
    ("anatomyGroup",),
    ("Diseksiyon",),
)

HOURS = ("13:30-14:20", "14:30-15:20", "15:30-16:20")


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def row_cells(row: int, values: list[str | None]) -> list[dict[str, Any]]:
    return [
        text_cell(row, column, value) for column, value in enumerate(values) if value is not None
    ]


def merge(
    start_row: int,
    end_row_exclusive: int,
    start_column: int,
    end_column_exclusive: int,
) -> dict[str, int]:
    return {
        "startRowIndex": start_row,
        "endRowIndexExclusive": end_row_exclusive,
        "startColumnIndex": start_column,
        "endColumnIndexExclusive": end_column_exclusive,
    }


def worksheet(
    cells: list[dict[str, Any]],
    *,
    merged_ranges: list[dict[str, int]] | None = None,
    title: str = "Table 1",
) -> dict[str, Any]:
    highest_row = max((cell["rowIndex"] for cell in cells), default=0)
    return {
        "sheetId": "1",
        "title": title,
        "index": 0,
        "rowCount": highest_row + 1,
        "columnCount": 3,
        "mergedRanges": merged_ranges or [],
        "cells": cells,
    }


def centred_day(
    start_row: int,
    date_text: str,
    groups: tuple[str, str, str],
) -> list[dict[str, Any]]:
    """A day written the earlier way: the date typed into the middle row."""
    return [
        *row_cells(start_row, [None, HOURS[0], groups[0]]),
        *row_cells(start_row + 1, [date_text, HOURS[1], groups[1]]),
        *row_cells(start_row + 2, [None, HOURS[2], groups[2]]),
    ]


def merged_day(
    start_row: int,
    date_text: str,
    groups: tuple[str, str, str],
) -> tuple[list[dict[str, Any]], dict[str, int]]:
    """A day written the later way: one date vertically merged over three hours."""
    cells = [
        *row_cells(start_row, [date_text, HOURS[0], groups[0]]),
        *row_cells(start_row + 1, [None, HOURS[1], groups[1]]),
        *row_cells(start_row + 2, [None, HOURS[2], groups[2]]),
    ]
    return cells, merge(start_row, start_row + 3, 0, 1)


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
                "academicYear": "2025-2026",
                "classYear": 2,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "TEST-SOURCE",
                "snapshotId": "test-snapshot",
                "spreadsheetId": "test-document",
                "acquiredAtUtc": "2026-07-25T09:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "SHA-256",
                "worksheets": worksheets,
            },
        }
    )
    return parse_anatomy_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


@pytest.mark.parametrize("name", ("grade2_anatomy_autumn_v1", "grade2_anatomy_spring_v1"))
def test_both_semesters_are_registered_against_one_implementation(name: str) -> None:
    profile = get_profile(name, "1.2.0")

    assert profile is not None
    assert get_parser(name, "1.2.0") is parse_anatomy_snapshot
    assert (name, "1.2.0") in implemented_profiles()


def test_the_test_profile_matches_the_registered_one() -> None:
    assert get_profile(PROFILE.name, PROFILE.version) == PROFILE


def test_a_merged_day_publishes_one_session_per_group() -> None:
    cells, merged = merged_day(0, "4 Kasım 2025 Salı", ("1", "2", "3"))

    response = parse([worksheet(cells, merged_ranges=[merged])])

    assert response.status is ParserResultStatus.COMPLETED
    assert [candidate.audience.selectors[0].value for candidate in response.candidates] == [
        "1",
        "2",
        "3",
    ]
    first = response.candidates[0]
    assert first.local_date.isoformat() == "2025-11-04"
    assert first.start_local_time == time(13, 30)
    assert first.end_local_time == time(14, 20)
    assert first.event_type is ScheduleEventType.ANATOMY_PRACTICE
    assert first.audience.scope is AudienceScope.SELECTED_GROUPS
    assert first.audience.selectors[0].dimension == "anatomyGroup"
    # A merge is the document itself saying the three hours are one day, so
    # nothing about the date is this profile's inference: the three hours score
    # the same as any date written with a month name.
    assert {candidate.confidence for candidate in response.candidates} == {0.9}
    assert {indicator.reason for indicator in response.confidence_indicators} == {"monthNameDate"}


def test_a_day_whose_date_sits_in_the_middle_row_publishes_all_three_hours() -> None:
    # The earlier rows of both documents write a day this way: no merge, the
    # date simply typed into the middle of three rows. Publishing only the row
    # that states it would give two of the three groups no session at all.
    response = parse([worksheet(centred_day(0, "2 Eylül 2025 Salı", ("1", "2", "3")))])

    assert [
        (candidate.local_date.isoformat(), candidate.audience.selectors[0].value)
        for candidate in response.candidates
    ] == [("2025-09-02", "1"), ("2025-09-02", "2"), ("2025-09-02", "3")]
    assert metrics(response)["rows.dateFromDayBlock"] == 2


def test_a_date_attributed_from_another_row_is_reported_at_lower_confidence() -> None:
    response = parse([worksheet(centred_day(0, "2 Eylül 2025 Salı", ("1", "2", "3")))])

    attributed = [
        candidate
        for candidate in response.candidates
        if candidate.confidence == CONFIDENCE_DATE_FROM_DAY_BLOCK
    ]
    assert len(attributed) == 2
    # The row that states the date scores as an ordinary month-name date; the
    # two beside it say in their own indicator where their date came from.
    by_reason = [indicator.reason for indicator in response.confidence_indicators]
    assert by_reason.count(CONFIDENCE_REASON_DAY_BLOCK) == 2
    assert by_reason.count("monthNameDate") == 1


def test_a_day_ends_where_the_hours_stop_advancing() -> None:
    cells = [
        *centred_day(0, "2 Eylül 2025 Salı", ("1", "2", "3")),
        *centred_day(3, "4 Eylül 2025 Perşembe", ("2", "3", "1")),
    ]

    response = parse([worksheet(cells)])

    assert metrics(response)["days.detected"] == 2
    assert [candidate.local_date.isoformat() for candidate in response.candidates] == [
        "2025-09-02",
        "2025-09-02",
        "2025-09-02",
        "2025-09-04",
        "2025-09-04",
        "2025-09-04",
    ]


def test_a_run_of_hours_stating_no_date_publishes_nothing() -> None:
    cells = [
        *row_cells(0, [None, HOURS[0], "1"]),
        *row_cells(1, [None, HOURS[1], "2"]),
        *row_cells(2, [None, HOURS[2], "3"]),
    ]

    response = parse([worksheet(cells)])

    assert response.candidates == []
    assert metrics(response)["days.ignored.dayBlockWithoutDate"] == 1
    # Every hour of the day is accounted for, not just the one that raised.
    assert metrics(response)["rows.ignored"] == 3


def test_a_run_of_hours_stating_two_dates_publishes_nothing() -> None:
    cells = [
        *row_cells(0, ["2 Eylül 2025 Salı", HOURS[0], "1"]),
        *row_cells(1, ["4 Eylül 2025 Perşembe", HOURS[1], "2"]),
        *row_cells(2, [None, HOURS[2], "3"]),
    ]

    response = parse([worksheet(cells)])

    assert response.candidates == []
    assert metrics(response)["days.ignored.dayBlockWithSeveralDates"] == 1


def test_a_day_whose_weekday_contradicts_its_date_is_refused_whole() -> None:
    # 9 April 2025 is a Wednesday; the spring document means 2026. The day is
    # the unit of refusal: publishing the hour that states the date and dropping
    # the two beside it would give two groups no session and the third one that
    # may not be theirs.
    cells, merged = merged_day(0, "9 Nisan 2025 Perşembe", ("2", "3", "1"))

    response = parse([worksheet(cells, merged_ranges=[merged])])

    assert response.candidates == []
    assert metrics(response)["days.ignored.weekdayContradictsSlotDate"] == 1
    assert metrics(response)["rows.ignored.rowInRefusedDay"] == 2
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert "2025-04-09" in warning.message
    assert warning.evidence is not None


def test_a_group_outside_the_three_this_source_states_is_refused() -> None:
    cells, merged = merged_day(0, "4 Kasım 2025 Salı", ("1", "4", "3"))

    response = parse([worksheet(cells, merged_ranges=[merged])])

    assert len(response.candidates) == 2
    assert metrics(response)["rows.ignored.unsupportedAnatomyGroupValue"] == 1


def test_the_document_heading_is_not_read_as_an_hour() -> None:
    # The heading is one cell merged across all three columns, so every column
    # of that row reads back as the heading text.
    cells = [
        text_cell(0, 0, "2025-2026 Güz YARIYILI- DÖNEM II ANATOMİ UYGULAMA GRUPLARI LİSTESİ"),
        *centred_day(1, "2 Eylül 2025 Salı", ("1", "2", "3")),
    ]

    response = parse([worksheet(cells, merged_ranges=[merge(0, 1, 0, 3)])])

    assert response.status is ParserResultStatus.COMPLETED
    assert len(response.candidates) == 3
    assert metrics(response)["rows.ignored.notADissectionRow"] == 1


def test_every_scanned_row_is_published_or_counted() -> None:
    cells = [
        text_cell(0, 0, "2025-2026 Güz YARIYILI"),
        *centred_day(1, "2 Eylül 2025 Salı", ("1", "2", "3")),
        *row_cells(4, [None, "belirsiz", "1"]),
    ]

    response = parse([worksheet(cells, merged_ranges=[merge(0, 1, 0, 3)])])
    values = metrics(response)

    assert values["rows.scanned"] == 5
    assert values["rows.ignored"] + values["candidates.emitted"] == values["rows.scanned"]
    assert values["rows.ignored.unresolvedDissectionTimeRange"] == 1


def test_the_lesson_title_comes_from_the_profile_not_from_the_parser() -> None:
    # The rows of this document name no lesson: they are a date, an hour and a
    # group. The title is the one the annual program uses for the same lesson.
    cells, merged = merged_day(0, "4 Kasım 2025 Salı", ("1", "2", "3"))

    response = parse([worksheet(cells, merged_ranges=[merged])])

    assert {candidate.display_title for candidate in response.candidates} == {"Diseksiyon"}
    assert {candidate.normalized_course_identity for candidate in response.candidates} == {
        "diseksiyon"
    }


def test_a_profile_that_names_no_lesson_publishes_nothing() -> None:
    nameless = ParserProfileDefinition(
        PROFILE.name,
        PROFILE.version,
        "anatomy",
        NumericDateOrder.UNDECLARED,
        ("anatomyGroup",),
    )
    cells, merged = merged_day(0, "4 Kasım 2025 Salı", ("1", "2", "3"))

    response = parse([worksheet(cells, merged_ranges=[merged])], profile=nameless)

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_a_worksheet_without_dissection_hours_is_rejected_not_silently_empty() -> None:
    response = parse([worksheet(row_cells(0, ["2 Eylül 2025 Salı"]))])

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_identity_separates_the_group_and_the_hour() -> None:
    cells, merged = merged_day(0, "4 Kasım 2025 Salı", ("1", "2", "3"))

    response = parse([worksheet(cells, merged_ranges=[merged])])

    identities = {candidate.stable_identity for candidate in response.candidates}
    assert len(identities) == 3
