"""Instructor extraction.

Source cells frequently mix a lesson topic and one or more instructors in one
cell, separated by line breaks or commas. Extraction is conservative: a segment
becomes an instructor only when it starts with a recognized Turkish or English
academic title. Everything else is returned as the remainder so the caller keeps
the text instead of losing it.
"""

import re
from dataclasses import dataclass

from sirkadiyen_parser.normalization.text import identity_key, normalize_text, text_lines

RULE_ACADEMIC_TITLE = "academicTitlePrefix"
RULE_NO_INSTRUCTOR = "noAcademicTitleFound"

CONFIDENCE_ACADEMIC_TITLE = 0.95
CONFIDENCE_NO_INSTRUCTOR = 0.0

#: First-token abbreviations that introduce an instructor name. Entries are
#: identity keys, so trailing dots and Turkish letters are already folded:
#: ``Öğr.`` matches as ``ogr`` and ``Doç.`` as ``doc``.
_TITLE_TOKEN_KEYS = frozenset(
    {
        "prof",
        "doc",
        "dr",
        "yrd",
        "ogr",
        "gor",
        "ars",
        "uzm",
        "op",
        "dt",
    }
)

_SEGMENT_SEPARATOR_PATTERN = re.compile(r"\s*[,;]\s*|\s+/\s+")
_LEADING_TOKEN_PATTERN = re.compile(r"^([^\s]+)")

#: Rejoins remainder segments that were split apart during extraction.
REMAINDER_JOIN_SEPARATOR = ", "


@dataclass(frozen=True, slots=True)
class InstructorExtraction:
    """Instructors found in a cell and the text left over."""

    instructors: tuple[str, ...]
    remainder: str
    rule: str
    confidence: float

    @property
    def resolved(self) -> bool:
        """Whether at least one instructor was recognized."""
        return bool(self.instructors)


def extract_instructors(value: str) -> InstructorExtraction:
    """Split cell text into instructor names and remaining content.

    Order is preserved for both collections, so the result is deterministic for
    a given input.
    """
    instructors: list[str] = []
    remainder_segments: list[str] = []

    for line in text_lines(value):
        for segment in _SEGMENT_SEPARATOR_PATTERN.split(line):
            candidate = normalize_text(segment)
            if not candidate:
                continue
            if _starts_with_academic_title(candidate):
                instructors.append(candidate)
            else:
                remainder_segments.append(candidate)

    if not instructors:
        return InstructorExtraction(
            instructors=(),
            remainder=REMAINDER_JOIN_SEPARATOR.join(remainder_segments),
            rule=RULE_NO_INSTRUCTOR,
            confidence=CONFIDENCE_NO_INSTRUCTOR,
        )

    return InstructorExtraction(
        instructors=tuple(instructors),
        remainder=REMAINDER_JOIN_SEPARATOR.join(remainder_segments),
        rule=RULE_ACADEMIC_TITLE,
        confidence=CONFIDENCE_ACADEMIC_TITLE,
    )


def _starts_with_academic_title(segment: str) -> bool:
    match = _LEADING_TOKEN_PATTERN.match(segment)
    if match is None:
        return False
    return identity_key(match.group(1)) in _TITLE_TOKEN_KEYS
