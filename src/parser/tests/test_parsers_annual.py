"""Unit tests for the row-oriented annual profile.

The golden tests prove the profile against the real workbooks. These tests pin
the individual rules with small, labelled worksheets, so a failure names the
rule that broke rather than pointing at a large diff.
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
from sirkadiyen_parser.normalization.dates import (
    REASON_NUMERIC_ORDER_NOT_DECLARED,
    RULE_NUMERIC_DAY_FIRST,
    RULE_SERIAL,
    NumericDateOrder,
)
from sirkadiyen_parser.parsers import get_parser, implemented_profiles
from sirkadiyen_parser.parsers.annual import (
    METRIC_CANDIDATES_ALL_DAY,
    METRIC_DATE_RULE_PREFIX,
    METRIC_LOCATION_DEFERRED,
    METRIC_ROWS_HIDDEN,
    METRIC_ROWS_NON_TEACHING_BREAK,
    METRIC_ROWS_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER,
    METRIC_ROWS_OUT_OF_SCOPE_SUBJECT,
    METRIC_WORKSHEETS_IGNORED_NO_HEADER,
    WARNING_CONFLICTING_DUPLICATE,
    WARNING_IMPLAUSIBLE_DURATION,
    classify_event_type,
    parse_annual_snapshot,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade1_yearly_v1",
    "1.4.0",
    "annual",
    NumericDateOrder.UNDECLARED,
)

#: The same profile as if a real workbook had shown it writes ``01/10/2025``.
DAY_FIRST_PROFILE = ParserProfileDefinition(
    "grade1_yearly_v1",
    "1.4.0",
    "annual",
    NumericDateOrder.DAY_FIRST,
)

TURKISH_HEADERS = [
    "Dönem",
    "TARİH",
    "Başlama Saati",
    "Bitiş Saati",
    "KONU",
    "DİLİM ADI / ANABİLİM DALI",
    "YER",
]
ENGLISH_HEADERS = [
    "Dönem",
    "Start Date",
    "Start Time",
    "End Time",
    "Subject",
    "Description",
    "Location",
]

#: 2025-10-01, as a spreadsheet date serial.
DATE_SERIAL = 45931


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def typed_cell(row: int, column: int, value: float, number_format: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "number", "numberValue": value},
        "effectiveFormat": {"numberFormatType": number_format},
    }


def lesson_row(
    row: int,
    *,
    term: str = "Dönem 1",
    date_serial: float | None = DATE_SERIAL,
    date_text: str | None = None,
    start: float | str | None = 0.375,
    end: float | str | None = 0.40625,
    title: str | None = "1-Hücre zarı / Prof.Dr. Ayşe DEMİR",
    block: str | None = "HÜCRE DİLİMİ / TIBBİ BİYOLOJİ AD.",
    location: str | None = "AZİZ SANCAR AMFİSİ",
) -> list[dict[str, Any]]:
    """Build one data row. ``None`` leaves a column empty.

    ``date_text`` replaces the typed date cell with written text, which is how a
    numeric date reaches the profile.
    """
    cells: list[dict[str, Any]] = []
    if term is not None:
        cells.append(text_cell(row, 0, term))
    if date_text is not None:
        cells.append(text_cell(row, 1, date_text))
    elif date_serial is not None:
        cells.append(typed_cell(row, 1, date_serial, "DATE"))
    for column, value in ((2, start), (3, end)):
        if isinstance(value, str):
            cells.append(text_cell(row, column, value))
        elif value is not None:
            cells.append(typed_cell(row, column, value, "TIME"))
    for column, value in ((4, title), (5, block), (6, location)):
        if value is not None:
            cells.append(text_cell(row, column, value))
    return cells


def worksheet(
    rows: list[dict[str, Any]],
    *,
    headers: list[str] | None = None,
    header_row: int = 0,
    title: str = "DÖNEM 1",
    row_count: int | None = None,
    hidden_rows: list[dict[str, int]] | None = None,
) -> dict[str, Any]:
    cells = [
        text_cell(header_row, column, header)
        for column, header in enumerate(headers if headers is not None else TURKISH_HEADERS)
    ]
    cells.extend(rows)
    highest_row = max((cell["rowIndex"] for cell in cells), default=header_row)
    return {
        "sheetId": "1",
        "title": title,
        "index": 0,
        "rowCount": row_count if row_count is not None else highest_row + 1,
        "columnCount": 7,
        "hiddenRows": hidden_rows or [],
        "cells": cells,
    }


def parse(
    worksheets: list[dict[str, Any]],
    *,
    class_year: int = 1,
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
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": {
                "contractVersion": "1.0",
                "sourceId": "TEST-SOURCE",
                "snapshotId": "test-snapshot",
                "spreadsheetId": "test-spreadsheet",
                "acquiredAtUtc": "2026-07-21T09:00:00Z",
                "contentHash": "sha256:test",
                "contentHashAlgorithm": "SHA-256",
                "worksheets": worksheets,
            },
        }
    )
    return parse_annual_snapshot(request, profile)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def test_the_registered_profile_is_the_annual_implementation() -> None:
    profile = get_profile("grade1_yearly_v1", "1.4.0")

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_annual_snapshot
    assert ("grade1_yearly_v1", "1.4.0") in implemented_profiles()


def test_a_lesson_row_becomes_a_candidate() -> None:
    response = parse([worksheet(lesson_row(1))])

    assert response.status is ParserResultStatus.COMPLETED
    candidate = response.candidates[0]
    assert candidate.display_title == "1-Hücre zarı"
    assert candidate.instructor == "Prof.Dr. Ayşe DEMİR"
    assert candidate.local_date.isoformat() == "2025-10-01"
    assert candidate.start_local_time == time(9, 0)
    assert candidate.end_local_time == time(9, 45)
    assert candidate.time_zone_id == "Europe/Istanbul"
    assert candidate.academic_year == "2025-2026"
    assert candidate.audience.scope is AudienceScope.ALL_STUDENTS_IN_PROGRAM
    assert candidate.normalized_course_identity == "hucre-zari"


@pytest.mark.parametrize(
    "title",
    ["UYGULAMA (PDÖ D3)", "LABORATORY SKILLS (BIOPHYSICS 5) İ1/ PBL"],
)
def test_pdo_pbl_rows_are_excluded_from_the_whole_class_program(title: str) -> None:
    # PDÖ/PBL problem-based learning is group-specific and published by the
    # practice source (ADR-030). An annual row naming it must not be shown to the
    # whole class, where it would overlap the parallel lecture the cohort attends.
    response = parse([worksheet(lesson_row(1, title=title))])

    assert response.candidates == []
    assert metrics(response)[METRIC_ROWS_OUT_OF_SCOPE_SUBJECT] == 1


@pytest.mark.parametrize("title", ["UYGULAMA", "PRACTICE"])
def test_generic_practice_placeholder_is_excluded_from_the_annual_program(
    title: str,
) -> None:
    response = parse([worksheet(lesson_row(1, title=title))])

    assert response.candidates == []
    assert metrics(response)[METRIC_ROWS_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER] == 1


@pytest.mark.parametrize(
    "title",
    [
        "Anatomi Uygulama 14 / 21",
        "FİZYOLOJİ UYGULAMA",
        "Psikolojinin Uygulama Alanları",
        "LABORATORY SKILLS (BIOPHYSICS 1)",
    ],
)
def test_named_practice_and_titles_containing_practice_words_are_retained(
    title: str,
) -> None:
    response = parse([worksheet(lesson_row(1, title=title))])

    assert [candidate.display_title for candidate in response.candidates] == [title]
    assert METRIC_ROWS_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER not in metrics(response)


def test_a_lunch_break_is_excluded_but_free_study_is_kept() -> None:
    # A lunch/interval break is not a lesson. Free study is a real whole-class
    # entry and is deliberately kept, so it can still be published (ADR-067).
    response = parse(
        [worksheet(lesson_row(1, title="ÖĞLE ARASI") + lesson_row(2, title="SERBEST ÇALIŞMA"))]
    )

    assert [candidate.display_title for candidate in response.candidates] == ["SERBEST ÇALIŞMA"]
    assert metrics(response)[METRIC_ROWS_NON_TEACHING_BREAK] == 1


def test_english_headers_select_the_same_columns() -> None:
    response = parse([worksheet(lesson_row(1), headers=ENGLISH_HEADERS, title="CLASS 1")])

    assert len(response.candidates) == 1


def test_a_worksheet_without_a_header_row_is_reported_not_silently_skipped() -> None:
    lookup = {
        "sheetId": "2",
        "title": "UYGULAMA YERLERİ",
        "index": 1,
        "rowCount": 2,
        "columnCount": 2,
        "cells": [text_cell(0, 0, "Anatomi"), text_cell(0, 1, "Temel Bilimler")],
    }

    response = parse([worksheet(lesson_row(1)), lookup])

    assert len(response.candidates) == 1
    assert metrics(response)[METRIC_WORKSHEETS_IGNORED_NO_HEADER] == 1


def test_a_snapshot_without_any_header_row_is_rejected() -> None:
    response = parse(
        [
            {
                "sheetId": "1",
                "title": "Notes",
                "index": 0,
                "rowCount": 1,
                "columnCount": 2,
                "cells": [text_cell(0, 0, "Anatomi")],
            }
        ]
    )

    assert response.status is ParserResultStatus.REJECTED
    assert response.candidates == []


def test_rows_belonging_to_another_class_year_are_excluded() -> None:
    response = parse([worksheet([*lesson_row(1), *lesson_row(2, term="Dönem 2")])])

    assert len(response.candidates) == 1
    assert metrics(response)["rows.ignored.otherClassYear"] == 1


def test_a_term_cell_without_a_usable_class_year_warns() -> None:
    response = parse([worksheet(lesson_row(1, term="46100"))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedTerm"] == 1
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS


def test_a_date_written_as_a_bare_number_is_not_read_as_a_serial() -> None:
    rows = lesson_row(1, date_serial=None)
    rows.append(
        {
            "rowIndex": 1,
            "columnIndex": 1,
            "a1Address": "B2",
            "effectiveValue": {"kind": "number", "numberValue": DATE_SERIAL},
            "effectiveFormat": {"numberFormatType": "NUMBER"},
        }
    )

    response = parse([worksheet(rows)])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedDate"] == 1


def test_an_ambiguous_numeric_date_is_refused_while_the_profile_declares_no_order() -> None:
    response = parse([worksheet(lesson_row(1, date_text="01/10/2025"))])

    # 01/10 is 1 October read day-first and 10 January read month-first, a
    # ten-month error nobody would see in a published calendar.
    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedDate"] == 1
    ignored = [warning for warning in response.warnings if warning.code == "rowsIgnored"]
    assert ignored[0].severity is ParserWarningSeverity.WARNING
    assert REASON_NUMERIC_ORDER_NOT_DECLARED in ignored[0].message
    assert ignored[0].evidence is not None
    assert ignored[0].evidence.raw_text == "01/10/2025"


def test_the_same_numeric_date_is_published_once_the_profile_declares_its_order() -> None:
    response = parse(
        [worksheet(lesson_row(1, date_text="01/10/2025"))],
        profile=DAY_FIRST_PROFILE,
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2025-10-01"
    assert metrics(response)[f"{METRIC_DATE_RULE_PREFIX}{RULE_NUMERIC_DAY_FIRST}"] == 1


def test_the_rule_each_published_date_was_read_under_is_counted() -> None:
    response = parse([worksheet(lesson_row(1) + lesson_row(2, title="2-Hücre iskeleti"))])

    assert metrics(response)[f"{METRIC_DATE_RULE_PREFIX}{RULE_SERIAL}"] == 2


def test_a_date_serial_in_the_time_column_is_refused_rather_than_read_as_midnight() -> None:
    rows = lesson_row(1, start=None)
    rows.append(
        {
            "rowIndex": 1,
            "columnIndex": 2,
            "a1Address": "C2",
            "effectiveValue": {"kind": "number", "numberValue": 45940},
            "effectiveFormat": {"numberFormatType": "DATE"},
        }
    )

    response = parse([worksheet(rows)])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.unresolvedStartTime"] == 1
    ignored = [warning for warning in response.warnings if warning.code == "rowsIgnored"]
    assert ignored[0].severity is ParserWarningSeverity.WARNING


def test_times_written_with_a_dot_are_read() -> None:
    response = parse([worksheet(lesson_row(1, start="09.30", end="10.20"))])

    candidate = response.candidates[0]
    assert candidate.start_local_time == time(9, 30)
    assert candidate.end_local_time == time(10, 20)


@pytest.mark.parametrize(
    "title",
    [
        "KURBAN BAYRAMI",
        "YARIYIL TATİL",
        "YILBAŞI TATİLİ",
        "LABOR DAY",
        "ULUSAL EGEMENLİK VE ÇOCUK BAYRAMI",
    ],
)
def test_a_dated_row_naming_a_closure_without_times_becomes_an_all_day_item(title: str) -> None:
    response = parse([worksheet(lesson_row(1, start=None, end=None, title=title))])

    candidate = response.candidates[0]
    assert candidate.is_all_day is True
    assert candidate.start_local_time is None
    assert candidate.end_local_time is None
    assert candidate.local_date.isoformat() == "2025-10-01"
    assert candidate.display_title == title
    # A closure is not teaching, so its type follows its shape and not the
    # keywords a classifier would find in the title.
    assert candidate.event_type is ScheduleEventType.OTHER
    assert response.status is ParserResultStatus.COMPLETED
    assert metrics(response)[METRIC_CANDIDATES_ALL_DAY] == 1


def test_an_all_day_item_reports_that_a_title_rule_shaped_it() -> None:
    response = parse([worksheet(lesson_row(1, start=None, end=None, title="KURBAN BAYRAMI"))])

    indicator = next(
        indicator for indicator in response.confidence_indicators if indicator.field == "isAllDay"
    )
    assert indicator.score < 1.0
    assert indicator.candidate_id == "1!R2"
    assert response.candidates[0].confidence == indicator.score


def test_a_dated_row_without_times_that_names_no_closure_is_still_refused() -> None:
    # The safe direction: a lesson whose times the faculty forgot must not become
    # an all-day block on every student's calendar.
    response = parse([worksheet(lesson_row(1, start=None, end=None, title="Hücre zarı"))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.noScheduledTimeAndNoClosure"] == 1
    ignored = [warning for warning in response.warnings if warning.code == "rowsIgnored"]
    assert ignored[0].severity is ParserWarningSeverity.WARNING
    assert "Hücre zarı" in ignored[0].message


def test_a_closure_title_on_a_timed_row_stays_a_timed_lesson() -> None:
    # The sources do this: the eve of Republic Day is three real hours of
    # teaching, and the English workbook writes its semester break with times.
    response = parse(
        [worksheet(lesson_row(1, title="CUMHURİYET BAYRAMI AREFESİ", start=0.5625, end=0.6805))]
    )

    candidate = response.candidates[0]
    assert candidate.is_all_day is False
    assert candidate.start_local_time is not None
    assert candidate.event_type is ScheduleEventType.THEORY


def test_an_all_day_item_and_a_lesson_on_one_date_are_different_lessons() -> None:
    response = parse(
        [
            worksheet(
                lesson_row(1, title="KURBAN BAYRAMI", start=None, end=None)
                + lesson_row(2, title="KURBAN BAYRAMI")
            )
        ]
    )

    assert len({candidate.stable_identity for candidate in response.candidates}) == 2


def test_an_end_time_that_does_not_follow_the_start_is_refused() -> None:
    response = parse([worksheet(lesson_row(1, start="10.30", end="09.30"))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.endTimeNotAfterStartTime"] == 1


def test_a_row_without_a_title_is_reported() -> None:
    response = parse([worksheet(lesson_row(1, title=None, block=None, location=None))])

    assert response.candidates == []
    assert metrics(response)["rows.ignored.missingTitle"] == 1


def test_a_completely_empty_row_is_counted_but_not_alarming() -> None:
    rows = [*lesson_row(1), *lesson_row(3, start=0.5, end=0.53125)]
    response = parse([worksheet(rows)])

    assert len(response.candidates) == 2
    assert metrics(response)["rows.ignored.blankRow"] == 1
    assert response.status is ParserResultStatus.COMPLETED


def test_a_repeated_identical_row_is_published_once() -> None:
    response = parse([worksheet([*lesson_row(1), *lesson_row(2)])])

    assert len(response.candidates) == 1
    assert metrics(response)["rows.ignored.duplicateStableIdentity"] == 1
    assert response.status is ParserResultStatus.COMPLETED


def test_two_rows_that_disagree_about_one_lesson_are_reported() -> None:
    response = parse([worksheet([*lesson_row(1), *lesson_row(2, end=0.5)])])

    assert len(response.candidates) == 1
    assert response.candidates[0].end_local_time == time(9, 45)
    codes = [warning.code for warning in response.warnings]
    assert WARNING_CONFLICTING_DUPLICATE in codes
    assert response.status is ParserResultStatus.COMPLETED_WITH_WARNINGS


def test_identity_ignores_room_and_instructor_but_content_does_not() -> None:
    first = parse([worksheet(lesson_row(1))]).candidates[0]
    rerouted = lesson_row(1, location="TEMEL BİLİMLER", title="1-Hücre zarı / Prof.Dr. Can YZ")
    moved = parse([worksheet(rerouted)]).candidates[0]

    assert moved.stable_identity == first.stable_identity
    assert moved.content_hash != first.content_hash


def test_the_block_cell_publishes_a_curriculum_block_and_a_department() -> None:
    candidate = parse([worksheet(lesson_row(1))]).candidates[0]

    assert candidate.curriculum_block == "HÜCRE DİLİMİ"
    assert candidate.departments == ["TIBBİ BİYOLOJİ AD."]


def test_an_integrated_session_publishes_every_department_it_names() -> None:
    row = lesson_row(1, block="HÜCRE DİLİMİ / BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD.")
    response = parse([worksheet(row)])

    assert response.candidates[0].departments == ["BİYOFİZİK AD.", "TIBBİ BİYOLOJİ AD."]
    assert metrics(response)["departments.integratedSession"] == 1.0


def test_a_block_segment_that_names_no_department_is_reported_once() -> None:
    rows = [
        *lesson_row(1, block="DOKU DİLİMİ / DİKEY KORİDOR"),
        *lesson_row(2, date_serial=DATE_SERIAL + 1, block="DOKU DİLİMİ / DİKEY KORİDOR"),
    ]
    response = parse([worksheet(rows)])

    assert [candidate.departments for candidate in response.candidates] == [[], []]
    assert metrics(response)["departments.ignored.unmarkedSegment"] == 2.0

    # Two rows, one note: the same wording repeats on dozens of real rows, and a
    # note per row would bury the finding it is meant to surface.
    notes = [
        warning for warning in response.warnings if warning.code == "unmarkedBlockDepartmentSegment"
    ]
    assert len(notes) == 1
    assert notes[0].severity is ParserWarningSeverity.INFORMATION
    assert notes[0].evidence is not None


def test_a_corrected_department_updates_the_lesson_instead_of_replacing_it() -> None:
    """Departments are content, not identity: a fix must not orphan the event."""
    first = parse([worksheet(lesson_row(1))]).candidates[0]
    corrected = parse(
        [worksheet(lesson_row(1, block="HÜCRE DİLİMİ / TIBBİ BİYOKİMYA AD."))]
    ).candidates[0]

    assert corrected.stable_identity == first.stable_identity
    assert corrected.content_hash != first.content_hash


def test_identity_changes_when_the_lesson_moves_to_another_day() -> None:
    first = parse([worksheet(lesson_row(1))]).candidates[0]
    later = parse([worksheet(lesson_row(1, date_serial=DATE_SERIAL + 1))]).candidates[0]

    assert later.stable_identity != first.stable_identity


def test_a_hidden_row_is_still_parsed_but_counted() -> None:
    response = parse(
        [worksheet(lesson_row(1), hidden_rows=[{"startIndex": 1, "endIndexExclusive": 2}])]
    )

    assert len(response.candidates) == 1
    assert metrics(response)[METRIC_ROWS_HIDDEN] == 1


def test_a_location_that_points_at_another_program_is_omitted_and_counted() -> None:
    deferred = "FAKÜLTEMİZ WEB SİTESİ ÖĞRENCİ AĞI AMFİ PROGRAMINA BAKINIZ"

    response = parse([worksheet(lesson_row(1, location=deferred))])

    assert response.candidates[0].location is None
    assert metrics(response)[METRIC_LOCATION_DEFERRED] == 1


def test_an_implausible_duration_is_published_with_a_warning() -> None:
    response = parse([worksheet(lesson_row(1, start=0.0, end=0.99))])

    assert len(response.candidates) == 1
    assert [warning.code for warning in response.warnings] == [WARNING_IMPLAUSIBLE_DURATION]


def test_a_weekday_that_contradicts_its_date_is_reported() -> None:
    response = parse([worksheet(lesson_row(1, date_serial=None, title="Ders"))])
    assert response.candidates == []

    response = parse(
        [
            worksheet(
                [
                    *lesson_row(1, date_serial=None, title="Ders"),
                    text_cell(1, 1, "1 Ekim 2025 Pazartesi"),
                ]
            )
        ]
    )

    candidate = response.candidates[0]
    assert candidate.local_date.isoformat() == "2025-10-01"
    assert [warning.code for warning in response.warnings] == ["weekdayMismatch"]


def test_evidence_cites_every_column_the_candidate_used() -> None:
    response = parse([worksheet(lesson_row(1))])

    rules = [evidence.extraction_rule for evidence in response.candidates[0].evidence]
    assert rules == [
        "annual.dateCell",
        "annual.startTimeCell",
        "annual.endTimeCell",
        "annual.titleCell",
        "annual.blockCell",
        "annual.locationCell",
    ]


@pytest.mark.parametrize(
    ("title", "block", "expected"),
    (
        ("SERBEST ÇALIŞMA (Dönem 2 Sınav)", None, ScheduleEventType.FREE_STUDY),
        ("ÖĞLE ARASI", None, ScheduleEventType.OTHER),
        ("FREE TIME", None, ScheduleEventType.FREE_STUDY),
        ("ANATOMİ UYGULAMA SINAVI", "HAREKET-1 DİLİMİ", ScheduleEventType.EXAM),
        ("EXAMINATION", "MOVEMENT", ScheduleEventType.EXAM),
        ("Diseksiyon", "HAREKET-1 DİLİMİ / ANATOMİ AD.", ScheduleEventType.ANATOMY_PRACTICE),
        ("ENTEGRE OTURUM - İnsanı tanımak", None, ScheduleEventType.INTEGRATED_SESSION),
        ("UYGULAMA", "TIBBA MERHABA DİLİMİ", ScheduleEventType.PRACTICE),
        ("Anatomi Uygulama 14 / 21", "HAREKET-1 DİLİMİ", ScheduleEventType.PRACTICE),
        (
            "UYGULAMA",
            "YAŞAMIN MOLEKÜLER TEMELLERİ / DİKEY KORİDOR",
            ScheduleEventType.VERTICAL_CORRIDOR,
        ),
        (
            "1-İletişim becerileri",
            "DİKEY KORİDOR DİLİMİ / RUH SAĞLIĞI AD.",
            ScheduleEventType.THEORY,
        ),
        ("25-Baş-boyun kasları I", "HAREKET-1 DİLİMİ / ANATOMİ AD.", ScheduleEventType.THEORY),
    ),
)
def test_event_type_classification(
    title: str,
    block: str | None,
    expected: ScheduleEventType,
) -> None:
    assert classify_event_type(title=title, block=block) is expected
