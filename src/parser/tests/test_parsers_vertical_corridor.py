"""Unit tests for the vertical-corridor skill-practice profile.

The golden test proves the profile against the real 2026-2027 workbook. These
pin the individual rules with small, labelled tables, so a failure names the rule
that broke rather than pointing at a large diff.

The workbook writes the header corner as ``Uygulama yeri``, starts the practices
in the next column with no separate place column, and states each practice's
title, instructor and room on three lines of its header cell (ADR-147).
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
from sirkadiyen_parser.parsers.vertical_corridor import (
    METRIC_PLACE_DEFERRED,
    parse_vertical_corridor_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade2_vertical_corridor_v1",
    "1.3.0",
    "verticalCorridor",
    NumericDateOrder.UNDECLARED,
    ("practiceGroup", "practiceSubgroup"),
    ("Uygulama",),
    group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
)

#: The header row exactly as the workbook writes it: the corner cell names the
#: slot column, and each practice states its title, instructor and room on three
#: lines. The first practice's room is a real place, the second defers to the
#: amphitheatre program, and the third names a department as its room.
HEADER = [
    "Uygulama yeri",
    "OKSİJEN\nDoç. Dr. Bengüsu MİRASOĞLU\nSualtı Hekimliği",
    "AYDINLATILMIŞ ONAM\nProf. Dr. Ayşe PALANDUZ\nAmfi programına bakınız",
    "EKİP OLMA\nDoç. Dr. A. Nilüfer ALÇALAR\nTemel Bilimler Tıp Eğitimi AD",
]

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
    column_count: int | None = None,
    merged_ranges: list[dict[str, int]] | None = None,
    title: str = "TR",
    sheet_id: str = "1",
) -> dict[str, Any]:
    highest_row = max((cell["rowIndex"] for cell in cells), default=0)
    highest_column = max((cell["columnIndex"] for cell in cells), default=0)
    return {
        "sheetId": sheet_id,
        "title": title,
        "index": 0,
        "rowCount": highest_row + 1,
        "columnCount": column_count if column_count is not None else highest_column + 1,
        "mergedRanges": merged_ranges or [],
        "cells": cells,
    }


def build(
    *,
    slot_rows: list[list[str | None]],
    header: list[str | None] | None = None,
) -> list[dict[str, Any]]:
    """Build the usual shape: a header row, then slot rows."""
    cells: list[dict[str, Any]] = []
    row = 0
    cells.extend(row_cells(row, list(header if header is not None else HEADER)))
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
    response = parse(build(slot_rows=[[FIRST_SLOT, "A", None, None]]))

    assert response.status is ParserResultStatus.COMPLETED
    candidate = response.candidates[0]
    assert candidate.display_title == "OKSİJEN"
    assert candidate.local_date.isoformat() == "2025-09-08"
    assert candidate.start_local_time == time(8, 30)
    assert candidate.end_local_time == time(10, 20)
    assert [(selector.dimension, selector.value) for selector in candidate.audience.selectors] == [
        ("practiceGroup", "A")
    ]
    # Every row of this document is a vertical-corridor practice; the source it
    # belongs to says so, not a keyword in the cell.
    assert candidate.event_type is ScheduleEventType.VERTICAL_CORRIDOR


def test_a_practice_states_its_own_instructor_and_room_in_its_header_cell() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "A", None, None]]))

    candidate = response.candidates[0]
    assert candidate.instructor == "Doç. Dr. Bengüsu MİRASOĞLU"
    assert candidate.location == "Sualtı Hekimliği"


def test_one_row_publishes_one_session_per_practice_that_names_a_group() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, "A", "B", None]]))

    assert [candidate.display_title for candidate in response.candidates] == [
        "OKSİJEN",
        "AYDINLATILMIŞ ONAM",
    ]
    assert metrics(response)["cells.scanned"] == 2


def test_a_subgroup_selects_half_a_cohort() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, None, None, "B2"]]))

    candidate = response.candidates[0]
    assert [(selector.dimension, selector.value) for selector in candidate.audience.selectors] == [
        ("practiceSubgroup", "B2")
    ]


def test_a_run_of_letters_names_one_cohort_each() -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, None, None, "CD"]]))

    candidate = response.candidates[0]
    assert [selector.value for selector in candidate.audience.selectors] == ["C", "D"]


def test_a_makeup_for_the_whole_class_is_read_as_covering_all_students() -> None:
    # `Telafi (Tüm Gruplar)` marks a makeup for the whole class. The makeup word
    # and the brackets say nothing about who attends and are stripped; what is
    # left names every group.
    response = parse(build(slot_rows=[[FIRST_SLOT, "Telafi (Tüm Gruplar)", None, None]]))

    candidate = response.candidates[0]
    assert candidate.audience.scope is AudienceScope.ALL_STUDENTS_IN_PROGRAM
    assert candidate.audience.selectors == []
    # No makeup event type exists, and a makeup of a vertical-corridor practice is
    # still one; the source family decides the type.
    assert candidate.event_type is ScheduleEventType.VERTICAL_CORRIDOR


def test_an_examination_names_its_cohorts_with_hyphens() -> None:
    # `A-B-C-D SINAV` is the only place this source separates cohorts with a
    # hyphen, and it states both the audience and what kind of session it is.
    exam_slot = "*\n28 Mart 2026 Cumartesi\n9.00-16.30"
    response = parse(build(slot_rows=[[exam_slot, None, None, "A-B-C-D SINAV"]]))

    candidate = response.candidates[0]
    assert candidate.event_type is ScheduleEventType.EXAM
    assert [selector.value for selector in candidate.audience.selectors] == ["A", "B", "C", "D"]
    assert candidate.start_local_time == time(9, 0)


def test_a_hyphen_that_does_not_separate_cohorts_is_not_split() -> None:
    # `EK-1` is a separately published list, not the cohorts E, K and 1. The
    # rewrite only applies when every part is one of the eight declared letters.
    response = parse(build(slot_rows=[[FIRST_SLOT, None, None, "EK-1"]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.separatelyPublishedCohortList"] == 1


def test_the_english_programmes_cohorts_are_counted_not_published() -> None:
    # The document carries both programmes. Publishing İ1 under a source whose
    # context states the Turkish programme would reach the wrong students.
    response = parse(build(slot_rows=[[FIRST_SLOT, "İ1 grubu\n13.30-15.20", "i1+i2", None]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.cohortOfAnotherProgram"] == 2


def test_a_word_is_never_read_as_a_run_of_cohorts() -> None:
    # With runs of up to eight letters allowed, `Beceri` expands to B, E, C, E, R
    # and I — three of which are real groups. The eight-letter alphabet is what
    # refuses it.
    response = parse(build(slot_rows=[[FIRST_SLOT, "Beceri", None, None]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unsupportedGroupValueShape"] == 1


@pytest.mark.parametrize("value", ("UYGULAMA", "T"))
def test_a_cell_that_states_no_readable_audience_publishes_nothing(value: str) -> None:
    response = parse(build(slot_rows=[[FIRST_SLOT, value, None, None]]))

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
        build(slot_rows=[["2/7\n24 Aralık 2024 Çarşamba\n10:30-12:20", None, None, "E"]])
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
    response = parse(build(slot_rows=[[FIRST_SLOT, None, None, None]]))

    assert response.status is ParserResultStatus.COMPLETED
    assert response.candidates == []
    assert metrics(response)["rows.slot"] == 1
    assert "cells.scanned" not in metrics(response)


def test_a_row_that_states_groups_but_no_readable_slot_is_reported() -> None:
    # This row writes its whole slot on one line. Splitting it would mean
    # guessing where the date ends, so it is refused — loudly, because a session
    # with an audience is being lost.
    response = parse(
        build(slot_rows=[["20 Nisan 2026 Pazartesi 8.30-10.20", None, None, "AB"]])
    )

    assert response.candidates == []
    assert metrics(response)["slots.ignored.groupsStatedWithoutReadableSlot"] == 1
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert "20 Nisan 2026" in warning.message


def test_each_practice_publishes_its_own_room_and_a_deferred_room_is_not() -> None:
    response = parse(
        build(
            slot_rows=[
                [FIRST_SLOT, "A", None, None],
                [SECOND_SLOT, None, "B", None],
            ]
        )
    )

    by_title = {candidate.display_title: candidate for candidate in response.candidates}
    assert by_title["OKSİJEN"].location == "Sualtı Hekimliği"
    # `Amfi programına bakınız` points at the amphitheatre program, so it is not
    # published as a room (ADR-133).
    assert by_title["AYDINLATILMIŞ ONAM"].location is None
    assert metrics(response)[METRIC_PLACE_DEFERRED] == 1


def test_an_instructor_is_read_from_the_column_header() -> None:
    ekip_olma = "EKİP OLMA\nDoç. Dr. Ayşe Nilüfer ALÇALAR\nBeceri Laboratuvarı"
    response = parse(
        build(
            header=["Uygulama yeri", ekip_olma],
            slot_rows=[[FIRST_SLOT, "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.display_title == "EKİP OLMA"
    assert candidate.instructor == "Doç. Dr. Ayşe Nilüfer ALÇALAR"
    assert candidate.location == "Beceri Laboratuvarı"


def test_a_header_cell_that_names_only_a_practice_states_no_instructor_or_room() -> None:
    response = parse(
        build(
            header=["Uygulama yeri", "SH ÖYKÜ ALMA"],
            slot_rows=[[FIRST_SLOT, "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.display_title == "SH ÖYKÜ ALMA"
    assert candidate.instructor is None
    assert candidate.location is None


def test_a_full_width_banner_is_not_read_as_an_audience_for_every_practice() -> None:
    # The workbook's `TÜM GRUPLAR` divider is one cell merged across every column.
    # Reading its value for each practice column would refuse the row once per
    # practice and could publish it five times over; a group cell counts only when
    # its value is stored at the cell itself.
    banner = "TÜM GRUPLAR\n7.10.2026\nÇarşamba\n10:30-12:20"
    cells = [*row_cells(0, list(HEADER)), text_cell(1, 0, banner)]
    ws = worksheet(
        cells,
        column_count=4,
        merged_ranges=[
            {
                "startRowIndex": 1,
                "startColumnIndex": 0,
                "endRowIndexExclusive": 2,
                "endColumnIndexExclusive": 4,
            }
        ],
    )

    response = parse([ws])

    assert response.candidates == []
    assert "cells.scanned" not in metrics(response)


def test_the_dissection_rotation_is_deferred_to_the_anatomy_sources() -> None:
    response = parse(build(slot_rows=[["Anatomi (17)", None, None, None]]))

    assert response.candidates == []
    assert metrics(response)["rows.ignored.outOfScopeGroupRotation"] == 1


def test_every_row_of_the_worksheet_is_accounted_for() -> None:
    cells = [
        text_cell(0, 0, "Beceri uygulamaları tarihlerinde güncelleme yapıldığında"),
        *row_cells(1, list(HEADER)),
        *row_cells(2, [FIRST_SLOT, "A", None, None]),
        *row_cells(3, [SECOND_SLOT, None, None, None]),
        *row_cells(4, ["Anatomi (17)"]),
        # A topic line below the table, which is neither a header nor a slot.
        *row_cells(5, ["Uygulama konu başlıkları"]),
    ]

    response = parse([worksheet(cells, column_count=4)])
    values = metrics(response)

    assert values["rows.scanned"] == 6
    accounted = (
        values["rows.headerRow"]
        + values["rows.slot"]
        + values["rows.ignored"]
    )
    assert accounted == values["rows.scanned"]


def test_a_second_table_replaces_the_first_rather_than_ending_the_worksheet() -> None:
    # A header may reappear lower in the worksheet to open a fresh table.
    cells = [
        *row_cells(0, list(HEADER)),
        *row_cells(1, [FIRST_SLOT, "A", None, None]),
        *row_cells(2, ["Uygulama yeri", "SH ÖYKÜ ALMA\nDr. Öğr. Üyesi Hacer NALBANT\nSimülasyon"]),
        *row_cells(3, [SECOND_SLOT, "B", None, None]),
    ]

    response = parse([worksheet(cells, column_count=4)])

    assert [candidate.display_title for candidate in response.candidates] == [
        "OKSİJEN",
        "SH ÖYKÜ ALMA",
    ]
    assert metrics(response)["rows.headerRow"] == 2


def test_a_worksheet_without_a_header_row_is_rejected_not_silently_empty() -> None:
    response = parse([worksheet(row_cells(0, ["1/1", "A", None]))])

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_identity_separates_the_cohorts_a_session_is_for() -> None:
    response = parse(
        build(
            slot_rows=[
                [FIRST_SLOT, "A", None, None],
                [SECOND_SLOT, "B", None, None],
            ]
        )
    )

    first, second = response.candidates
    assert first.stable_identity != second.stable_identity
