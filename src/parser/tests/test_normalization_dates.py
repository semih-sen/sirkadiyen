from datetime import date

import pytest

from sirkadiyen_parser.contracts.snapshot import NormalizedCell
from sirkadiyen_parser.normalization.dates import (
    REASON_NUMERIC_ORDER_CONTRADICTED,
    REASON_NUMERIC_ORDER_NOT_DECLARED,
    RULE_ISO,
    RULE_MONTH_NAME,
    RULE_NUMERIC_DAY_FIRST,
    RULE_NUMERIC_MONTH_FIRST,
    RULE_NUMERIC_SINGLE_READING,
    RULE_SERIAL,
    RULE_UNRESOLVED,
    NumericDateOrder,
    date_from_serial,
    resolve_cell_date,
    resolve_date_text,
)
from tests.support import snapshots


def number_cell(value: float, *, number_format_type: str | None = None) -> NormalizedCell:
    return snapshots.number_cell(0, 0, value, number_format_type=number_format_type)


def text_cell(value: str) -> NormalizedCell:
    return snapshots.text_cell(0, 0, value)


def test_date_from_serial_uses_the_sheets_epoch() -> None:
    assert date_from_serial(45973) == date(2025, 11, 12)


def test_date_from_serial_ignores_a_time_component() -> None:
    assert date_from_serial(45973.375) == date(2025, 11, 12)


def test_date_from_serial_rejects_the_fictitious_1900_window() -> None:
    assert date_from_serial(59) is None


@pytest.mark.parametrize(
    ("text", "expected", "rule"),
    [
        ("2025-11-12", date(2025, 11, 12), RULE_ISO),
        ("12 Kasım 2025", date(2025, 11, 12), RULE_MONTH_NAME),
        ("12 KASIM 2025", date(2025, 11, 12), RULE_MONTH_NAME),
        ("12 November 2025", date(2025, 11, 12), RULE_MONTH_NAME),
    ],
)
def test_resolve_date_text_supported_forms(text: str, expected: date, rule: str) -> None:
    resolution = resolve_date_text(text)

    assert resolution.value == expected
    assert resolution.rule == rule


@pytest.mark.parametrize("text", ["12.11.2025", "12/11/2025", "12-11-2025"])
def test_a_declared_day_first_order_reads_the_day_first(text: str) -> None:
    resolution = resolve_date_text(text, numeric_order=NumericDateOrder.DAY_FIRST)

    assert resolution.value == date(2025, 11, 12)
    assert resolution.rule == RULE_NUMERIC_DAY_FIRST


def test_a_declared_month_first_order_reads_the_month_first() -> None:
    resolution = resolve_date_text("12/11/2025", numeric_order=NumericDateOrder.MONTH_FIRST)

    assert resolution.value == date(2025, 12, 11)
    assert resolution.rule == RULE_NUMERIC_MONTH_FIRST


def test_an_undeclared_order_refuses_a_date_that_means_two_things() -> None:
    # The whole point of the declaration: 12/11 is a real date either way, and
    # publishing one of them would put a lesson a month from where it belongs.
    resolution = resolve_date_text("12/11/2025")

    assert not resolution.resolved
    assert resolution.reason == REASON_NUMERIC_ORDER_NOT_DECLARED


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("25/11/2025", date(2025, 11, 25)),
        ("11/25/2025", date(2025, 11, 25)),
        ("07/07/2025", date(2025, 7, 7)),
    ],
)
def test_an_undeclared_order_still_reads_a_date_with_one_meaning(
    text: str,
    expected: date,
) -> None:
    # No declaration is needed when both orders agree, either because only one of
    # them names a real date or because the components are the same.
    resolution = resolve_date_text(text)

    assert resolution.value == expected
    assert resolution.rule == RULE_NUMERIC_SINGLE_READING


def test_a_date_contradicting_the_declared_order_is_refused_not_reordered() -> None:
    # A day-first profile meeting 11/25 has either a bad cell or a wrong
    # declaration. Silently reordering it would hide both.
    resolution = resolve_date_text("11/25/2025", numeric_order=NumericDateOrder.DAY_FIRST)

    assert not resolution.resolved
    assert resolution.reason == REASON_NUMERIC_ORDER_CONTRADICTED


def test_a_declared_order_is_reported_with_the_weekday_it_disagrees_with() -> None:
    resolution = resolve_date_text(
        "12/11/2025 Pazartesi",
        numeric_order=NumericDateOrder.DAY_FIRST,
    )

    assert resolution.value == date(2025, 11, 12)
    assert resolution.weekday_matches is False


def test_resolve_date_text_reports_an_agreeing_weekday() -> None:
    resolution = resolve_date_text("12 Kasım 2025 Çarşamba")

    assert resolution.value == date(2025, 11, 12)
    assert resolution.weekday_text == "Çarşamba"
    assert resolution.weekday_matches is True


def test_resolve_date_text_reports_a_disagreeing_weekday_without_correcting_it() -> None:
    resolution = resolve_date_text("12 Kasım 2025 Pazartesi")

    assert resolution.value == date(2025, 11, 12)
    assert resolution.weekday_matches is False


def test_resolve_date_text_never_invents_a_missing_year() -> None:
    resolution = resolve_date_text("12 Kasım")

    assert not resolution.resolved
    assert resolution.reason == "missingYearAndNoProfileYearRule"


def test_resolve_date_text_applies_a_profile_year_with_reduced_confidence() -> None:
    resolution = resolve_date_text("12 Kasım", year_hint=2025)

    assert resolution.value == date(2025, 11, 12)
    assert resolution.reason == "yearSuppliedByProfile"
    assert resolution.confidence < 1.0


def test_resolve_date_text_rejects_two_digit_years() -> None:
    resolution = resolve_date_text("12.11.25")

    assert not resolution.resolved
    assert resolution.reason == "twoDigitYearNotSupported"


def test_resolve_date_text_rejects_an_impossible_calendar_date() -> None:
    resolution = resolve_date_text("31.02.2025")

    assert not resolution.resolved
    assert resolution.reason == "impossibleCalendarDate"


def test_resolve_date_text_rejects_unknown_text() -> None:
    resolution = resolve_date_text("Ders Programı")

    assert resolution.rule == RULE_UNRESOLVED
    assert resolution.reason == "unrecognizedDateFormat"


def test_resolve_cell_date_reads_a_serial_declared_as_a_date() -> None:
    resolution = resolve_cell_date(number_cell(45973, number_format_type="DATE"))

    assert resolution.value == date(2025, 11, 12)
    assert resolution.rule == RULE_SERIAL


def test_resolve_cell_date_leaves_a_bare_serial_unresolved_by_default() -> None:
    resolution = resolve_cell_date(number_cell(45973))

    assert not resolution.resolved
    assert resolution.reason == "unrecognizedDateFormat"


def test_resolve_cell_date_reads_a_bare_serial_when_the_profile_opts_in() -> None:
    resolution = resolve_cell_date(number_cell(45973), allow_bare_serial=True)

    assert resolution.value == date(2025, 11, 12)
    assert resolution.rule == RULE_SERIAL


def test_resolve_cell_date_falls_back_to_readable_text() -> None:
    resolution = resolve_cell_date(
        text_cell("12.11.2025"),
        numeric_order=NumericDateOrder.DAY_FIRST,
    )

    assert resolution.value == date(2025, 11, 12)


def test_resolve_cell_date_carries_the_undeclared_order_into_the_text_reading() -> None:
    resolution = resolve_cell_date(text_cell("12.11.2025"))

    assert not resolution.resolved
    assert resolution.reason == REASON_NUMERIC_ORDER_NOT_DECLARED


def test_resolve_cell_date_reports_a_missing_cell() -> None:
    assert resolve_cell_date(None).reason == "missingCell"
