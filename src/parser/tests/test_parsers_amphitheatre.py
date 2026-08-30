"""Unit tests for the weekly amphitheatre reader and its room lookup.

The profile publishes nothing, so what these tests pin is the reading: how a day
block is recognized in a worksheet that also holds debris from earlier weeks, how
a cell's own time overrules the row it sits in, and — most importantly — when the
lookup refuses to name a room. A wrong room is written onto a student's calendar
event, so the refusals matter more here than the matches.
"""

from datetime import date, time

import pytest

from sirkadiyen_parser.contracts.parsing import (
    ParserResultStatus,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ProgramLanguage,
)
from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers import get_parser
from sirkadiyen_parser.parsers.amphitheatre import (
    REASON_AMBIGUOUS,
    REASON_NO_ASSIGNMENT,
    REASON_UNANIMOUS_WITHOUT_DEPARTMENT,
    RULE_ASSIGNMENT,
    AmphitheatreDocument,
    AmphitheatreIndex,
    read_amphitheatre_document,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile
from tests.support.golden import load_fixture_json
from tests.support.snapshots import merged_range, number_cell, text_cell, worksheet

PROFILE = ParserProfileDefinition(
    "weekly_amphitheatre_v1",
    "1.0.0",
    "amphitheatre",
    NumericDateOrder.UNDECLARED,
)

MONDAY = date(2026, 8, 31)
TUESDAY = date(2026, 9, 1)


def snapshot(*worksheets) -> NormalizedSpreadsheetSnapshot:
    """Wrap worksheets in the snapshot envelope the parser is handed."""
    return NormalizedSpreadsheetSnapshot.model_validate(
        {
            "contractVersion": "1.0",
            "sourceId": "SHARED-AMPHI",
            "snapshotId": "snapshot-1",
            "spreadsheetId": "spreadsheet-1",
            "acquiredAtUtc": "2026-08-30T00:00:00Z",
            "contentHash": "hash",
            "contentHashAlgorithm": "sha256",
            "worksheets": [sheet.model_dump(by_alias=True) for sheet in worksheets],
        }
    )


def day_block(
    first_row: int,
    title: str,
    rooms: dict[int, str],
    slots: list[tuple[str, dict[int, str]]],
) -> list:
    """Build one day's title row, room header row and slot rows."""
    cells = [text_cell(first_row, 0, title), text_cell(first_row + 1, 0, "SAAT")]
    cells += [text_cell(first_row + 1, column, name) for column, name in rooms.items()]
    for offset, (slot, entries) in enumerate(slots):
        row = first_row + 2 + offset
        cells.append(text_cell(row, 0, slot))
        cells += [text_cell(row, column, value) for column, value in entries.items()]
    return cells


def test_a_day_block_is_read_from_its_title_and_room_header() -> None:
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                day_block(
                    0,
                    "31 AĞUSTOS 2026 / Pazartesi",
                    {1: "KEMAL ATAY AMFİSİ"},
                    [("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE -KAN LENFOİD DİLİMİ -FİZYOLOJİ"})],
                )
            )
        )
    )

    assert len(document.assignments) == 1
    assignment = document.assignments[0]
    assert assignment.local_date == MONDAY
    assert assignment.start_local_time == time(8, 30)
    assert assignment.end_local_time == time(9, 10)
    assert assignment.room == "KEMAL ATAY AMFİSİ"
    assert assignment.class_year == 2
    assert assignment.program_language is ProgramLanguage.TURKISH
    assert assignment.curriculum_block == "KAN LENFOİD DİLİMİ"
    assert assignment.department == "FİZYOLOJİ"


def test_a_title_with_no_room_header_below_it_is_not_a_day() -> None:
    """The committed workbook strands old day titles in far columns.

    They carry no header row and no data. Recognizing a day title on its own
    would read `16 Eylül 2025 / Salı` as a real day of a 2026-2027 week.
    """
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                [
                    text_cell(0, 28, "16 Eylül 2025 / Salı"),
                    *day_block(
                        4,
                        "31 AĞUSTOS 2026 / Pazartesi",
                        {1: "KEMAL ATAY AMFİSİ"},
                        [("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ"})],
                    ),
                ]
            )
        )
    )

    assert document.dates() == (MONDAY,)


def test_a_day_title_written_as_a_date_serial_is_read() -> None:
    """One block writes its date as a value rather than as text."""
    cells = [
        number_cell(0, 8, 46271.0, number_format_type="DATE"),
        text_cell(1, 0, "SAAT"),
        text_cell(1, 1, "KEMAL ATAY AMFİSİ"),
        text_cell(2, 0, "08.30 - 09.10"),
        text_cell(2, 1, "DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ"),
    ]
    document = read_amphitheatre_document(snapshot(worksheet(cells)))

    assert document.dates() == (date(2026, 9, 6),)


def test_room_columns_are_read_per_block_not_once_per_worksheet() -> None:
    """Friday renames the columns every other day calls something else."""
    cells = day_block(
        0,
        "31 AĞUSTOS 2026 / Pazartesi",
        {1: "ESKİ FİZİK TEDAVİ ANABİLİM DALI A DERSLİĞİ"},
        [("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ"})],
    ) + day_block(
        4,
        "1 EYLÜL 2026 / Salı",
        {1: "FİZİK TEDAVİ YÜKSEK OKULU A AMFİSİ"},
        [("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ"})],
    )
    document = read_amphitheatre_document(snapshot(worksheet(cells)))

    rooms = {assignment.local_date: assignment.room for assignment in document.assignments}
    assert rooms[MONDAY] == "ESKİ FİZİK TEDAVİ ANABİLİM DALI A DERSLİĞİ"
    assert rooms[TUESDAY] == "FİZİK TEDAVİ YÜKSEK OKULU A AMFİSİ"


def test_a_vertical_merge_runs_to_the_end_of_the_last_slot_it_covers() -> None:
    cells = day_block(
        0,
        "31 AĞUSTOS 2026 / Pazartesi",
        {1: "KEMAL ATAY AMFİSİ"},
        [
            ("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ"}),
            ("09.20 - 10.00", {}),
            ("10.10 - 10.50", {}),
        ],
    )
    document = read_amphitheatre_document(
        snapshot(worksheet(cells, merged_ranges=[merged_range(2, 1, 5, 2)]))
    )

    assert len(document.assignments) == 1
    assert document.assignments[0].start_local_time == time(8, 30)
    assert document.assignments[0].end_local_time == time(10, 50)


@pytest.mark.parametrize(
    ("text", "start", "end"),
    [
        # A stated range replaces both ends of the slot.
        ("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ-13.00-15.20", time(13, 0), time(15, 20)),
        # A single trailing time replaces only the start: that is all it asserts.
        ("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ-08.45", time(8, 45), time(9, 10)),
        # A group label that looks numeric is not a time.
        ("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ A2-2", time(8, 30), time(9, 10)),
    ],
)
def test_a_cell_states_its_own_time_over_the_slot_row(
    text: str,
    start: time,
    end: time,
) -> None:
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                day_block(
                    0,
                    "31 AĞUSTOS 2026 / Pazartesi",
                    {1: "KEMAL ATAY AMFİSİ"},
                    [("08.30 - 09.10", {1: text})],
                )
            )
        )
    )

    assert (document.assignments[0].start_local_time, document.assignments[0].end_local_time) == (
        start,
        end,
    )


@pytest.mark.parametrize(
    "text",
    [
        "DÖNEM 3-TÜRKÇE-A GRUBU -SEMİYOLOJİ -İÇ HASTALIKLARI",
        "DÖNEM 3- A GRUBU- TÜRKÇE -SEMİYOLOJİ -İÇ HASTALIKLARI",
        "DÖNEM  - 3- TÜRKÇE - A GRUBU -SEMİYOLOJİ -İÇ HASTALIKLARI",
    ],
)
def test_the_audience_is_read_whatever_order_the_cell_writes_it_in(text: str) -> None:
    """The source writes the same facts in whichever order it likes."""
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                day_block(
                    0,
                    "31 AĞUSTOS 2026 / Pazartesi",
                    {1: "TEVFİK SAĞLAM AMFİSİ"},
                    [("08.30 - 09.10", {1: text})],
                )
            )
        )
    )

    assignment = document.assignments[0]
    assert assignment.class_year == 3
    assert assignment.program_language is ProgramLanguage.TURKISH
    assert assignment.curriculum_group == "3-A"
    assert assignment.department == "İÇ HASTALIKLARI"


def test_a_session_announced_for_both_programs_states_no_language() -> None:
    """Naming both narrows neither, so it must not be read as one of them."""
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                day_block(
                    0,
                    "31 AĞUSTOS 2026 / Pazartesi",
                    {1: "KEMAL ATAY AMFİSİ"},
                    [("08.30 - 09.10", {1: "DÖNEM 2-TÜRKÇE +İNGİLİZCE -DİLİM -FİZYOLOJİ"})],
                )
            )
        )
    )

    assert document.assignments[0].program_language is None


def test_a_cell_naming_no_class_year_is_accounted_for_rather_than_published() -> None:
    """Seminars and specialty examinations share the grid with lessons."""
    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "test",
            "parserProfile": {"name": PROFILE.name, "version": PROFILE.version},
            "sourceContext": {
                "academicYear": "2026-2027",
                "classYear": 1,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": snapshot(
                worksheet(
                    day_block(
                        0,
                        "31 AĞUSTOS 2026 / Pazartesi",
                        {1: "KEMAL ATAY AMFİSİ"},
                        [("08.30 - 09.10", {1: "ANESTEZİYOLOJİ SEMİNER -07.30-08.30"})],
                    )
                )
            ).model_dump(by_alias=True, mode="json"),
        }
    )

    response = get_parser(PROFILE.name, PROFILE.version)(request, PROFILE)

    assert response.candidates == []
    metrics = {metric.name: metric.value for metric in response.metrics}
    assert metrics["cells.ignored.amphitheatreCellNamesNoClassYear"] == 1


def test_the_profile_publishes_nothing_from_the_real_document() -> None:
    """A cell says which room a session uses, never that the session exists."""
    profile = get_profile("weekly_amphitheatre_v1", "1.0.0")
    assert profile is not None

    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "test",
            "parserProfile": {"name": profile.name, "version": profile.version},
            "sourceContext": {
                "academicYear": "2026-2027",
                "classYear": 1,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": load_fixture_json("real/shared-amphi.snapshot.json"),
        }
    )

    response: ParseSnapshotResponse = get_parser(profile.name, profile.version)(request, profile)

    assert response.candidates == []
    assert response.status is not ParserResultStatus.REJECTED
    metrics = {metric.name: metric.value for metric in response.metrics}
    assert metrics["candidates.emitted"] == 0
    assert metrics["amphitheatre.assignments"] > 0


def test_the_real_document_covers_the_week_its_day_titles_state() -> None:
    """The worksheet title says `31 AĞUSTOS-1 EYLÜL 2026`; the blocks say more.

    Neither the file name nor the tab title may decide the week, which is why
    every date comes from a day title row.
    """
    document = read_amphitheatre_document(
        NormalizedSpreadsheetSnapshot.model_validate(
            load_fixture_json("real/shared-amphi.snapshot.json")
        )
    )

    week = {date(2026, 8, 31) + __import__("datetime").timedelta(days=day) for day in range(5)}
    assert week <= set(document.dates())


def index_of(*assignments) -> AmphitheatreIndex:
    return AmphitheatreIndex(AmphitheatreDocument(assignments=tuple(assignments)))


def assignment_from(text: str, room: str):
    document = read_amphitheatre_document(
        snapshot(
            worksheet(
                day_block(
                    0,
                    "31 AĞUSTOS 2026 / Pazartesi",
                    {1: room},
                    [("08.30 - 09.10", {1: text})],
                )
            )
        )
    )
    return document.assignments[0]


def test_the_department_selects_between_two_rooms_in_one_hour() -> None:
    index = index_of(
        assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ", "KEMAL ATAY AMFİSİ"),
        assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -ANATOMİ", "SAMİ ZAN AMFİSİ"),
    )

    resolution = index.resolve(
        local_date=MONDAY,
        class_year=2,
        program_language=ProgramLanguage.TURKISH,
        curriculum_groups=(),
        departments=("ANATOMİ",),
        start_local_time=time(8, 30),
        end_local_time=time(9, 10),
    )

    assert resolution.room == "SAMİ ZAN AMFİSİ"
    assert resolution.reason == RULE_ASSIGNMENT


def test_a_lesson_with_no_department_takes_the_hour_s_only_room() -> None:
    """Under half the published lessons name a department (ADR-035)."""
    index = index_of(assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ", "KEMAL ATAY AMFİSİ"))

    resolution = index.resolve(
        local_date=MONDAY,
        class_year=2,
        program_language=ProgramLanguage.TURKISH,
        curriculum_groups=(),
        departments=(),
        start_local_time=time(8, 30),
        end_local_time=time(9, 10),
    )

    assert resolution.room == "KEMAL ATAY AMFİSİ"
    assert resolution.reason == REASON_UNANIMOUS_WITHOUT_DEPARTMENT


def test_two_rooms_that_survive_every_test_leave_the_lesson_unplaced() -> None:
    """Nothing picks a winner between two equally good rooms (ADR-035)."""
    index = index_of(
        assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ", "KEMAL ATAY AMFİSİ"),
        assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -ANATOMİ", "SAMİ ZAN AMFİSİ"),
    )

    resolution = index.resolve(
        local_date=MONDAY,
        class_year=2,
        program_language=ProgramLanguage.TURKISH,
        curriculum_groups=(),
        departments=(),
        start_local_time=time(8, 30),
        end_local_time=time(9, 10),
    )

    assert resolution.room is None
    assert resolution.reason == REASON_AMBIGUOUS


@pytest.mark.parametrize(
    ("class_year", "language", "groups", "start", "end"),
    [
        # Another class year's booking is not this lesson's room.
        (2, ProgramLanguage.TURKISH, ("3-A",), time(8, 30), time(9, 10)),
        # Nor is the other programme's.
        (3, ProgramLanguage.ENGLISH, ("3-A",), time(8, 30), time(9, 10)),
        # Nor is the other half of the class's.
        (3, ProgramLanguage.TURKISH, ("3-B",), time(8, 30), time(9, 10)),
        # Nor is a booking in a different hour.
        (3, ProgramLanguage.TURKISH, ("3-A",), time(13, 30), time(14, 10)),
    ],
)
def test_a_booking_for_someone_else_never_becomes_this_lesson_s_room(
    class_year: int,
    language: ProgramLanguage,
    groups: tuple[str, ...],
    start: time,
    end: time,
) -> None:
    index = index_of(
        assignment_from(
            "DÖNEM 3-TÜRKÇE -A GRUBU -DİLİM -FİZYOLOJİ",
            "KEMAL ATAY AMFİSİ",
        )
    )

    resolution = index.resolve(
        local_date=MONDAY,
        class_year=class_year,
        program_language=language,
        curriculum_groups=groups,
        departments=("FİZYOLOJİ",),
        start_local_time=start,
        end_local_time=end,
    )

    assert resolution.room is None
    assert resolution.reason == REASON_NO_ASSIGNMENT


def test_an_all_day_item_is_never_given_a_room() -> None:
    """It occupies no hour the weekly grid could place it in."""
    index = index_of(assignment_from("DÖNEM 2-TÜRKÇE -DİLİM -FİZYOLOJİ", "KEMAL ATAY AMFİSİ"))

    resolution = index.resolve(
        local_date=MONDAY,
        class_year=2,
        program_language=ProgramLanguage.TURKISH,
        curriculum_groups=(),
        departments=(),
        start_local_time=None,
        end_local_time=None,
    )

    assert resolution.room is None


def test_neither_companion_reader_claims_the_other_s_document() -> None:
    """Grade 3 is handed two companions, and each must ignore the other's.

    Both readers are offered every auxiliary snapshot, because the parse request
    says which documents came along but not which family each belongs to. That is
    only safe while a document of one family states nothing the other recognizes:
    if the amphitheatre workbook produced bedside topics, a wrong topic would be
    written onto a session a student attends.
    """
    from sirkadiyen_parser.normalization.dates import NumericDateOrder as Order
    from sirkadiyen_parser.parsers.bedside import read_bedside_document

    amphitheatre = NormalizedSpreadsheetSnapshot.model_validate(
        load_fixture_json("real/shared-amphi.snapshot.json")
    )
    bedside = NormalizedSpreadsheetSnapshot.model_validate(
        load_fixture_json("real/g3-tr-a-bedside.snapshot.json")
    )

    assert read_amphitheatre_document(bedside).assignments == ()

    read_as_bedside = read_bedside_document(
        amphitheatre,
        class_year=3,
        numeric_date_order=Order.DAY_FIRST,
    )
    assert read_as_bedside.slots == []
    assert read_as_bedside.topics_by_date() == {}
