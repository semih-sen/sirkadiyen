"""Group expression parsing.

Group labels appear as ``Grup 1``, ``GRUP 1-3``, ``G2``, ``A1``, ``1, 2, 3`` and
as phrases meaning every group. A group expression decides which students
receive a lesson, so a partially understood expression is treated as fully
unresolved: dropping one token would silently remove a whole cohort's lessons.
"""

import re
from dataclasses import dataclass

from sirkadiyen_parser.normalization.text import comparison_key, normalize_text

RULE_ALL_GROUPS = "allGroupsPhrase"
RULE_ENUMERATED = "enumeratedGroups"
RULE_LETTER_RUN = "letterRunGroups"
RULE_UNRESOLVED = "unresolvedGroupExpression"

CONFIDENCE_ALL_GROUPS = 1.0
CONFIDENCE_ENUMERATED = 1.0
#: A run such as ``AB`` is read as two lettered groups. That reading is a
#: property of the cohort model, not of the cell, so it scores below a value the
#: source spelled out.
CONFIDENCE_LETTER_RUN = 0.9

#: Phrases that mean "every group in scope" rather than a specific group.
_ALL_GROUP_KEYS = frozenset(
    {
        "tum gruplar",
        "tum grup",
        "tum gruplara",
        "butun gruplar",
        "hepsi",
        "genel",
        "all",
        "all groups",
        "whole class",
    }
)

#: Leading words that only introduce a group label and carry no value.
_GROUP_PREFIX_KEYS = frozenset({"grup", "gruplar", "group", "groups", "g"})

#: The same list without the single letter, for lettered cohorts where ``G``
#: names a group.
_WORD_PREFIX_KEYS = frozenset({"grup", "gruplar", "group", "groups"})

_TOKEN_SEPARATOR_PATTERN = re.compile(r"\s*[,;+/]\s*|\s+ve\s+|\s+and\s+")
# Group labels are short codes such as ``A``, ``B2`` or ``12``. Longer words are
# heading or course text, and longer digit runs are dates, serials or room
# numbers. Neither may be mistaken for a group, because a wrong group selects
# the wrong cohort's calendars.
_RANGE_PATTERN = re.compile(r"^([A-Za-z]{0,2})(\d{1,2})\s*[-–—]\s*([A-Za-z]{0,2})(\d{1,2})$")
_SIMPLE_PATTERN = re.compile(r"^([A-Za-z]{0,2})(\d{0,2})$")
_PREFIX_PATTERN = re.compile(r"^(grup|gruplar|group|groups|g)\s*\.?\s*", re.IGNORECASE)
_WORD_PREFIX_PATTERN = re.compile(r"^(gruplar|grup|groups|group)\s*\.?\s*", re.IGNORECASE)

#: Guards against an expression such as ``1-400`` expanding into nonsense.
MAX_RANGE_LENGTH = 40


@dataclass(frozen=True, slots=True)
class GroupExpression:
    """The outcome of interpreting a cell as a group audience expression."""

    dimension: str
    values: tuple[str, ...]
    covers_all: bool
    rule: str
    confidence: float
    reason: str | None = None
    unresolved_text: str | None = None

    @property
    def resolved(self) -> bool:
        """Whether the expression selects a known audience."""
        return self.covers_all or bool(self.values)


def parse_group_expression(
    value: str,
    *,
    dimension: str,
    letter_groups: bool = False,
) -> GroupExpression:
    """Interpret a group label for one audience dimension.

    ``dimension`` names the profile dimension being read, such as
    ``practiceGroup`` or ``anatomyGroup``. Anatomy and practice groups are
    separate dimensions and must never be parsed into one another.

    ``letter_groups`` selects the cohort model, because one letter cannot mean
    two things at once. With numbered cohorts, a leading ``G`` abbreviates the
    word *grup* and ``G2`` is group two. With lettered cohorts, ``G`` is group
    G, ``G2`` is subgroup two of group G, and a run such as ``AB`` names two
    groups. The source decides which model applies, so the parser profile
    states it rather than inferring it from the value.
    """
    text = normalize_text(value)
    if not text:
        return _unresolved(dimension, text, "emptyGroupExpression")

    if comparison_key(text) in _ALL_GROUP_KEYS:
        return GroupExpression(
            dimension=dimension,
            values=(),
            covers_all=True,
            rule=RULE_ALL_GROUPS,
            confidence=CONFIDENCE_ALL_GROUPS,
        )

    values: list[str] = []
    expanded_letter_run = False
    for token in _TOKEN_SEPARATOR_PATTERN.split(text):
        stripped = _strip_prefix(token, letter_groups=letter_groups)
        if not stripped:
            continue

        expanded = _expand_token(stripped, letter_groups=letter_groups)
        if expanded is None:
            return _unresolved(dimension, text, "unrecognizedGroupToken")
        expanded_letter_run = expanded_letter_run or len(expanded) > 1 and stripped.isalpha()
        values.extend(expanded)

    if not values:
        return _unresolved(dimension, text, "noGroupValueFound")

    return GroupExpression(
        dimension=dimension,
        values=_deduplicate(values),
        covers_all=False,
        rule=RULE_LETTER_RUN if expanded_letter_run else RULE_ENUMERATED,
        confidence=CONFIDENCE_LETTER_RUN if expanded_letter_run else CONFIDENCE_ENUMERATED,
    )


def _strip_prefix(token: str, *, letter_groups: bool) -> str:
    normalized = normalize_text(token)
    # With lettered cohorts a bare `G` is group G, so only the spelled-out words
    # introduce a label there.
    pattern = _WORD_PREFIX_PATTERN if letter_groups else _PREFIX_PATTERN
    stripped = pattern.sub("", normalized).strip(" .")
    prefixes = _WORD_PREFIX_KEYS if letter_groups else _GROUP_PREFIX_KEYS
    if comparison_key(stripped) in prefixes:
        return ""
    return stripped


def _expand_token(token: str, *, letter_groups: bool) -> tuple[str, ...] | None:
    range_match = _RANGE_PATTERN.match(token)
    if range_match is not None:
        return _expand_range(range_match)

    simple_match = _SIMPLE_PATTERN.match(token)
    if simple_match is None:
        return None

    letters, digits = simple_match.group(1), simple_match.group(2)
    if not letters and not digits:
        return None

    if letter_groups and len(letters) > 1 and not digits:
        return tuple(letter.upper() for letter in letters)

    return (f"{letters.upper()}{digits.lstrip('0') or digits}",)


def _expand_range(match: re.Match[str]) -> tuple[str, ...] | None:
    start_letters, start_digits, end_letters, end_digits = match.groups()
    if comparison_key(start_letters) != comparison_key(end_letters):
        return None

    start, end = int(start_digits), int(end_digits)
    if end < start or end - start + 1 > MAX_RANGE_LENGTH:
        return None

    prefix = start_letters.upper()
    return tuple(f"{prefix}{number}" for number in range(start, end + 1))


def _deduplicate(values: list[str]) -> tuple[str, ...]:
    seen: dict[str, None] = {}
    for value in values:
        seen.setdefault(value, None)
    return tuple(seen)


def _unresolved(dimension: str, text: str, reason: str) -> GroupExpression:
    return GroupExpression(
        dimension=dimension,
        values=(),
        covers_all=False,
        rule=RULE_UNRESOLVED,
        confidence=0.0,
        reason=reason,
        unresolved_text=text or None,
    )
