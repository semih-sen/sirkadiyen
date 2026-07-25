"""Rules shared by the Grade 2 rotation sources.

The Grade 2 practice table and the Grade 2 vertical-corridor calendar are
transposes of each other, so they need different readers (ADR-074). What they
must **not** have two of is the pair of rules that decide whether a session is
published at all and whose calendar it reaches:

- a dated slot that contradicts itself is refused, not corrected
- a cell names cohorts only within the eight letters these sources state

The second rule is why they are here rather than copied. Reading a run of
letters as a list of cohorts is safe only while the alphabet is bounded: without
the bound the same rule reads the word ``SINAV`` as the five cohorts S, I, N, A
and V, one of which is real. Two copies of that bound could drift apart, and the
drift would be invisible until a word reached the wrong students' calendars.
"""

import re

from sirkadiyen_parser.contracts.parsing import AudienceSelector
from sirkadiyen_parser.normalization.dates import DateResolution
from sirkadiyen_parser.normalization.groups import GroupExpression, parse_group_expression
from sirkadiyen_parser.normalization.text import normalize_text
from sirkadiyen_parser.parsers.practice import (
    DIMENSION_PRACTICE_GROUP,
    DIMENSION_PRACTICE_SUBGROUP,
)

REASON_UNRESOLVED_SLOT_DATE = "unresolvedSlotDate"
REASON_WEEKDAY_CONTRADICTS_DATE = "weekdayContradictsSlotDate"

#: The eight lettered cohorts both Grade 2 rotation sources state (ADR-048).
COHORT_LETTERS = "A-H"

#: This source family concatenates cohort letters (``ABCD``), so a run may be as
#: long as the cohort list itself. It is safe only together with
#: :data:`_GROUP_VALUE_PATTERN` below.
MAX_LETTER_RUN = 8

#: A group value is either a whole practice group (``A``) or one subgroup of it
#: (``A2``), exactly as in the Grade 1 rotation table (ADR-020).
_GROUP_VALUE_PATTERN = re.compile(rf"^([{COHORT_LETTERS}])(\d?)$")

#: A trailing ``1/3`` in a group cell numbers the session within its own series.
#: It is not part of the audience, and leaving it in would make the whole cell
#: unreadable rather than merely unlabelled.
SESSION_MARKER_PATTERN = re.compile(r"\s*\d+\s*/\s*\d+\s*$")


def read_cohort_groups(text: str) -> GroupExpression:
    """Read the audience a rotation cell states, ignoring its session number."""
    return parse_group_expression(
        SESSION_MARKER_PATTERN.sub("", normalize_text(text)),
        dimension=DIMENSION_PRACTICE_GROUP,
        letter_groups=True,
        max_letter_run=MAX_LETTER_RUN,
    )


def cohort_audience_selectors(expression: GroupExpression) -> list[AudienceSelector] | None:
    """Turn a resolved group expression into audience selectors.

    ``A`` selects a whole practice group and ``A2`` one subgroup of it, so the
    two are reported under different dimensions. A value of any other shape
    names a cohort these profiles do not model, and the caller refuses the cell
    rather than assigning it to the nearest dimension.
    """
    if expression.covers_all:
        return []

    selectors: list[AudienceSelector] = []
    for value in expression.values:
        match = _GROUP_VALUE_PATTERN.match(value)
        if match is None:
            return None
        dimension = DIMENSION_PRACTICE_SUBGROUP if match.group(2) else DIMENSION_PRACTICE_GROUP
        selectors.append(AudienceSelector(dimension=dimension, value=value))
    return selectors


def date_refusal(
    resolved: DateResolution,
    date_text: str,
    *,
    subject: str = "Slot header",
) -> tuple[str, str] | None:
    """Return why a slot date may not be published, or ``None`` when it may.

    A stated weekday that contradicts its own date is refused rather than
    published with a warning. The annual profiles publish such a row, because
    their dates are spreadsheet serials and the weekday text is a formatting
    artifact. These are typed by hand, the weekday is the only thing in the cell
    that can corroborate them, and both real sources prove the point: each
    contains dates whose year is a year out, and every one of them disagrees
    with its own weekday. Publishing them would put practices a year in the past
    on real calendars and quarantine the whole revision for a date outside the
    academic year; correcting the year would be inference.
    """
    if not resolved.resolved or resolved.value is None:
        return (
            REASON_UNRESOLVED_SLOT_DATE,
            f"{subject} date '{date_text}' could not be read as a date "
            f"({resolved.reason}), so nothing was published from it.",
        )

    if resolved.weekday_matches is False:
        return (
            REASON_WEEKDAY_CONTRADICTS_DATE,
            f"{subject} states '{date_text}', but {resolved.value.isoformat()} is not a "
            f"'{resolved.weekday_text}'. The cell contradicts itself, so nothing was "
            "published from it; correcting either part would be a guess.",
        )

    return None
