"""Unit tests for the vertical-corridor skill-practice profile.

The golden tests prove the profile against both real Word documents. These pin
the individual rules with small, labelled tables, so a failure names the rule
that broke rather than pointing at a large diff.
"""

from datetime import time
from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import (
    ParserResultStatus,
    ParserWarningSeverity,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ScheduleEventType,
)
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers import get_parser, implemented_profiles
from sirkadiyen_parser.parsers.vertical_corridor import (
    METRIC_PLACE_DEFERRED,
    parse_vertical_corridor_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade2_vertical_corridor_v1",
    "1.2.0",
    "verticalCorridor",
    NumericDateOrder.UNDECLARED,
    ("practiceGroup", "practiceSubgroup"),
    ("Uygulama",),
    group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
)

HEADER = ["Uygulama adı", "Uyg Yeri", "AYDINLATILMIŞ ONAM", "OKSİJEN", "EKİP OLMA"]

#: One dated row's first cell, exactly as the document writes it.
FIRST_SLOT = "1/1\n8 Eylül 2025 Pazartesi\n08:30-10:20"
SECOND_SLOT = "1/2\n9 Eylül 2025 Salı\n10:30-12:20"


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


def worksheet(
    cells: list[dict[str, Any]],
    *,
    column_count: int = 5,
    title: str = "Table 1",
    sheet_id: str = "1",
) -> dict[str, Any]:
    highest_row = max((cell["rowIndex"] for cell in cells), default=0)
    return {
        "sheetId": sheet_id,
        "title": title,
        "index": 0,
        "rowCount": highest_row + 1,
        "columnCount": column_count,
        "mergedRanges": [],
        "cells": cells,
    }


def build(
    *,
    slot_rows: list[list[str | None]],
    header: list[str | None] | None = None,
    place_statement: str | None = None,
) -> list[dict[str, Any]]:
    """Build the usual shape: a header row, an optional place row, then slots."""
    cells: list[dict[str, Any]] = []
    row = 0
    cells.extend(row_cells(row, list(header if header is not None else HEADER)))
    row += 1
    if place_statement is not None:
        cells.extend(row_cells(row, ["Uygulama yeri", place_statement]))
        row += 1
    for values in slot_rows:
        cells.extend(row_cells(row, values))
        row += 1

    return [worksheet(cells)]


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
    return parse_vertical_corridor_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def test_the_registered_profile_is_the_vertical_corridor_implementation() -> None:
    profile = get_profile(PROFILE.name, PROFILE.version)

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_vertical_corridor_snapshot
    assert (PROFILE.name, PROFILE.version) in implemented_profiles()
    # These tests parse through a profile they declare themselves, so what they
    # prove is only worth anything while it matches the registered one.
    assert profile == PROFILE


def test_a_group_cell_becomes_a_session_of_the_practice_its_column_names() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", "A", None, None]]))

    assert response.status is ParserResultStatus.COMPLETED
    candidate = response.candidates[0]
    assert candidate.display_title == "AYDINLATILMIŞ ONAM"
    assert candidate.local_date.isoformat() == "2025-09-08"
    assert candidate.start_local_time == time(8, 30)
    assert candidate.end_local_time == time(10, 20)
    assert [(selector.dimension, selector.value) for selector in candidate.audience.selectors] == [
        ("practiceGroup", "A")
    ]
    # Every row of this document is a vertical-corridor practice; the source it
    # belongs to says so, not a keyword in the cell.
    assert candidate.event_type is ScheduleEventType.VERTICAL_CORRIDOR


def test_one_row_publishes_one_session_per_practice_that_names_a_group() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", "D", "A", None]]))

    assert [candidate.display_title for candidate in response.candidates] == [
        "AYDINLATILMIŞ ONAM",
        "OKSİJEN",
    ]
    assert metrics(response)["cells.scanned"] == 2


def test_a_subgroup_selects_half_a_cohort() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", None, None, "B2"]]))

    candidate = response.candidates[0]
    assert [(selector.dimension, selector.value) for selector in candidate.audience.selectors] == [
        ("practiceSubgroup", "B2")
    ]


def test_a_run_of_letters_names_one_cohort_each() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", None, None, "CD"]]))

    candidate = response.candidates[0]
    assert [selector.value for selector in candidate.audience.selectors] == ["C", "D"]


def test_an_examination_names_its_cohorts_with_hyphens() -> None:
    # `A-B-C-D SINAV` is the only place this source separates cohorts with a
    # hyphen, and it states both the audience and what kind of session it is.
    exam_slot = "*\n28 Mart 2026 Cumartesi\n9.00-16.30"
    response = parse(build(slot_rows=[[exam_slot, None, None, None, "A-B-C-D SINAV"]]))

    candidate = response.candidates[0]
    assert candidate.event_type is ScheduleEventType.EXAM
    assert [selector.value for selector in candidate.audience.selectors] == ["A", "B", "C", "D"]
    assert candidate.start_local_time == time(9, 0)


def test_a_hyphen_that_does_not_separate_cohorts_is_not_split() -> None:
    # `EK-1` is a separately published list, not the cohorts E, K and 1. The
    # rewrite only applies when every part is one of the eight declared letters.
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", None, None, "EK-1"]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.separatelyPublishedCohortList"] == 1


def test_the_english_programmes_cohorts_are_counted_not_published() -> None:
    # The document carries both programmes. Publishing İ1 under a source whose
    # context states the Turkish programme would reach the wrong students.
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", "İ1 grubu\n13.30-15.20", "i1+i2", None]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.cohortOfAnotherProgram"] == 2


def test_a_word_is_never_read_as_a_run_of_cohorts() -> None:
    # With runs of up to eight letters allowed, `Telafi` expands to T, E, L, A,
    # F and I — three of which are real groups. The eight-letter alphabet is what
    # refuses it.
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", "Telafi", None, None]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unsupportedGroupValueShape"] == 1


@pytest.mark.parametrize("value", ("UYGULAMA TELAFİ", "T"))
def test_a_cell_that_states_no_readable_audience_publishes_nothing(value: str) -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", value, None, None]]))

    assert response.candidates == []
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert warning.evidence is not None


def test_a_row_whose_weekday_contradicts_its_date_is_refused_with_its_address() -> None:
    # 24 December 2024 is a Tuesday. The document means 2025, but correcting the
    # year would be a guess, and publishing it would put a practice a year in the
    # past on real calendars.
    response = parse(
        build(slot_rows=[["2/7\n24 Aralık 2024 Çarşamba\n10:30-12:20", "*", None, None, "E"]])
    )

    assert response.candidates == []
    assert metrics(response)["slots.ignored.weekdayContradictsSlotDate"] == 1
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert "2024-12-24" in warning.message
    assert warning.evidence is not None
    assert warning.evidence.range == "A2"


def test_a_dated_row_that_states_no_group_yet_is_not_an_anomaly() -> None:
    # Student Affairs fills this document in over the year, so most dated rows
    # are empty. They are counted, and they raise nothing.
    response = parse(build(slot_rows=[[FIRST_SLOT, "*", None, None, None]]))

    assert response.status is ParserResultStatus.COMPLETED
    assert response.candidates == []
    assert metrics(response)["rows.slot"] == 1
    assert "cells.scanned" not in metrics(response)


def test_a_row_that_states_groups_but_no_readable_slot_is_reported() -> None:
    # This row writes its whole slot on one line. Splitting it would mean
    # guessing where the date ends, so it is refused — loudly, because a session
    # with an audience is being lost.
    response = parse(
        build(slot_rows=[["20 Nisan 2026 Pazartesi 8.30-10.20", None, None, None, "AB"]])
    )

    assert response.candidates == []
    assert metrics(response)["slots.ignored.groupsStatedWithoutReadableSlot"] == 1
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert "20 Nisan 2026" in warning.message


def test_the_table_states_its_room_once_and_a_deferred_room_is_not_published() -> None:
    response = parse(
        build(
            place_statement="Web Sitesinde Yayınlanacak",
            slot_rows=[[FIRST_SLOT, "*", "A", None, None]],
        )
    )

    assert response.candidates[0].location is None
    assert metrics(response)[METRIC_PLACE_DEFERRED] == 1
    assert metrics(response)["rows.placeStatement"] == 1


def test_a_room_the_table_names_is_published() -> None:
    response = parse(
        build(
            place_statement="Beceri Laboratuvarı",
            slot_rows=[[FIRST_SLOT, "*", "A", None, None]],
        )
    )

    assert response.candidates[0].location == "Beceri Laboratuvarı"


def test_a_header_row_is_recognized_without_the_place_header() -> None:
    # One of the seven spring tables leaves that cell empty. Requiring it dropped
    # the whole table — eleven dated rows — into "no table in force".
    response = parse(
        build(
            header=["Uygulama adı", None, "AYDINLATILMIŞ ONAM", None, None],
            slot_rows=[[FIRST_SLOT, "*", "A", None, None]],
        )
    )

    assert len(response.candidates) == 1
    assert metrics(response)["rows.headerRow"] == 1


def test_an_instructor_is_read_from_the_column_header() -> None:
    response = parse(
        build(
            header=["Uygulama adı", "Uyg Yeri", "EKİP OLMA\n\n(Doç. Dr. Ayşe Nilüfer ALÇALAR)"],
            slot_rows=[[FIRST_SLOT, "*", "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.display_title == "EKİP OLMA"
    assert candidate.instructor == "Doç. Dr. Ayşe Nilüfer ALÇALAR"


def test_a_header_whose_closing_bracket_is_missing_still_names_one_practice() -> None:
    # Four of the seven spring tables never close the bracket. Without this the
    # same practice reaches calendars under two titles, one ending mid-bracket.
    response = parse(
        build(
            header=["Uygulama adı", "Uyg Yeri", "OKSİJEN\n\n(Doç. Dr. Bengüsu MİRASOĞLU"],
            slot_rows=[[FIRST_SLOT, "*", "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.display_title == "OKSİJEN"
    assert candidate.instructor == "Doç. Dr. Bengüsu MİRASOĞLU"


def test_a_stray_bracket_that_names_no_instructor_stays_in_the_title() -> None:
    response = parse(
        build(
            header=["Uygulama adı", "Uyg Yeri", "OKSİJEN (Sualtı Hekimliği"],
            slot_rows=[[FIRST_SLOT, "*", "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.display_title == "OKSİJEN (Sualtı Hekimliği"
    assert candidate.instructor is None


def test_the_dissection_rotation_is_deferred_to_the_anatomy_sources() -> None:
    response = parse(build(slot_rows=[["Anatomi (17)", None, None, None, None]]))

    assert response.candidates == []
    assert metrics(response)["rows.ignored.outOfScopeGroupRotation"] == 1


def test_every_row_of_the_worksheet_is_accounted_for() -> None:
    cells = [
        text_cell(0, 0, "Beceri uygulamaları tarihlerinde güncelleme yapıldığında"),
        *row_cells(1, list(HEADER)),
        *row_cells(2, ["Uygulama yeri", "Web Sitesinde Yayınlanacak"]),
        *row_cells(3, [FIRST_SLOT, "*", "A", None, None]),
        *row_cells(4, [SECOND_SLOT, "*", None, None, None]),
        *row_cells(5, ["Anatomi (17)"]),
        # A topic line below the table, which is neither a header nor a slot.
        *row_cells(6, ["Uygulama konu başlıkları"]),
    ]

    response = parse([worksheet(cells)])
    values = metrics(response)

    assert values["rows.scanned"] == 7
    accounted = (
        values["rows.headerRow"]
        + values["rows.placeStatement"]
        + values["rows.slot"]
        + values["rows.ignored"]
    )
    assert accounted == values["rows.scanned"]


def test_a_second_table_replaces_the_first_rather_than_ending_the_worksheet() -> None:
    # The spring document repeats its header for every Word table, and the
    # converter makes each one a worksheet — but a header may also reappear
    # inside one.
    cells = [
        *row_cells(0, list(HEADER)),
        *row_cells(1, [FIRST_SLOT, "*", "A", None, None]),
        *row_cells(2, ["Uygulama adı", "Uyg Yeri", "SH ÖYKÜ ALMA", None, None]),
        *row_cells(3, [SECOND_SLOT, "*", "B", None, None]),
    ]

    response = parse([worksheet(cells)])

    assert [candidate.display_title for candidate in response.candidates] == [
        "AYDINLATILMIŞ ONAM",
        "SH ÖYKÜ ALMA",
    ]
    assert metrics(response)["rows.headerRow"] == 2


def test_a_worksheet_without_a_header_row_is_rejected_not_silently_empty() -> None:
    response = parse([worksheet(row_cells(0, ["1/1", "*", "A"]))])

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_identity_separates_the_cohorts_a_session_is_for() -> None:
    response = parse(
        build(
            slot_rows=[
                [FIRST_SLOT, "*", "A", None, None],
                [SECOND_SLOT, "*", "B", None, None],
            ]
        )
    )

    first, second = response.candidates
    assert first.stable_identity != second.stable_identity
