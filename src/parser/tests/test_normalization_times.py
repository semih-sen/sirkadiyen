from datetime import time

import pytest

from sirkadiyen_parser.contracts.snapshot import NormalizedCell
from sirkadiyen_parser.normalization.times import (
    RULE_COMPACT_TEXT,
    RULE_FRACTION,
    RULE_TEXT,
    duration_minutes,
    resolve_cell_time,
    resolve_cell_time_range,
    resolve_time_range_text,
    resolve_time_text,
    time_from_fraction,
)
from tests.support import snapshots

EN_DASH = chr(0x2013)


def number_cell(value: float, *, number_format_type: str | None = None) -> NormalizedCell:
    return snapshots.number_cell(0, 0, value, number_format_type=number_format_type)


def text_cell(value: str) -> NormalizedCell:
    return snapshots.text_cell(0, 0, value)


def test_time_from_fraction_converts_a_day_fraction() -> None:
    assert time_from_fraction(0.375) == time(9, 0)


def test_time_from_fraction_absorbs_float_representation_error() -> None:
    assert time_from_fraction(0.4513888888888889) == time(10, 50)


def test_time_from_fraction_uses_only_the_fractional_part() -> None:
    assert time_from_fraction(45973.375) == time(9, 0)


@pytest.mark.parametrize("text", ["09:00", "09.00", "09,00", "9:00"])
def test_resolve_time_text_accepts_every_common_separator(text: str) -> None:
    resolution = resolve_time_text(text)

    assert resolution.value == time(9, 0)
    assert resolution.rule == RULE_TEXT


def test_resolve_time_text_leaves_a_compact_block_unresolved_by_default() -> None:
    resolution = resolve_time_text("0900")

    assert not resolution.resolved
    assert resolution.reason == "unrecognizedTimeFormat"


def test_resolve_time_text_accepts_a_compact_block_when_the_profile_opts_in() -> None:
    resolution = resolve_time_text("0900", allow_compact=True)

    assert resolution.value == time(9, 0)
    assert resolution.rule == RULE_COMPACT_TEXT
    assert resolution.confidence < 1.0


def test_resolve_time_text_rejects_an_impossible_clock_time() -> None:
    assert resolve_time_text("25:00").reason == "impossibleClockTime"


@pytest.mark.parametrize(
    "text",
    ["09:00-10:50", "09.00 - 10.50", f"09:00 {EN_DASH} 10:50", "09:00 / 10:50"],
)
def test_resolve_time_range_text_accepts_every_common_separator(text: str) -> None:
    resolution = resolve_time_range_text(text)

    assert resolution.start == time(9, 0)
    assert resolution.end == time(10, 50)


def test_resolve_time_range_text_refuses_to_reorder_a_backwards_range() -> None:
    resolution = resolve_time_range_text("10:50-09:00")

    assert not resolution.resolved
    assert resolution.reason == "endTimeNotAfterStartTime"


def test_resolve_time_range_text_rejects_a_zero_length_range() -> None:
    assert resolve_time_range_text("09:00-09:00").reason == "endTimeNotAfterStartTime"


def test_resolve_time_range_text_rejects_a_single_time() -> None:
    assert resolve_time_range_text("09:00").reason == "unrecognizedTimeRangeFormat"


def test_resolve_cell_time_reads_a_fraction_declared_as_a_time() -> None:
    resolution = resolve_cell_time(number_cell(0.375, number_format_type="TIME"))

    assert resolution.value == time(9, 0)
    assert resolution.rule == RULE_FRACTION


def test_resolve_cell_time_leaves_a_bare_fraction_unresolved_by_default() -> None:
    assert not resolve_cell_time(number_cell(0.375)).resolved


def test_resolve_cell_time_reads_a_bare_fraction_when_the_profile_opts_in() -> None:
    resolution = resolve_cell_time(number_cell(0.375), allow_bare_fraction=True)

    assert resolution.value == time(9, 0)


def test_resolve_cell_time_range_reads_readable_text() -> None:
    resolution = resolve_cell_time_range(text_cell("13:00-14:50"))

    assert resolution.start == time(13, 0)
    assert resolution.end == time(14, 50)


def test_resolve_cell_time_range_reports_a_missing_cell() -> None:
    assert resolve_cell_time_range(None).reason == "missingCell"


def test_duration_minutes() -> None:
    assert duration_minutes(time(9, 0), time(10, 50)) == 110
