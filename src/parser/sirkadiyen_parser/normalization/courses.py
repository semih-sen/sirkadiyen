"""Course title normalization and course identity.

A lesson keeps two separate strings: the title a student reads, and a normalized
identity used for matching the same logical lesson across revisions. They are
deliberately different concepts, so a change to title punctuation or casing does
not create a duplicate lesson.

Identity is derived only from the title text. Anything the source did not say is
not invented here.
"""

import re
from dataclasses import dataclass

from sirkadiyen_parser.normalization.text import identity_key, normalize_text, text_lines

RULE_SINGLE_LINE = "singleLineTitle"
RULE_JOINED_LINES = "joinedLineTitle"
RULE_UNRESOLVED = "unresolvedTitle"

CONFIDENCE_SINGLE_LINE = 1.0
#: A title reassembled from several lines may have lost the author's intended
#: separation between title, topic and note.
CONFIDENCE_JOINED_LINES = 0.85

#: Separates lines that were joined back into one display title.
LINE_JOIN_SEPARATOR = " - "

_ORDINAL_PREFIX_PATTERN = re.compile(r"^\d{1,3}\s*[.)]\s+")


@dataclass(frozen=True, slots=True)
class CourseTitle:
    """A display title together with its normalized identity."""

    display_title: str
    course_identity: str | None
    rule: str
    confidence: float
    reason: str | None = None
    line_count: int = 0

    @property
    def resolved(self) -> bool:
        """Whether a usable title was produced."""
        return bool(self.display_title)


def normalize_course_title(value: str) -> CourseTitle:
    """Build a display title and course identity from raw cell text.

    Multi-line cells are joined rather than truncated, because a discarded line
    is discarded schedule information.
    """
    lines = text_lines(value)
    if not lines:
        return CourseTitle(
            display_title="",
            course_identity=None,
            rule=RULE_UNRESOLVED,
            confidence=0.0,
            reason="emptyTitle",
        )

    if len(lines) == 1:
        display_title = normalize_text(lines[0])
        rule, confidence = RULE_SINGLE_LINE, CONFIDENCE_SINGLE_LINE
    else:
        display_title = LINE_JOIN_SEPARATOR.join(lines)
        rule, confidence = RULE_JOINED_LINES, CONFIDENCE_JOINED_LINES

    return CourseTitle(
        display_title=display_title,
        course_identity=course_identity(display_title),
        rule=rule,
        confidence=confidence,
        line_count=len(lines),
    )


def course_identity(display_title: str) -> str | None:
    """Return the normalized identity of a course title.

    A leading list number such as ``1.`` is dropped because it orders the source
    row rather than naming the course. Row ordering is never lesson identity.
    """
    without_ordinal = _ORDINAL_PREFIX_PATTERN.sub("", normalize_text(display_title))
    key = identity_key(without_ordinal)
    return key or None
