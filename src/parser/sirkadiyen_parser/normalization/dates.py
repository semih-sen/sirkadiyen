"""Date resolution.

Schedule dates arrive either as Google Sheets serial numbers or as human text in
Turkish or English. Resolution is explicit: every result records the rule that
produced it, and an unresolvable value stays unresolved with a reason.

Three behaviours are deliberately opt-in, because guessing them would silently
invent schedule data:

- a bare numeric cell is only read as a serial when the caller allows it
- a date without a year is only completed when the caller supplies a year
- ``12/11/2026`` is only read in a component order the parser profile declares

The last one is the dangerous case, because both readings of it are real dates
and a wrong one is indistinguishable from a right one. An undeclared profile
therefore publishes a numeric date only when both orders agree on what it means
(ADR-051).
"""

import re
from dataclasses import dataclass
from datetime import date, timedelta
from enum import StrEnum

from sirkadiyen_parser.contracts.snapshot import NormalizedCell
from sirkadiyen_parser.normalization.grid import cell_display_text, cell_number
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text

#: Google Sheets and Excel count days from this epoch.
SERIAL_EPOCH = date(1899, 12, 30)

#: Serials below this value fall inside the fictitious 1900 leap-year window and
#: cannot represent a real academic date.
MIN_SUPPORTED_SERIAL = 61

RULE_SERIAL = "serialDate"
RULE_ISO = "isoDate"
RULE_NUMERIC_DAY_FIRST = "numericDayFirstDate"
RULE_NUMERIC_MONTH_FIRST = "numericMonthFirstDate"
RULE_NUMERIC_SINGLE_READING = "numericSingleReadingDate"
RULE_MONTH_NAME = "monthNameDate"
RULE_UNRESOLVED = "unresolvedDate"

#: The numeric cell has two possible meanings and the profile has not said which
#: one this source writes, so nothing is published from it.
REASON_NUMERIC_ORDER_NOT_DECLARED = "numericDateOrderNotDeclaredByProfile"

#: The order the profile declared yields no real date but the other order does.
#: Either the cell is wrong or the declaration is, and both are worth a look.
REASON_NUMERIC_ORDER_CONTRADICTED = "numericDateImpossibleUnderDeclaredOrder"

CONFIDENCE_SERIAL = 1.0
CONFIDENCE_ISO = 1.0
CONFIDENCE_NUMERIC = 0.95
CONFIDENCE_MONTH_NAME = 0.9
#: A year supplied by profile configuration is weaker evidence than a year read
#: from the source cell.
CONFIDENCE_YEAR_FROM_PROFILE = 0.7


class NumericDateOrder(StrEnum):
    """The component order a parser profile declares for numeric date cells.

    ``UNDECLARED`` is not a synonym for day-first. It states that no source
    evidence has established the order yet, which is the honest position for
    every profile whose committed fixtures write dates only as serials or with a
    month name. Declaring an order is a claim about the source document, so it
    belongs to the parser profile rather than to a shared default.
    """

    DAY_FIRST = "dayFirst"
    MONTH_FIRST = "monthFirst"
    UNDECLARED = "undeclared"


_MONTHS_BY_KEY = {
    "ocak": 1,
    "subat": 2,
    "mart": 3,
    "nisan": 4,
    "mayis": 5,
    "haziran": 6,
    "temmuz": 7,
    "agustos": 8,
    "eylul": 9,
    "ekim": 10,
    "kasim": 11,
    "aralik": 12,
    "january": 1,
    "february": 2,
    "march": 3,
    "april": 4,
    "may": 5,
    "june": 6,
    "july": 7,
    "august": 8,
    "september": 9,
    "october": 10,
    "november": 11,
    "december": 12,
    "jan": 1,
    "feb": 2,
    "mar": 3,
    "apr": 4,
    "jun": 6,
    "jul": 7,
    "aug": 8,
    "sep": 9,
    "sept": 9,
    "oct": 10,
    "nov": 11,
    "dec": 12,
}

#: Monday is 0, matching :meth:`datetime.date.weekday`.
_WEEKDAYS_BY_KEY = {
    "pazartesi": 0,
    "sali": 1,
    "carsamba": 2,
    "persembe": 3,
    "cuma": 4,
    "cumartesi": 5,
    "pazar": 6,
    "monday": 0,
    "tuesday": 1,
    "wednesday": 2,
    "thursday": 3,
    "friday": 4,
    "saturday": 5,
    "sunday": 6,
}

_ISO_PATTERN = re.compile(r"^(\d{4})-(\d{2})-(\d{2})$")
_NUMERIC_PATTERN = re.compile(r"^(\d{1,2})[./-](\d{1,2})(?:[./-](\d{2}|\d{4}))?$")
_MONTH_NAME_PATTERN = re.compile(r"^(\d{1,2})\s+([^\W\d_]+)(?:\s+(\d{4}))?$", re.UNICODE)
_TOKEN_PATTERN = re.compile(r"[^\W\d_]+", re.UNICODE)


@dataclass(frozen=True, slots=True)
class DateResolution:
    """The outcome of interpreting a cell as a schedule date."""

    value: date | None
    rule: str
    confidence: float
    reason: str | None = None
    weekday_text: str | None = None
    weekday_matches: bool | None = None

    @property
    def resolved(self) -> bool:
        """Whether a date was produced."""
        return self.value is not None


def unresolved_date(reason: str) -> DateResolution:
    """Build an unresolved result carrying an explicit reason code."""
    return DateResolution(value=None, rule=RULE_UNRESOLVED, confidence=0.0, reason=reason)


def date_from_serial(serial: float) -> date | None:
    """Convert a spreadsheet date serial to a date.

    Returns ``None`` for serials that cannot denote a real date. Fractional
    serials carry a time component; the date part is used and the fraction is
    ignored here because time resolution is a separate concern.
    """
    whole_days = int(serial)
    if whole_days < MIN_SUPPORTED_SERIAL:
        return None
    try:
        return SERIAL_EPOCH + timedelta(days=whole_days)
    except OverflowError:
        return None


def resolve_date_text(
    value: str,
    *,
    year_hint: int | None = None,
    numeric_order: NumericDateOrder = NumericDateOrder.UNDECLARED,
) -> DateResolution:
    """Interpret human-readable date text.

    ``year_hint`` completes a date written without a year. It represents an
    explicit parser-profile rule, so the result is reported with reduced
    confidence and an explicit reason.

    ``numeric_order`` is the order the parser profile declares for ``12/11/2026``
    style cells. It defaults to undeclared, so a caller that has not stated the
    order cannot silently read one.
    """
    text = normalize_text(value)
    if not text:
        return unresolved_date("emptyDateText")

    weekday_text, weekday_index = _extract_weekday(text)
    remainder = _strip_weekday(text) if weekday_text is not None else text

    resolution = _resolve_date_body(remainder, year_hint=year_hint, numeric_order=numeric_order)
    resolved_value = resolution.value
    if resolved_value is None or weekday_index is None:
        return resolution

    matches = resolved_value.weekday() == weekday_index
    return DateResolution(
        value=resolved_value,
        rule=resolution.rule,
        confidence=resolution.confidence,
        reason=resolution.reason,
        weekday_text=weekday_text,
        weekday_matches=matches,
    )


def resolve_cell_date(
    cell: NormalizedCell | None,
    *,
    year_hint: int | None = None,
    allow_bare_serial: bool = False,
    numeric_order: NumericDateOrder = NumericDateOrder.UNDECLARED,
) -> DateResolution:
    """Interpret a cell as a schedule date.

    A numeric cell is read as a serial when its number format declares a date,
    or when ``allow_bare_serial`` records that the parser profile has confirmed
    the column holds serial dates. Otherwise the readable text is interpreted
    under the profile's declared ``numeric_order``.
    """
    if cell is None:
        return unresolved_date("missingCell")

    number = cell_number(cell)
    if number is not None and (allow_bare_serial or _declares_date_format(cell)):
        resolved = date_from_serial(number)
        if resolved is None:
            return unresolved_date("serialOutOfSupportedRange")
        return DateResolution(value=resolved, rule=RULE_SERIAL, confidence=CONFIDENCE_SERIAL)

    text = cell_display_text(cell)
    if text is None:
        return unresolved_date("emptyCell")
    return resolve_date_text(text, year_hint=year_hint, numeric_order=numeric_order)


def _declares_date_format(cell: NormalizedCell) -> bool:
    number_format = cell.effective_format.number_format_type if cell.effective_format else None
    return number_format is not None and comparison_key(number_format) in {"date", "datetime"}


def _extract_weekday(text: str) -> tuple[str | None, int | None]:
    for token in _TOKEN_PATTERN.findall(text):
        index = _WEEKDAYS_BY_KEY.get(comparison_key(token))
        if index is not None:
            return token, index
    return None, None


def _strip_weekday(text: str) -> str:
    kept: list[str] = []
    for token in text.split(" "):
        letters = "".join(_TOKEN_PATTERN.findall(token))
        if letters and comparison_key(letters) in _WEEKDAYS_BY_KEY:
            continue
        kept.append(token)
    return " ".join(kept).strip(" ,-")


def _resolve_date_body(
    text: str,
    *,
    year_hint: int | None,
    numeric_order: NumericDateOrder,
) -> DateResolution:
    iso_match = _ISO_PATTERN.match(text)
    if iso_match is not None:
        return _build(
            int(iso_match.group(1)),
            int(iso_match.group(2)),
            int(iso_match.group(3)),
            rule=RULE_ISO,
            confidence=CONFIDENCE_ISO,
        )

    numeric_match = _NUMERIC_PATTERN.match(text)
    if numeric_match is not None:
        return _resolve_numeric(numeric_match, year_hint=year_hint, numeric_order=numeric_order)

    month_match = _MONTH_NAME_PATTERN.match(text)
    if month_match is not None:
        month = _MONTHS_BY_KEY.get(comparison_key(month_match.group(2)))
        if month is None:
            return unresolved_date("unknownMonthName")
        year, confidence, reason = _resolve_year(
            month_match.group(3), year_hint, CONFIDENCE_MONTH_NAME
        )
        if year is None:
            return unresolved_date(reason or "missingYear")
        return _build(
            year,
            month,
            int(month_match.group(1)),
            rule=RULE_MONTH_NAME,
            confidence=confidence,
            reason=reason,
        )

    return unresolved_date("unrecognizedDateFormat")


def _resolve_numeric(
    match: re.Match[str],
    *,
    year_hint: int | None,
    numeric_order: NumericDateOrder,
) -> DateResolution:
    """Interpret ``12/11/2026`` under the order the profile declared.

    Both components are read both ways before anything is decided, because what
    the alternative reading yields is the evidence: a declared order that cannot
    produce a date while the other one can is a contradiction worth reporting,
    and two readings that agree need no declaration at all.
    """
    year, confidence, reason = _resolve_year(match.group(3), year_hint, CONFIDENCE_NUMERIC)
    if year is None:
        return unresolved_date(reason or "missingYear")

    first = int(match.group(1))
    second = int(match.group(2))
    as_day_first = _try_build(year, month=second, day=first)
    as_month_first = _try_build(year, month=first, day=second)

    if numeric_order is NumericDateOrder.DAY_FIRST:
        return _as_declared(
            as_day_first, as_month_first, RULE_NUMERIC_DAY_FIRST, confidence, reason
        )
    if numeric_order is NumericDateOrder.MONTH_FIRST:
        return _as_declared(
            as_month_first, as_day_first, RULE_NUMERIC_MONTH_FIRST, confidence, reason
        )

    if as_day_first is not None and as_month_first is not None and as_day_first != as_month_first:
        # The cell states two different real dates depending on a rule this
        # profile has never stated. Publishing either one would be a guess.
        return unresolved_date(REASON_NUMERIC_ORDER_NOT_DECLARED)

    single = as_day_first if as_day_first is not None else as_month_first
    if single is None:
        return unresolved_date("impossibleCalendarDate")

    # Either only one order yields a real date, or both yield the same one. The
    # reading is then independent of the order, so no declaration is needed.
    return DateResolution(
        value=single,
        rule=RULE_NUMERIC_SINGLE_READING,
        confidence=confidence,
        reason=reason,
    )


def _as_declared(
    declared: date | None,
    alternative: date | None,
    rule: str,
    confidence: float,
    reason: str | None,
) -> DateResolution:
    if declared is not None:
        return DateResolution(value=declared, rule=rule, confidence=confidence, reason=reason)
    if alternative is not None:
        return unresolved_date(REASON_NUMERIC_ORDER_CONTRADICTED)
    return unresolved_date("impossibleCalendarDate")


def _try_build(year: int, *, month: int, day: int) -> date | None:
    try:
        return date(year, month, day)
    except ValueError:
        return None


def _resolve_year(
    year_text: str | None,
    year_hint: int | None,
    confidence: float,
) -> tuple[int | None, float, str | None]:
    if year_text is None:
        if year_hint is None:
            return None, 0.0, "missingYearAndNoProfileYearRule"
        return year_hint, min(confidence, CONFIDENCE_YEAR_FROM_PROFILE), "yearSuppliedByProfile"

    if len(year_text) == 2:
        # Two-digit years are ambiguous across centuries and are never guessed.
        return None, 0.0, "twoDigitYearNotSupported"
    return int(year_text), confidence, None


def _build(
    year: int,
    month: int,
    day: int,
    *,
    rule: str,
    confidence: float,
    reason: str | None = None,
) -> DateResolution:
    value = _try_build(year, month=month, day=day)
    if value is None:
        return unresolved_date("impossibleCalendarDate")
    return DateResolution(value=value, rule=rule, confidence=confidence, reason=reason)
