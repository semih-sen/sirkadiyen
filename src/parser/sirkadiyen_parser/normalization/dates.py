"""Date resolution.

Schedule dates arrive either as Google Sheets serial numbers or as human text in
Turkish or English. Resolution is explicit: every result records the rule that
produced it, and an unresolvable value stays unresolved with a reason.

Two behaviours are deliberately opt-in, because guessing them would silently
invent schedule data:

- a bare numeric cell is only read as a serial when the profile allows it
- a date without a year is only completed when the profile supplies a year
"""

import re
from dataclasses import dataclass
from datetime import date, timedelta

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
RULE_MONTH_NAME = "monthNameDate"
RULE_UNRESOLVED = "unresolvedDate"

CONFIDENCE_SERIAL = 1.0
CONFIDENCE_ISO = 1.0
CONFIDENCE_NUMERIC = 0.95
CONFIDENCE_MONTH_NAME = 0.9
#: A year supplied by profile configuration is weaker evidence than a year read
#: from the source cell.
CONFIDENCE_YEAR_FROM_PROFILE = 0.7

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


def resolve_date_text(value: str, *, year_hint: int | None = None) -> DateResolution:
    """Interpret human-readable date text.

    ``year_hint`` completes a date written without a year. It represents an
    explicit parser-profile rule, so the result is reported with reduced
    confidence and an explicit reason.
    """
    text = normalize_text(value)
    if not text:
        return unresolved_date("emptyDateText")

    weekday_text, weekday_index = _extract_weekday(text)
    remainder = _strip_weekday(text) if weekday_text is not None else text

    resolution = _resolve_date_body(remainder, year_hint=year_hint)
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
) -> DateResolution:
    """Interpret a cell as a schedule date.

    A numeric cell is read as a serial when its number format declares a date,
    or when ``allow_bare_serial`` records that the parser profile has confirmed
    the column holds serial dates. Otherwise the readable text is interpreted.
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
    return resolve_date_text(text, year_hint=year_hint)


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


def _resolve_date_body(text: str, *, year_hint: int | None) -> DateResolution:
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
        year_text = numeric_match.group(3)
        year, confidence, reason = _resolve_year(year_text, year_hint, CONFIDENCE_NUMERIC)
        if year is None:
            return unresolved_date(reason or "missingYear")
        return _build(
            year,
            int(numeric_match.group(2)),
            int(numeric_match.group(1)),
            rule=RULE_NUMERIC_DAY_FIRST,
            confidence=confidence,
            reason=reason,
        )

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
    try:
        value = date(year, month, day)
    except ValueError:
        return unresolved_date("impossibleCalendarDate")
    return DateResolution(value=value, rule=rule, confidence=confidence, reason=reason)
