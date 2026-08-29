"""Unit tests for the slot-column practice profile.

The golden test proves the profile against the real Grade 2 workbook. These
tests pin the individual rules with small, labelled worksheets, so a failure
names the rule that broke rather than pointing at a large diff.
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
from sirkadiyen_parser.parsers.practice_slots import (
    METRIC_CANDIDATES_SELF_DATED,
    METRIC_CELLS_MERGE_CONTINUATION,
    METRIC_PLACE_DEFERRED,
    METRIC_SUBJECTS_GROUP_ROTATION,
    METRIC_SUBJECTS_OUT_OF_SCOPE,
    WARNING_SLOT_REFUSED,
    parse_practice_slot_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade2_practice_v1",
    "1.3.0",
    "practice",
    NumericDateOrder.DAY_FIRST,
    ("practiceGroup",),
    group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
)

HEADING = "KAN LENFOİD 1"
SLOT_HEADER = ["Uygulama adı", "Uygulama yeri"]

#: One slot column, exactly as the workbook writes it.
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
    rows: list[dict[str, Any]],
    *,
    merged_ranges: list[dict[str, int]] | None = None,
    column_count: int = 4,
    title: str = "Sayfa1",
) -> dict[str, Any]:
    highest_row = max((cell["rowIndex"] for cell in rows), default=0)
    return {
        "sheetId": "1",
        "title": title,
        "index": 0,
        "rowCount": highest_row + 1,
        "columnCount": column_count,
        "mergedRanges": merged_ranges or [],
        "cells": rows,
    }


def merge(row: int, start_column: int, end_column_exclusive: int) -> dict[str, int]:
    return {
        "startRowIndex": row,
        "endRowIndexExclusive": row + 1,
        "startColumnIndex": start_column,
        "endColumnIndexExclusive": end_column_exclusive,
    }


def build(
    *,
    heading: str | None = HEADING,
    slots: list[str] | None = None,
    subject_rows: list[list[str | None]],
    column_count: int = 4,
) -> list[dict[str, Any]]:
    """Build the usual shape: a heading, a slot-header row, then subject rows."""
    cells: list[dict[str, Any]] = []
    merges: list[dict[str, int]] = []
    row = 0
    if heading is not None:
        cells.append(text_cell(row, 0, heading))
        merges.append(merge(row, 0, column_count))
        row += 1

    cells.extend(row_cells(row, [*SLOT_HEADER, *(slots if slots is not None else [FIRST_SLOT])]))
    row += 1
    for values in subject_rows:
        cells.extend(row_cells(row, values))
        row += 1

    return [worksheet(cells, merged_ranges=merges, column_count=column_count)]


def parse(
    worksheets: list[dict[str, Any]],
    *,
    class_year: int = 2,
    program_language: str = "turkish",
    profile: ParserProfileDefinition = PROFILE,
) -> ParseSnapshotResponse:
    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "unit-test",
            "parserProfile": {"name": profile.name, "version": profile.version},
            "sourceContext": {
                "academicYear": "2025-2026",
                "classYear": class_year,
                "programLanguage": program_language,
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "TEST-SOURCE",
                "snapshotId": "test-snapshot",
                "spreadsheetId": "test-spreadsheet",
                "acquiredAtUtc": "2026-07-25T09:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "SHA-256",
                "worksheets": worksheets,
            },
        }
    )
    return parse_practice_slot_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def test_the_registered_profile_is_the_slot_column_implementation() -> None:
    profile = get_profile(PROFILE.name, PROFILE.version)

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_practice_slot_snapshot
    assert (PROFILE.name, PROFILE.version) in implemented_profiles()
    # These tests parse through a profile they declare themselves, so what they
    # prove is only worth anything while it matches the registered one.
    assert profile == PROFILE


def test_a_group_cell_becomes_a_candidate_for_that_group() -> None:
    response = parse(
        build(subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", "A"]]),
    )

    assert response.status is ParserResultStatus.COMPLETED
    candidate = response.candidates[0]
    assert candidate.display_title == "Fizyoloji"
    assert candidate.local_date.isoformat() == "2025-09-08"
    assert candidate.start_local_time == time(8, 30)
    assert candidate.end_local_time == time(10, 20)
    assert candidate.location == "Fizyoloji Pratik salonu"
    assert candidate.curriculum_block == HEADING
    assert candidate.event_type is ScheduleEventType.PRACTICE
    assert candidate.audience.scope is AudienceScope.SELECTED_GROUPS
    assert [selector.value for selector in candidate.audience.selectors] == ["A"]
    assert [selector.dimension for selector in candidate.audience.selectors] == ["practiceGroup"]


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("i1", ["İ1"]),
        ("i2", ["İ2"]),
        ("i1+i2", ["İ1", "İ2"]),
    ],
)
def test_an_english_group_cell_uses_the_independent_english_practice_groups(
    value: str,
    expected: list[str],
) -> None:
    response = parse(
        build(subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", value]]),
        program_language="english",
    )

    assert [selector.value for selector in response.candidates[0].audience.selectors] == expected
    assert {selector.dimension for selector in response.candidates[0].audience.selectors} == {
        "practiceGroup"
    }


def test_an_english_group_token_never_enters_a_turkish_candidate() -> None:
    response = parse(
        build(subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", "i1"]]),
    )

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unsupportedGroupValueShape"] == 1


def test_an_english_group_not_declared_by_the_fixture_is_refused() -> None:
    response = parse(
        build(subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", "i3"]]),
        program_language="english",
    )

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unsupportedGroupValueShape"] == 1


def test_the_date_and_time_come_from_the_column_the_cell_sits_in() -> None:
    response = parse(
        build(
            slots=[FIRST_SLOT, SECOND_SLOT],
            subject_rows=[["Histoloji", "Histoloji Pratik salonu", "A", "B"]],
            column_count=4,
        )
    )

    assert [
        (candidate.local_date.isoformat(), candidate.start_local_time)
        for candidate in response.candidates
    ] == [("2025-09-08", time(8, 30)), ("2025-09-09", time(10, 30))]


def test_a_compact_day_and_month_are_separated_without_correcting_the_date() -> None:
    response = parse(
        build(
            slots=["23Aralık 2025 Salı\n10:30-12:20"],
            subject_rows=[["Histoloji", "Histoloji Pratik salonu", "A"]],
        )
    )

    assert response.candidates[0].local_date.isoformat() == "2025-12-23"


def test_a_date_and_time_on_one_line_are_read_from_their_stated_parts() -> None:
    response = parse(
        build(
            slots=["18 Mayıs 2026 Pazartesi 13:30-15:20"],
            subject_rows=[["Histoloji", "Histoloji Pratik salonu", "A"]],
        )
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2026-05-18"
    assert candidate.start_local_time == time(13, 30)
    assert candidate.end_local_time == time(15, 20)


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("F + B", ["F", "B"]),
        ("D+H", ["D", "H"]),
        ("ABCD 1/1", ["A", "B", "C", "D"]),
        ("GH 1/3", ["G", "H"]),
    ],
)
def test_a_cell_naming_several_groups_selects_all_of_them(
    value: str,
    expected: list[str],
) -> None:
    # The session number after the groups counts the session within the
    # subject's own series; it is not part of the audience.
    response = parse(build(subject_rows=[["Patoloji", "Patoloji Pratik salonu", value]]))

    assert [selector.value for selector in response.candidates[0].audience.selectors] == expected


def test_a_word_is_never_read_as_a_run_of_groups() -> None:
    # `SINAV` would expand to S, I, N, A and V under the letter-run rule, and one
    # of those is a real cohort. The cell is refused with its address instead.
    response = parse(build(subject_rows=[["Biyokimya", "Biyokimya Pratik salonu", "SINAV"]]))

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unsupportedGroupValueShape"] == 1
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS


@pytest.mark.parametrize(
    ("value", "reason"),
    [
        ("*", "cells.ignored.groupsAnnouncedElsewhere"),
        ("-", "cells.ignored.noSessionInSlot"),
        ("UYGULAMA TELAFİ", "cells.ignored.unresolvedGroupExpression"),
    ],
)
def test_a_cell_that_states_no_audience_publishes_nothing(value: str, reason: str) -> None:
    response = parse(build(subject_rows=[["Beceri", "Web Sitesinde Yayınlanacak", value]]))

    assert response.candidates == []
    assert metrics(response)[reason] == 1


def test_a_slot_whose_weekday_contradicts_its_date_is_refused_with_its_address() -> None:
    # The real workbook writes four dates whose year is a year out, and every one
    # of them disagrees with the weekday beside it. 2025-02-03 is a Monday.
    response = parse(
        build(
            slots=["1/3\n3 Şubat 2025 Salı\n08:30-10:20"],
            subject_rows=[["Histoloji", "Histoloji Pratik salonu", "B"]],
        )
    )

    assert response.candidates == []
    assert metrics(response)["slots.ignored.weekdayContradictsSlotDate"] == 1
    refusals = [warning for warning in response.warnings if warning.code == WARNING_SLOT_REFUSED]
    assert refusals[0].severity is ParserWarningSeverity.WARNING
    assert refusals[0].evidence is not None
    # The cell below it is accounted for, but does not raise the same alarm twice.
    assert metrics(response)["cells.ignored.cellInRefusedSlot"] == 1


def test_an_unreadable_slot_date_refuses_only_its_own_column() -> None:
    response = parse(
        build(
            slots=["1/1\n24 Eylü 2025 Çarşamba\n08:30-10:20", SECOND_SLOT],
            subject_rows=[["Histoloji", "Histoloji Pratik salonu", "A", "B"]],
        )
    )

    assert [candidate.local_date.isoformat() for candidate in response.candidates] == ["2025-09-09"]
    assert metrics(response)["slots.ignored.unresolvedSlotDate"] == 1


def test_a_cell_that_dates_itself_is_read_from_the_cell_not_its_column() -> None:
    # A whole-cohort session is written into the table with its own date and
    # time, and that date is not the one its column header states.
    response = parse(
        build(
            subject_rows=[
                ["Fizyoloji 2", "AMFİ", "TÜM GRUPLAR\n23 Ekim 2025 Perşembe\n08.30-10.20"]
            ],
        )
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2025-10-23"
    assert candidate.start_local_time == time(8, 30)
    assert candidate.audience.scope is AudienceScope.ALL_STUDENTS_IN_PROGRAM
    assert candidate.audience.selectors == []
    assert metrics(response)[METRIC_CANDIDATES_SELF_DATED] == 1


def test_a_numeric_date_is_read_day_first_as_the_profile_declares() -> None:
    # The workbook writes one date numerically: `8.10.2025`. It is 8 October
    # day-first and 10 August month-first, and the Grade 2 annual workbook dates
    # that same session 2025-10-08 as a spreadsheet serial, which is what the
    # declaration is read from (ADR-075).
    response = parse(
        build(subject_rows=[["Fizyoloji 1", "AMFİ", "TÜM GRUPLAR\n8.10.2025\n08:30-10:20"]]),
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2025-10-08"
    assert candidate.audience.scope is AudienceScope.ALL_STUDENTS_IN_PROGRAM
    assert metrics(response)["dates.rule.numericDayFirstDate"] == 1


def test_a_date_the_declared_order_cannot_explain_is_refused_not_flipped() -> None:
    # `13.10.2025` is 13 October day-first; `10.13.2025` is a real date only
    # month-first. Reading it the other way round would quietly undo the
    # declaration for whichever cells happen to contradict it, so the cell is
    # refused with its address instead.
    response = parse(
        build(subject_rows=[["Fizyoloji 1", "AMFİ", "TÜM GRUPLAR\n10.13.2025\n08:30-10:20"]]),
    )

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unresolvedSelfDatedCell"] == 1
    warning = next(
        warning
        for warning in response.warnings
        if warning.severity is ParserWarningSeverity.WARNING
    )
    assert "numericDateImpossibleUnderDeclaredOrder" in warning.message
    assert warning.evidence is not None


def test_a_session_number_is_never_completed_into_a_date() -> None:
    # A slot label such as `2/6` has the shape of a numeric date, and declaring
    # an order is exactly what would let one be read as 2 June. It states no
    # year, and this profile supplies none, so no reading is possible.
    response = parse(
        build(
            slots=["1/1\n2/6\n08:30-10:20"],
            subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", "A"]],
        )
    )

    assert response.candidates == []
    assert metrics(response)["slots.ignored.unresolvedSlotDate"] == 1


def test_a_self_dated_session_merged_across_columns_is_published_once() -> None:
    cells = [
        text_cell(0, 0, HEADING),
        *row_cells(1, [*SLOT_HEADER, FIRST_SLOT, SECOND_SLOT]),
        *row_cells(
            2,
            ["Fizyoloji 2", "AMFİ", "TÜM GRUPLAR\n23 Ekim 2025 Perşembe\n08.30-10.20", None],
        ),
    ]
    merges = [merge(0, 0, 4), merge(2, 2, 4)]

    response = parse([worksheet(cells, merged_ranges=merges)])

    assert len(response.candidates) == 1
    assert metrics(response)[METRIC_CELLS_MERGE_CONTINUATION] == 1


def test_a_subject_the_profile_defers_to_its_own_source_publishes_nothing() -> None:
    # The anatomy row states dissection dates rather than groups, and the anatomy
    # sources own that rotation (ADR-073).
    response = parse(build(subject_rows=[["Anatomi 13", "Anabilim Dalı", "45902"]]))

    assert response.candidates == []
    assert metrics(response)[METRIC_SUBJECTS_GROUP_ROTATION] == 1
    assert metrics(response)["cells.ignored.outOfScopeGroupRotation"] == 1


def test_pdo_is_out_of_scope_here_too() -> None:
    response = parse(build(subject_rows=[["PDÖ", "Web Sitesinde Yayınlanacak", "A"]]))

    assert response.candidates == []
    assert metrics(response)[METRIC_SUBJECTS_OUT_OF_SCOPE] == 1


def test_a_room_that_names_a_future_announcement_is_not_published_as_a_place() -> None:
    response = parse(
        build(subject_rows=[["Mikrobiyoloji", "Web Sitesinde Yayınlanacak", "A"]]),
    )

    assert response.candidates[0].location is None
    assert metrics(response)[METRIC_PLACE_DEFERRED] == 1


def test_a_practical_examination_is_reported_as_an_examination() -> None:
    response = parse(build(subject_rows=[["Biyokimya Sınav", "Biyokimya Pratik salonu", "H"]]))

    assert response.candidates[0].event_type is ScheduleEventType.EXAM


def test_a_topic_list_below_a_table_is_counted_and_not_read_as_a_lesson() -> None:
    cells = [
        text_cell(0, 0, HEADING),
        *row_cells(1, [*SLOT_HEADER, FIRST_SLOT]),
        *row_cells(2, ["Fizyoloji", "Fizyoloji Pratik salonu", "A"]),
        # The topic list: a heading in the first column, then its entries. One of
        # them is merged across the first two columns, which is how the real
        # workbook writes the longer ones.
        text_cell(3, 0, "Fizyoloji-1:"),
        text_cell(4, 0, "Hematokrit değerinin saptanması"),
        text_cell(5, 0, "İnsan Kanı (Giemsa)"),
    ]
    merges = [merge(0, 0, 4), merge(5, 0, 2)]

    response = parse([worksheet(cells, merged_ranges=merges)])

    assert [candidate.display_title for candidate in response.candidates] == ["Fizyoloji"]
    assert metrics(response)["rows.ignored.notAPracticeSubjectRow"] == 3
    # Every row of the worksheet is classified exactly once.
    assert metrics(response)["rows.scanned"] == 6
    assert (
        metrics(response)["rows.blockHeading"]
        + metrics(response)["rows.slotHeader"]
        + metrics(response)["rows.subject"]
        + metrics(response)["rows.ignored.notAPracticeSubjectRow"]
        == 6
    )


def test_a_long_merged_note_does_not_become_a_curriculum_block() -> None:
    note = (
        "Dönem 2 Dikey Koridor II Uygulamaları; Aydınlatılmış Onam Alma, Hastane "
        "Enfeksiyonlarının Kontrolü ve Simüle Hasta Uygulamalarından oluşmaktadır."
    )
    cells = [
        text_cell(0, 0, HEADING),
        text_cell(1, 0, note),
        *row_cells(2, [*SLOT_HEADER, FIRST_SLOT]),
        *row_cells(3, ["Fizyoloji", "Fizyoloji Pratik salonu", "A"]),
    ]
    merges = [merge(0, 0, 4), merge(1, 0, 4)]

    response = parse([worksheet(cells, merged_ranges=merges)])

    assert response.candidates[0].curriculum_block == HEADING


def test_a_subject_row_before_any_slot_header_is_reported() -> None:
    # Nothing in the worksheet can date such a row, and assuming it belongs to
    # the previous block's table would date it from another block.
    cells = [
        text_cell(0, 0, HEADING),
        *row_cells(1, ["Fizyoloji", "Fizyoloji Pratik salonu", "A"]),
        *row_cells(2, [*SLOT_HEADER, FIRST_SLOT]),
        *row_cells(3, ["Histoloji", "Histoloji Pratik salonu", "B"]),
    ]

    response = parse([worksheet(cells, merged_ranges=[merge(0, 0, 4)])])

    assert [candidate.display_title for candidate in response.candidates] == ["Histoloji"]
    assert metrics(response)["rows.ignored.subjectRowOutsideAnySlotTable"] == 1
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS


def test_a_worksheet_without_a_slot_header_is_rejected_not_silently_empty() -> None:
    response = parse([worksheet([text_cell(0, 0, "Uygulama Salon Bilgileri")])])

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_identity_separates_the_groups_a_session_is_for() -> None:
    response = parse(
        build(
            slots=[FIRST_SLOT, SECOND_SLOT],
            subject_rows=[
                ["Fizyoloji", "Fizyoloji Pratik salonu", "A", None],
                ["Histoloji", "Histoloji Pratik salonu", "B", None],
            ],
        )
    )

    identities = {candidate.stable_identity for candidate in response.candidates}
    assert len(identities) == 2


def test_a_room_change_updates_the_lesson_instead_of_replacing_it() -> None:
    first = parse(build(subject_rows=[["Fizyoloji", "Fizyoloji Pratik salonu", "A"]]))
    moved = parse(build(subject_rows=[["Fizyoloji", "AMFİ", "A"]]))

    assert moved.candidates[0].stable_identity == first.candidates[0].stable_identity
    assert moved.candidates[0].content_hash != first.candidates[0].content_hash
