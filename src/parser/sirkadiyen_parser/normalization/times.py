"""Time and time-range resolution.

Lesson times arrive as day fractions or as text with inconsistent separators
(``09:00``, ``09.00``, ``09,00``) and inconsistent range dashes. Resolution
never repairs a range: an end that does not follow its start stays unresolved so
the profile can raise a warning instead of publishing an impossible lesson.
"""

import math
import re
from dataclasses import dataclass
from datetime import time

from sirkadiyen_parser.contracts.snapshot import NormalizedCell
from sirkadiyen_parser.normalization.grid import cell_display_text, cell_number
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text

MINUTES_PER_DAY = 24 * 60

RULE_FRACTION = "dayFractionTime"
RULE_TEXT = "textTime"
RULE_COMPACT_TEXT = "compactTextTime"
RULE_TEXT_RANGE = "textTimeRange"
RULE_UNRESOLVED = "unresolvedTime"

#: A cell that states a time of day, but whose number is not one. A source typed
#: ``9`` into a time-formatted cell meaning nine in the morning; as a spreadsheet
#: value that is nine whole days and no time at all, and the spreadsheet itself
#: shows it as ``00:00``. Reading only the fractional part would publish a lesson
#: at midnight, which is exactly the silent misreading ADR-051 forbids.
REASON_NOT_A_DAY_FRACTION = "numericTimeOutsideDayFraction"

CONFIDENCE_FRACTION = 1.0
CONFIDENCE_TEXT = 1.0
#: A four-digit block is only a time by profile agreement; ``2025`` is equally a
#: year. The score records that weaker evidence.
CONFIDENCE_COMPACT_TEXT = 0.8

_FORMAT_SEPARATOR_PATTERN = re.compile(r"[ _-]+")
_TIME_PATTERN = re.compile(r"^(\d{1,2})[:.,](\d{2})$")
_COMPACT_PATTERN = re.compile(r"^(\d{1,2})(\d{2})$")
_RANGE_SEPARATOR_PATTERN = re.compile(r"\s*[-–—~]\s*|\s+/\s*|\s*/\s+")


@dataclass(frozen=True, slots=True)
class TimeResolution:
    """The outcome of interpreting a value as a local time."""

    value: time | None
    rule: str
    confidence: float
    reason: str | None = None

    @property
    def resolved(self) -> bool:
        """Whether a time was produced."""
        return self.value is not None


@dataclass(frozen=True, slots=True)
class TimeRangeResolution:
    """The outcome of interpreting a value as a local start and end time."""

    start: time | None
    end: time | None
    rule: str
    confidence: float
    reason: str | None = None

    @property
    def resolved(self) -> bool:
        """Whether both ends of the range were produced."""
        return self.start is not None and self.end is not None


def unresolved_time(reason: str) -> TimeResolution:
    """Build an unresolved time carrying an explicit reason code."""
    return TimeResolution(value=None, rule=RULE_UNRESOLVED, confidence=0.0, reason=reason)


def unresolved_time_range(reason: str) -> TimeRangeResolution:
    """Build an unresolved time range carrying an explicit reason code."""
    return TimeRangeResolution(
        start=None, end=None, rule=RULE_UNRESOLVED, confidence=0.0, reason=reason
    )


def time_from_fraction(fraction: float) -> time | None:
    """Convert a spreadsheet day fraction to a local time.

    The fraction is rounded half-up to the nearest minute, because stored
    fractions carry float representation error. A whole-day serial with a time
    component may be passed: only the fractional part is used.
    """
    day_fraction = fraction - math.floor(fraction)
    minutes = math.floor(day_fraction * MINUTES_PER_DAY + 0.5)
    if minutes >= MINUTES_PER_DAY:
        return None
    return time(hour=minutes // 60, minute=minutes % 60)


def resolve_time_text(value: str, *, allow_compact: bool = False) -> TimeResolution:
    """Interpret human-readable time text.

    ``allow_compact`` enables the ``0900`` form. It is opt-in because a bare
    four-digit block is indistinguishable from a year.
    """
    text = normalize_text(value)
    if not text:
        return unresolved_time("emptyTimeText")

    match = _TIME_PATTERN.match(text)
    if match is not None:
        return _build_time(
            int(match.group(1)), int(match.group(2)), rule=RULE_TEXT, confidence=CONFIDENCE_TEXT
        )

    if allow_compact:
        compact = _COMPACT_PATTERN.match(text)
        if compact is not None:
            return _build_time(
                int(compact.group(1)),
                int(compact.group(2)),
                rule=RULE_COMPACT_TEXT,
                confidence=CONFIDENCE_COMPACT_TEXT,
            )

    return unresolved_time("unrecognizedTimeFormat")


def resolve_time_range_text(value: str, *, allow_compact: bool = False) -> TimeRangeResolution:
    """Interpret text holding a start and end time separated by a dash or slash."""
    text = normalize_text(value)
    if not text:
        return unresolved_time_range("emptyTimeRangeText")

    parts = [part for part in _RANGE_SEPARATOR_PATTERN.split(text) if part]
    if len(parts) != 2:
        return unresolved_time_range("unrecognizedTimeRangeFormat")

    start = resolve_time_text(parts[0], allow_compact=allow_compact)
    end = resolve_time_text(parts[1], allow_compact=allow_compact)
    if start.value is None or end.value is None:
        return unresolved_time_range("unrecognizedTimeRangeFormat")
    if end.value <= start.value:
        return unresolved_time_range("endTimeNotAfterStartTime")

    return TimeRangeResolution(
        start=start.value,
        end=end.value,
        rule=RULE_TEXT_RANGE,
        confidence=min(start.confidence, end.confidence),
    )


def resolve_cell_time(
    cell: NormalizedCell | None,
    *,
    allow_bare_fraction: bool = False,
    allow_compact: bool = False,
) -> TimeResolution:
    """Interpret a cell as a local time.

    A numeric cell is read as a day fraction when its number format declares a
    time, or when ``allow_bare_fraction`` records that the parser profile has
    confirmed the column holds fractions.

    Only a cell whose format declares a full timestamp may carry a whole-day
    part. A cell that declares a time of day, or a column the profile confirmed
    holds fractions, must hold a value in ``[0, 1)``; anything else states no
    time this resolver can read and is refused rather than truncated.
    """
    if cell is None:
        return unresolved_time("missingCell")

    number = cell_number(cell)
    timestamp = _declares_timestamp_format(cell)
    if number is not None and (allow_bare_fraction or timestamp or _declares_time_format(cell)):
        if not timestamp and not 0.0 <= number < 1.0:
            return unresolved_time(REASON_NOT_A_DAY_FRACTION)
        resolved = time_from_fraction(number)
        if resolved is None:
            return unresolved_time("fractionOutOfSupportedRange")
        return TimeResolution(value=resolved, rule=RULE_FRACTION, confidence=CONFIDENCE_FRACTION)

    text = cell_display_text(cell)
    if text is None:
        return unresolved_time("emptyCell")
    return resolve_time_text(text, allow_compact=allow_compact)


def resolve_cell_time_range(
    cell: NormalizedCell | None,
    *,
    allow_compact: bool = False,
) -> TimeRangeResolution:
    """Interpret a cell as a local time range.

    A single fraction cannot express a range, so only readable text is used.
    """
    if cell is None:
        return unresolved_time_range("missingCell")

    text = cell_display_text(cell)
    if text is None:
        return unresolved_time_range("emptyCell")
    return resolve_time_range_text(text, allow_compact=allow_compact)


def duration_minutes(start: time, end: time) -> int:
    """Return the whole-minute duration between two same-day local times."""
    return (end.hour * 60 + end.minute) - (start.hour * 60 + start.minute)


def _declares_time_format(cell: NormalizedCell) -> bool:
    """Whether the cell's format declares a time of day."""
    return _number_format(cell) == "time"


def _declares_timestamp_format(cell: NormalizedCell) -> bool:
    """Whether the cell's format declares a date and a time together.

    A timestamp legitimately carries a whole-day serial part, so only this shape
    may be reduced to its fractional part.
    """
    return _number_format(cell) == "datetime"


def _number_format(cell: NormalizedCell) -> str | None:
    """The cell's declared number format, folded to a separator-free key.

    Producers spell the same format differently — the Sheets API returns
    ``DATE_TIME`` and another converter may return ``DateTime`` — so the
    separators are removed rather than compared literally.
    """
    number_format = cell.effective_format.number_format_type if cell.effective_format else None
    if number_format is None:
        return None
    return _FORMAT_SEPARATOR_PATTERN.sub("", comparison_key(number_format))


def _build_time(hour: int, minute: int, *, rule: str, confidence: float) -> TimeResolution:
    if hour > 23 or minute > 59:
        return unresolved_time("impossibleClockTime")
    return TimeResolution(value=time(hour=hour, minute=minute), rule=rule, confidence=confidence)
