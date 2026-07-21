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

#: Separates a lesson title from the instructors written after it. Spacing
#: around the slash is inconsistent in the sources, so it is not required. A
#: dash only separates when it is surrounded by spaces, because a dash inside a
#: word or after a list number belongs to the title.
_TRAILING_SEPARATOR_PATTERN = re.compile(r"\s*/\s*|\s+[-–—]\s+")

#: Rejoins remainder segments that were split apart during extraction.
REMAINDER_JOIN_SEPARATOR = ", "

RULE_TRAILING_INSTRUCTORS = "trailingInstructorSegments"

CONFIDENCE_TRAILING_INSTRUCTORS = 0.95


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
            if starts_with_academic_title(candidate):
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


@dataclass(frozen=True, slots=True)
class TitleInstructorSplit:
    """A lesson title and the instructors written after it in the same cell."""

    title: str
    instructors: tuple[str, ...]
    rule: str
    confidence: float

    @property
    def resolved(self) -> bool:
        """Whether trailing instructors were separated from the title."""
        return bool(self.instructors)


def split_trailing_instructors(value: str) -> TitleInstructorSplit:
    """Separate trailing instructor segments from a combined title cell.

    Sources write ``<title> / <instructor>`` and sometimes list several
    instructors. The split point is the earliest separator whose whole tail is
    made of instructor segments, so an instructor is never left inside the title
    and a title containing a slash is never truncated.

    The title is returned as the source wrote it, minus surrounding whitespace.
    Nothing is reordered or reformatted, because the title is what a student
    reads in the calendar.
    """
    text = normalize_text(value)
    if not text:
        return TitleInstructorSplit(
            title="",
            instructors=(),
            rule=RULE_NO_INSTRUCTOR,
            confidence=CONFIDENCE_NO_INSTRUCTOR,
        )

    for match in _TRAILING_SEPARATOR_PATTERN.finditer(text):
        head = text[: match.start()].strip()
        tail = text[match.end() :].strip()
        if not head or not tail:
            continue

        segments = [
            segment.strip()
            for segment in _TRAILING_SEPARATOR_PATTERN.split(tail)
            if segment.strip()
        ]
        if segments and all(starts_with_academic_title(segment) for segment in segments):
            return TitleInstructorSplit(
                title=head,
                instructors=tuple(segments),
                rule=RULE_TRAILING_INSTRUCTORS,
                confidence=CONFIDENCE_TRAILING_INSTRUCTORS,
            )

    return TitleInstructorSplit(
        title=text,
        instructors=(),
        rule=RULE_NO_INSTRUCTOR,
        confidence=CONFIDENCE_NO_INSTRUCTOR,
    )


def starts_with_academic_title(segment: str) -> bool:
    """Whether a text segment begins with a recognized academic title.

    Sources write titles both spaced and run together: ``Prof. Dr. Ayşe`` and
    ``Prof.Dr. Ayşe`` name the same person. Only the first word of the leading
    token is compared, so ``Prof.Dr.`` matches on ``prof`` while an ordinary
    word that merely starts with the same letters does not, because the
    identity key keeps whole words separate.
    """
    match = _LEADING_TOKEN_PATTERN.match(segment)
    if match is None:
        return False
    leading_word = identity_key(match.group(1)).split("-")[0]
    return leading_word in _TITLE_TOKEN_KEYS
