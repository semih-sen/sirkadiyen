"""Unit tests for the Grade 3 bedside document reader.

The profile publishes nothing, so what these tests pin is the reading: how the
two differently shaped schedule tables are paired, and how a topic code is
matched to its description despite the prefixes the source cannot spell
consistently.
"""

from datetime import date
from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import (
    ParserResultStatus,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
)
from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot
from sirkadiyen_parser.normalization.dates import NumericDateOrder
from sirkadiyen_parser.parsers import get_parser
from sirkadiyen_parser.parsers.bedside import (
    SECTION_CHILD_HEALTH,
    SECTION_INTERNAL_MEDICINE,
    BedsideDocument,
    TopicCode,
    parse_bedside_snapshot,
    read_bedside_document,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition, get_profile

PROFILE = ParserProfileDefinition(
    "grade3_bedside_v1",
    "1.0.0",
    "bedsidePractice",
    NumericDateOrder.DAY_FIRST,
    ("curriculumGroup",),
)


def text_cell(row: int, column: int, value: str) -> dict[str, Any]:
    return {
        "rowIndex": row,
        "columnIndex": column,
        "a1Address": f"R{row}C{column}",
        "effectiveValue": {"kind": "text", "textValue": value},
        "formattedValue": value,
    }


def worksheet(
    title: str,
    rows: list[list[str]],
    *,
    index: int = 0,
) -> dict[str, Any]:
    cells = [
        text_cell(row, column, value)
        for row, values in enumerate(rows)
        for column, value in enumerate(values)
        if value
    ]
    return {
        "sheetId": str(index + 1),
        "title": title,
        "index": index,
        "rowCount": len(rows),
        "columnCount": max((len(values) for values in rows), default=1),
        "cells": cells,
    }


#: The A document's schedule table: a blank spacer column between the autumn and
#: spring pairs.
SPACED_SCHEDULE = [
    ["İÇ HASTALIKLARI - ÇOCUK SAĞLIĞI VE HASTALIKLARI", "", "", "", ""],
    ["Güz Yarıyılı", "", "", "Bahar Yarıyılı", ""],
    ["Tarih", "A Grubu", "", "Tarih", "A Grubu"],
    ["01.10.2026", "İçH U1", "", "01.02.2027", "İçH U24"],
    ["02.10.2026", "ÇSvH U1", "", "02.02.2027", "ÇSvH U24"],
]

#: The B document's, with no spacer and upper-case headers.
UNSPACED_SCHEDULE = [
    ["İÇ HASTALIKLARI - ÇOCUK SAĞLIĞI VE HASTALIKLARI", "", "", ""],
    ["Güz Yarıyılı", "", "Bahar Yarıyılı", ""],
    ["TARİH", "B GRUBU", "TARİH", "B GRUBU"],
    ["01.10.2026", "İçH U1", "01.02.2027", "İçH U24"],
    ["02.10.2026", "ÇSvH U1", "02.02.2027", "ÇSvH U24"],
]

CATALOGUE = [
    ["HASTA BAŞI UYGULAMA KONULARI (13.30-14.20)"],
    ["İÇ HASTALIKLARI"],
    ["UYGULAMALI ÇALIŞMA KONULARI"],
    ["İçH - U 1"],
    ["Hastaya yaklaşım ve anamnez alma."],
    ["IçH - U 24"],
    ["Karında ağrılı noktalar."],
    ["ÇOCUK SAĞLIĞI VE HASTALIKLARI UYGULAMALI ÇALIŞMA KONULARI"],
    ["ÇSH -U 1"],
    ["Tanışma ve grup düzeni."],
    ["ÇSH -U 24"],
    ["Çocukta kan basıncı ölçümü."],
]


def snapshot(worksheets: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "contractVersion": "1.0",
        "sourceId": "TEST-SOURCE",
        "snapshotId": "test-snapshot",
        "spreadsheetId": "test-spreadsheet",
        "acquiredAtUtc": "2026-08-15T09:00:00Z",
        "contentHash": "sha256:test",
        "contentHashAlgorithm": "SHA-256",
        "worksheets": worksheets,
    }


def read(worksheets: list[dict[str, Any]]) -> BedsideDocument:
    validated = NormalizedSpreadsheetSnapshot.model_validate(snapshot(worksheets))
    return read_bedside_document(
        validated,
        class_year=3,
        numeric_date_order=PROFILE.numeric_date_order,
    )


def parse(worksheets: list[dict[str, Any]]) -> ParseSnapshotResponse:
    request = ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": "unit-test",
            "parserProfile": {"name": PROFILE.name, "version": PROFILE.version},
            "sourceContext": {
                "academicYear": "2026-2027",
                "classYear": 3,
                "programLanguage": "turkish",
                "timeZoneId": "Europe/Istanbul",
            },
            "snapshot": snapshot(worksheets),
        }
    )
    return parse_bedside_snapshot(request, PROFILE)


def metrics(response: ParseSnapshotResponse) -> dict[str, float]:
    return {metric.name: metric.value for metric in response.metrics}


def test_the_registered_profile_is_the_bedside_implementation() -> None:
    profile = get_profile("grade3_bedside_v1", "1.0.0")

    assert profile is not None
    assert get_parser(profile.name, profile.version) is parse_bedside_snapshot


def test_the_document_publishes_no_sessions_of_its_own() -> None:
    """The annual program owns these events, because it proves each one's time.

    This document's only time is a heading over its catalogue, and that heading
    is wrong for the sessions the annual puts at 14:00 (ADR-087).
    """
    response = parse(
        [worksheet("Text 1", CATALOGUE), worksheet("Table 1", SPACED_SCHEDULE, index=1)]
    )

    assert response.candidates == []
    assert response.status is ParserResultStatus.COMPLETED
    counts = metrics(response)
    # It is still read in full, so a reviewer can see what it stated.
    assert counts["schedule.entries"] == 4
    assert counts["candidates.emitted"] == 0


@pytest.mark.parametrize(
    ("rows", "group"),
    ((SPACED_SCHEDULE, "3-A"), (UNSPACED_SCHEDULE, "3-B")),
)
def test_both_table_shapes_pair_their_date_and_topic_columns(
    rows: list[list[str]],
    group: str,
) -> None:
    """One document separates its two semester pairs with a blank column.

    Pairing by position would read the spacer as a date in one of them, so the
    columns are paired by header instead.
    """
    document = read([worksheet("Table 1", rows)])

    assert {slot.curriculum_group for slot in document.slots} == {group}
    assert {slot.local_date for slot in document.slots} == {
        date(2026, 10, 1),
        date(2026, 10, 2),
        date(2027, 2, 1),
        date(2027, 2, 2),
    }


def test_a_dotted_date_is_read_day_first() -> None:
    """`01.10.2026` is the first of October, and the document proves the order.

    Several of its dates state a day above twelve, so the profile declares
    day-first rather than leaving those cells refused (ADR-075).
    """
    document = read([worksheet("Table 1", SPACED_SCHEDULE)])

    assert date(2026, 10, 1) in {slot.local_date for slot in document.slots}
    assert date(2026, 1, 10) not in {slot.local_date for slot in document.slots}


def test_a_topic_is_matched_by_section_and_ordinal_not_by_its_prefix() -> None:
    """The schedule writes `ÇSvH` where the catalogue writes `ÇSH`.

    The same document also writes `İçH` and `IçH` with two different capital
    I's, so matching on the prefix would lose most of the catalogue. The section
    headings it is written under are unambiguous, and those decide.
    """
    document = read(
        [worksheet("Text 1", CATALOGUE), worksheet("Table 1", SPACED_SCHEDULE, index=1)]
    )

    assert document.topics[TopicCode(SECTION_CHILD_HEALTH, 1)] == "Tanışma ve grup düzeni."
    assert document.topics[TopicCode(SECTION_INTERNAL_MEDICINE, 24)] == "Karında ağrılı noktalar."

    by_date = document.topics_by_date()
    assert by_date[("3-A", date(2026, 10, 2))] == "Tanışma ve grup düzeni."
    assert by_date[("3-A", date(2027, 2, 1))] == "Karında ağrılı noktalar."


def test_a_code_and_its_description_in_one_cell_are_read_together() -> None:
    """Word wraps some topics in a one-cell table, and a cell's own line breaks
    do not survive the conversion, so the code and its description arrive as one
    line."""
    catalogue = [
        ["ÇOCUK SAĞLIĞI VE HASTALIKLARI UYGULAMALI ÇALIŞMA KONULARI"],
        ["ÇSH -U 43 Maket üzerinde subkutan enjeksiyon uygulaması"],
    ]

    document = read([worksheet("Table 1", catalogue)])

    assert (
        document.topics[TopicCode(SECTION_CHILD_HEALTH, 43)]
        == "Maket üzerinde subkutan enjeksiyon uygulaması"
    )


def test_a_catalogue_split_across_worksheets_is_still_one_catalogue() -> None:
    """A worksheet boundary is only where Word ended a table (ADR-076).

    The A document puts a code at the end of one worksheet and its description
    at the start of the next, so reading each worksheet alone loses the topic.
    """
    document = read(
        [
            worksheet("Text 1", [["İÇ HASTALIKLARI"], ["İçH - U 7"]]),
            worksheet("Table 1", [["Kalp muayenesi."]], index=1),
        ]
    )

    assert document.topics[TopicCode(SECTION_INTERNAL_MEDICINE, 7)] == "Kalp muayenesi."


def test_one_description_written_for_a_run_of_codes_covers_every_one() -> None:
    """`IçH - U 39-43` is one description the source wrote for five sessions."""
    document = read(
        [worksheet("Text 1", [["İÇ HASTALIKLARI"], ["IçH - U 39-43"], ["Serbest uygulama."]])]
    )

    assert all(
        document.topics[TopicCode(SECTION_INTERNAL_MEDICINE, ordinal)] == "Serbest uygulama."
        for ordinal in range(39, 44)
    )


def test_a_code_with_no_catalogue_entry_leaves_its_session_without_a_topic() -> None:
    """A guessed topic is worse than none: the event keeps the description it has."""
    document = read([worksheet("Table 1", SPACED_SCHEDULE)])

    assert len(document.slots) == 4
    assert document.topics_by_date() == {}
    assert all(document.topic_for(slot) is None for slot in document.slots)


def test_a_document_with_no_schedule_table_is_rejected() -> None:
    response = parse([worksheet("Text 1", CATALOGUE)])

    assert response.status is ParserResultStatus.REJECTED
    assert any(warning.code == "noBedsideScheduleTable" for warning in response.warnings)


def test_the_preamble_hour_is_never_read_as_a_session_time() -> None:
    """`(13.30-14.20)` is a heading over a catalogue, not a per-session time.

    It is wrong for the twenty-two sessions the annual program places at 14:00,
    which is the whole reason this profile publishes nothing.
    """
    response = parse(
        [worksheet("Text 1", CATALOGUE), worksheet("Table 1", SPACED_SCHEDULE, index=1)]
    )

    assert response.candidates == []
