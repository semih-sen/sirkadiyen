"""Unit tests for the rotation-matrix practice profile.

Each test builds one small block, because the rules that matter here decide
which students receive an event and a failure has to name the rule that broke.
"""

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
from sirkadiyen_parser.normalization.dates import (
    REASON_NUMERIC_ORDER_NOT_DECLARED,
    RULE_NUMERIC_DAY_FIRST,
    NumericDateOrder,
)
from sirkadiyen_parser.parsers import get_parser, implemented_profiles
from sirkadiyen_parser.parsers.annual import METRIC_DATE_RULE_PREFIX
from sirkadiyen_parser.parsers.practice import (
    DIMENSION_PRACTICE_GROUP,
    DIMENSION_PRACTICE_SUBGROUP,
    METRIC_BLOCKS_DETECTED,
    METRIC_NESTED_TABLES,
    WARNING_NESTED_TABLE,
    classify_event_type,
    parse_practice_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade1_practice_v1",
    "1.0.0",
    "practice",
    NumericDateOrder.UNDECLARED,
    ("practiceGroup", "practiceSubgroup"),
)

#: The same profile as if a real workbook had shown it writes ``03/10/2025``.
DAY_FIRST_PROFILE = ParserProfileDefinition(
    "grade1_practice_v1",
    "1.0.0",
    "practice",
    NumericDateOrder.DAY_FIRST,
    ("practiceGroup", "practiceSubgroup"),
)

#: 2025-10-03, as a spreadsheet date serial.
DATE_SERIAL = 45933


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def date_cell(row: int, column: int, serial: float) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "number", "numberValue": serial},
        "effectiveFormat": {"numberFormatType": "DATE"},
    }


def merged(start_row: int, start_column: int, end_row: int, end_column: int) -> dict[str, int]:
    return {
        "startRowIndex": start_row,
        "endRowIndexExclusive": end_row,
        "startColumnIndex": start_column,
        "endColumnIndexExclusive": end_column,
    }


def block(
    *,
    heading: str = "TIBBA MERHABA DİLİMİ",
    subjects: tuple[str, ...] = ("Tıbbi Biyoloji", "Biyofizik"),
    rows: tuple[tuple[float | str, str, tuple[str, ...]], ...] = (
        (DATE_SERIAL, "10:30-12:20", ("A", "B")),
    ),
    column_count: int = 6,
) -> dict[str, Any]:
    """Build a worksheet holding one rotation block."""
    cells: list[dict[str, Any]] = [text_cell(0, 0, heading)]
    cells.append(text_cell(1, 0, "Uygulama Tarihi"))
    cells.append(text_cell(1, 1, "Saat"))
    for offset, subject in enumerate(subjects):
        cells.append(text_cell(1, 2 + offset, subject))

    for index, (date_value, time_value, values) in enumerate(rows):
        row = 2 + index
        if isinstance(date_value, str):
            if date_value:
                cells.append(text_cell(row, 0, date_value))
        else:
            cells.append(date_cell(row, 0, date_value))
        if time_value:
            cells.append(text_cell(row, 1, time_value))
        for offset, value in enumerate(values):
            if value:
                cells.append(text_cell(row, 2 + offset, value))

    return {
        "sheetId": "1",
        "title": "Sayfa1",
        "index": 0,
        "rowCount": 2 + len(rows),
        "columnCount": column_count,
        "mergedRanges": [merged(0, 0, 1, column_count)],
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
                "academicYear": "2025-2026",
                "classYear": 1,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "TEST-PRACTICE",
                "snapshotId": "test-snapshot",
                "spreadsheetId": "test-spreadsheet",
                "acquiredAtUtc": "2026-07-21T09:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "SHA-256",
                "worksheets": worksheets,
            },
        }
    )
    return parse_practice_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def test_the_registered_profile_is_the_practice_implementation() -> None:
    profile = get_profile("grade1_practice_v1", "1.0.0")

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_practice_snapshot
    assert ("grade1_practice_v1", "1.0.0") in implemented_profiles()


def test_a_cell_becomes_a_candidate_for_the_group_it_names() -> None:
    response = parse([block()])

    assert response.status is ParserResultStatus.COMPLETED
    assert len(response.candidates) == 2
    first = response.candidates[0]
    assert first.display_title == "Tıbbi Biyoloji"
    assert first.local_date.isoformat() == "2025-10-03"
    assert first.start_local_time.isoformat() == "10:30:00"
    assert first.end_local_time.isoformat() == "12:20:00"
    assert first.audience.scope is AudienceScope.SELECTED_GROUPS
    assert [(s.dimension, s.value) for s in first.audience.selectors] == [
        (DIMENSION_PRACTICE_GROUP, "A")
    ]


def test_an_empty_cell_is_not_a_lesson() -> None:
    response = parse([block(rows=((DATE_SERIAL, "10:30-12:20", ("A", "")),))])

    assert len(response.candidates) == 1


def test_a_subgroup_value_uses_its_own_audience_dimension() -> None:
    response = parse([block(rows=((DATE_SERIAL, "10:30-12:20", ("C1", "")),))])

    selectors = response.candidates[0].audience.selectors
    assert [(s.dimension, s.value) for s in selectors] == [(DIMENSION_PRACTICE_SUBGROUP, "C1")]
    assert metrics(response)["audience.dimension.practiceSubgroup"] == 1


def test_a_letter_run_selects_both_groups() -> None:
    response = parse([block(rows=((DATE_SERIAL, "10:30-12:20", ("AB", "")),))])

    selectors = response.candidates[0].audience.selectors
    assert [s.value for s in selectors] == ["A", "B"]
    assert response.candidates[0].confidence < 1.0


def test_all_groups_becomes_a_program_wide_audience() -> None:
    response = parse(
        [block(rows=((DATE_SERIAL, "10:30-12:20", ("Tüm Gruplar (Amfide yapılacak)", "")),))]
    )

    candidate = response.candidates[0]
    assert candidate.audience.scope is AudienceScope.ALL_STUDENTS_IN_PROGRAM
    assert candidate.audience.selectors == []


def test_a_makeup_marker_is_refused_rather_than_sent_to_everyone() -> None:
    response = parse([block(rows=((DATE_SERIAL, "10:30-12:20", ("TELAFİ", "")),))])

    assert response.candidates == []
    assert metrics(response)["cells.ignored.unresolvedGroupExpression"] == 1
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS
    assert response.warnings[0].severity is ParserWarningSeverity.WARNING


def test_an_out_of_scope_subject_publishes_nothing_but_accounts_for_its_cells() -> None:
    """PDÖ is deliberately not synchronized (ADR-030).

    Its groups are arranged out of band, so a published PDÖ lesson would name a
    partition no student profile can express. The cells still have to be
    accounted for, so the column is reported and every populated cell is counted.
    """
    response = parse(
        [
            block(
                subjects=("PDÖ", "Biyofizik"),
                rows=((DATE_SERIAL, "10:30-12:20", ("A1", "B")),),
            )
        ]
    )

    assert [candidate.display_title for candidate in response.candidates] == ["Biyofizik"]
    assert metrics(response)["cells.ignored.outOfScopeSubject"] == 1
    assert metrics(response)["subjects.ignored.outOfScope"] == 1


def test_a_subject_is_out_of_scope_only_when_a_whole_word_matches() -> None:
    """The filter must not swallow a subject that merely contains the letters."""
    response = parse(
        [
            block(
                subjects=("Pdöner Kapak Uygulaması", "Biyofizik"),
                rows=((DATE_SERIAL, "10:30-12:20", ("A", "B")),),
            )
        ]
    )

    assert len(response.candidates) == 2
    assert "subjects.ignored.outOfScope" not in metrics(response)


def test_a_row_without_a_readable_date_publishes_none_of_its_cells() -> None:
    response = parse([block(rows=((("11 Mayıs Pazartesi"), "10:30-12:20", ("A", "B")),))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedDate"] == 1


def test_an_ambiguous_numeric_date_costs_the_whole_rotation_row() -> None:
    response = parse([block(rows=(("03/10/2025", "10:30-12:20", ("A", "B")),))])

    # A refused date row costs every group in it, which is the safe direction:
    # the alternative is sending two groups to a practice ten months early.
    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedDate"] == 1
    ignored = [warning for warning in response.warnings if warning.code == "rowsIgnored"]
    assert REASON_NUMERIC_ORDER_NOT_DECLARED in ignored[0].message


def test_the_declared_order_reaches_the_rotation_date() -> None:
    response = parse(
        [block(rows=(("03/10/2025", "10:30-12:20", ("A", "B")),))],
        profile=DAY_FIRST_PROFILE,
    )

    assert [candidate.local_date.isoformat() for candidate in response.candidates] == [
        "2025-10-03",
        "2025-10-03",
    ]
    # Counted once for the row, not once per group attending it.
    assert metrics(response)[f"{METRIC_DATE_RULE_PREFIX}{RULE_NUMERIC_DAY_FIRST}"] == 1


def test_a_row_without_a_readable_time_range_publishes_none_of_its_cells() -> None:
    response = parse([block(rows=((DATE_SERIAL, "09.30-11-20", ("A", "B")),))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedTimeRange"] == 1


def test_the_same_group_and_subject_twice_in_one_slot_is_published_once() -> None:
    duplicated = block(
        subjects=("Tıbbi Biyoloji", "Tıbbi Biyoloji"),
        rows=((DATE_SERIAL, "10:30-12:20", ("A", "A")),),
    )

    response = parse([duplicated])

    assert len(response.candidates) == 1
    assert metrics(response)["cells.ignored.duplicateStableIdentity"] == 1


def test_two_groups_in_one_slot_are_separate_lessons() -> None:
    response = parse([block(rows=((DATE_SERIAL, "10:30-12:20", ("A", "A")),))])

    identities = {candidate.stable_identity for candidate in response.candidates}
    assert len(identities) == 2


def test_the_subject_header_supplies_the_title_and_the_instructor() -> None:
    newline = chr(0x000A)
    header = f"Öğrenme{newline}(Prof.Dr. Zeynep SOLAKOĞLU)"

    response = parse([block(subjects=(header,), rows=((DATE_SERIAL, "10:30-12:20", ("A",)),))])

    candidate = response.candidates[0]
    assert candidate.display_title == "Öğrenme"
    assert candidate.instructor == "Prof.Dr. Zeynep SOLAKOĞLU"


def test_a_wrapped_subject_header_keeps_both_lines_of_its_title() -> None:
    newline = chr(0x000A)
    header = f"Kırık-Çıkıklarda{newline}İlkyardım"

    response = parse([block(subjects=(header,), rows=((DATE_SERIAL, "10:30-12:20", ("A",)),))])

    assert response.candidates[0].display_title == "Kırık-Çıkıklarda İlkyardım"


def test_the_totals_row_ends_a_block() -> None:
    worksheet = block(rows=((DATE_SERIAL, "10:30-12:20", ("A", "B")),))
    worksheet["rowCount"] = 5
    worksheet["cells"].append(text_cell(3, 0, "Uygulama Sayısı"))
    worksheet["cells"].append(text_cell(4, 0, "1.Konsantrasyon"))

    response = parse([worksheet])

    assert len(response.candidates) == 2
    assert metrics(response)["rows.scanned"] == 1


def test_a_nested_table_is_reported_and_its_columns_are_not_read() -> None:
    worksheet = block(subjects=("Fizyoloji", "Anatomi"), rows=(), column_count=6)
    worksheet["rowCount"] = 5
    worksheet["cells"].extend(
        [
            text_cell(2, 2, "Uygulama Tarihi"),
            text_cell(2, 3, "Saati"),
            date_cell(3, 0, DATE_SERIAL),
            text_cell(3, 1, "10:30-12:20"),
            text_cell(3, 2, "A"),
            date_cell(3, 3, DATE_SERIAL),
        ]
    )

    response = parse([worksheet])

    assert metrics(response)[METRIC_NESTED_TABLES] == 1
    assert metrics(response)[METRIC_BLOCKS_DETECTED] == 2
    assert WARNING_NESTED_TABLE in [warning.code for warning in response.warnings]
    assert response.candidates == []


def test_a_worksheet_without_a_block_header_is_rejected_not_reported_empty() -> None:
    response = parse(
        [
            {
                "sheetId": "1",
                "title": "Salon Bilgileri",
                "index": 0,
                "rowCount": 2,
                "columnCount": 2,
                "cells": [text_cell(0, 0, "Anatomi"), text_cell(0, 1, "Temel Bilimler")],
            }
        ]
    )

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


@pytest.mark.parametrize(
    ("subject", "heading", "expected"),
    (
        ("Tıbbi Biyoloji", "HÜCRE DİLİMİ", ScheduleEventType.PRACTICE),
        ("Nabız, Kb", "Dikey Koridor", ScheduleEventType.VERTICAL_CORRIDOR),
        ("Anatomi", "HAREKET DİLİMİ", ScheduleEventType.ANATOMY_PRACTICE),
        ("Diseksiyon", None, ScheduleEventType.ANATOMY_PRACTICE),
    ),
)
def test_event_type_classification(
    subject: str,
    heading: str | None,
    expected: ScheduleEventType,
) -> None:
    assert classify_event_type(subject=subject, block=heading) is expected
