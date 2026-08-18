"""Curriculum block and academic department resolution.

The annual sources state both facts in one cell, and its header names the
convention: ``DİLİM ADI / ANABİLİM DALI`` — the curriculum block, then the
owning department. The English workbook writes the same cell under a generic
``Description`` header, with Turkish department names.

Splitting on the slash alone would fabricate departments, because the sources
also write a second curriculum block, a nested block name or a whole faculty
after it: ``YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ / DİKEY KORİDOR`` names no
department at all. A segment therefore becomes a department only when it carries
an explicit department marker — ``AD.``, ``A.D.`` or ``ANABİLİM DALI`` —
optionally followed by a sub-department in parentheses, as in
``İÇ HASTALIKLARI AD. (ENDOKRİNOLOJİ BD.)``. The sub-department is kept: it is
part of what the source stated. Every segment that carries no marker is reported
back to the caller instead of being guessed at.

One cell may name several departments. An integrated session ("entegre oturum")
is taught by two or three of them and the source writes them as one dashed list,
as in ``BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD. - İÇ HASTALIKLARI HEMATOLOJİ``. All
of them are kept, because a student has to see every department teaching the
session. Inside a segment that carries at least one marker the source is
enumerating departments, so an unmarked member of *that* list is a department
too; it is kept at reduced confidence. That is a rule about the list the source
wrote, not an inference about the words in it.

Nothing here decides how a department is used. Whether a value is comparable
enough for semantic matching is the diff engine's decision (ADR-035); this
module only reports what the cell says.
"""

import re
from dataclasses import dataclass

from sirkadiyen_parser.normalization.text import comparison_key, normalize_text

RULE_EMPTY = "emptyBlockDepartmentCell"
RULE_BLOCK_ONLY = "blockWithoutStatedDepartment"
RULE_MARKED_DEPARTMENT = "markedDepartmentSegment"
RULE_DEPARTMENT_LIST_MEMBER = "unmarkedDepartmentListMember"

CONFIDENCE_MARKED_DEPARTMENT = 1.0

#: A department accepted only because it sits in a list whose other members are
#: marked. The value is verbatim source text, but the reason it counts as a
#: department comes from its neighbours, so it is reported as less certain.
CONFIDENCE_DEPARTMENT_LIST_MEMBER = 0.9

#: Segments are separated by a slash, written with or without surrounding
#: spaces. Both forms appear in the same workbook.
_SEGMENT_SEPARATOR_PATTERN = re.compile(r"\s*/\s*")

#: Members of a department list are separated by a spaced dash. The dash must be
#: surrounded by spaces, because ``HAREKET-1 DİLİMİ`` is one name.
_LIST_SEPARATOR_PATTERN = re.compile(r"\s+[-–—]\s+")

#: Matches an explicit department marker at the end of a segment, on the folded
#: comparison key. A trailing parenthesis is allowed so that a stated
#: sub-department does not hide the marker in front of it.
_MARKER_PATTERN = re.compile(r"(?:^|\s)(?:a\.?\s?d\.?|anabilim\s+dali)\s*(?:\([^)]*\))?$")


@dataclass(frozen=True, slots=True)
class BlockDepartmentResolution:
    """What one block/department cell stated, and what it did not."""

    curriculum_block: str | None
    departments: tuple[str, ...]

    #: Segments that named neither the block nor a marked department. They are
    #: carried out of the resolver so the caller can account for them rather
    #: than dropping part of a cell silently.
    unmarked_segments: tuple[str, ...]

    rule: str
    confidence: float

    @property
    def resolved(self) -> bool:
        """Whether at least one department was stated explicitly enough to keep."""
        return bool(self.departments)

    @property
    def names_several_departments(self) -> bool:
        """Whether the cell names an integrated session's departments."""
        return len(self.departments) > 1


def resolve_block_and_departments(value: str | None) -> BlockDepartmentResolution:
    """Read the curriculum block and academic departments from one cell.

    The first segment is the curriculum block, which is how the sources are
    written and what the Turkish header declares. A first segment that carries a
    department marker is taken as a department instead, so a cell that names only
    a department does not become a block named after one.

    Order is preserved and duplicates are removed, so the result is deterministic
    for a given cell.
    """
    text = normalize_text(value or "")
    if not text:
        return BlockDepartmentResolution(
            curriculum_block=None,
            departments=(),
            unmarked_segments=(),
            rule=RULE_EMPTY,
            confidence=CONFIDENCE_MARKED_DEPARTMENT,
        )

    segments = [segment for segment in _SEGMENT_SEPARATOR_PATTERN.split(text) if segment.strip()]

    curriculum_block: str | None = None
    remaining = segments
    if segments and not _has_department_marker(segments[0]):
        curriculum_block = segments[0]
        remaining = segments[1:]

    departments: list[str] = []
    seen: set[str] = set()
    unmarked: list[str] = []
    borrowed_marker = False

    for segment in remaining:
        members = [member for member in _LIST_SEPARATOR_PATTERN.split(segment) if member.strip()]
        if not any(_has_department_marker(member) for member in members):
            unmarked.append(segment)
            continue

        for member in members:
            if not _has_department_marker(member):
                borrowed_marker = True
            key = comparison_key(member)
            if key in seen:
                continue
            seen.add(key)
            departments.append(member)

    return BlockDepartmentResolution(
        curriculum_block=curriculum_block,
        departments=tuple(departments),
        unmarked_segments=tuple(unmarked),
        rule=_rule_for(departments, borrowed_marker),
        confidence=(
            CONFIDENCE_DEPARTMENT_LIST_MEMBER if borrowed_marker else CONFIDENCE_MARKED_DEPARTMENT
        ),
    )


def _rule_for(departments: list[str], borrowed_marker: bool) -> str:
    if not departments:
        return RULE_BLOCK_ONLY
    return RULE_DEPARTMENT_LIST_MEMBER if borrowed_marker else RULE_MARKED_DEPARTMENT


def _has_department_marker(segment: str) -> bool:
    """Whether a segment ends with an explicit academic-department marker."""
    return _MARKER_PATTERN.search(comparison_key(segment)) is not None


#: How the Grade 3 sources name the department each half of the class sits with,
#: written inside the lesson title rather than in the block cell:
#: ``Hasta Başı Uygulama-1 A Grubu (İç H.) B Grubu (ÇSvH)``. The English workbook
#: writes the same construction in the same Turkish wording.
#:
#: The group letter has to stand alone. A Grade 1 lesson is titled
#: ``... A-B GRUBU (İngilizce Tıp ile Ortak Ders)``, which addresses both groups
#: at once and whose parenthesis holds a note rather than a department; without
#: the lookbehinds its ``B`` would read as a group and that note as its
#: department.
_GROUP_DEPARTMENT_PATTERN = re.compile(
    r"(?<![^\W\d_])(?<!-)(?P<letter>[A-Za-z])\s*grubu\s*\(\s*(?P<department>[^()]+?)\s*\)",
    re.IGNORECASE,
)


def resolve_group_departments(title: str | None) -> tuple[tuple[str, str], ...]:
    """The department a title states for each curriculum group, in source order.

    A bedside session sits one half of the class with one department and the
    other half with another, and the annual workbook states both in the title of
    the single row it writes for the session. The block cell of those rows names
    the curriculum block only, so this is the only place the department is said.

    Nothing here decides which of the pairs applies to a record; that follows
    from the audience the row was published to, and is the caller's to make.
    Returning the pairs in source order keeps that decision explainable.

    A group named twice keeps its first department: a title that contradicts
    itself is not resolved here into whichever came last.

    The construction is only recognised when the title states it for more than
    one group, which is how both sources write it — one row carries the session
    both halves attend, and names the department of each. A lone
    ``X Grubu (...)`` is left alone: the parenthesis after a single group is a
    note as often as it is a department, and a wrong department is worse on a
    calendar than no department.
    """
    if not title:
        return ()

    pairs: list[tuple[str, str]] = []
    seen: set[str] = set()
    for match in _GROUP_DEPARTMENT_PATTERN.finditer(title):
        letter = match.group("letter").upper()
        department = normalize_text(match.group("department"))
        if not department or letter in seen:
            continue
        seen.add(letter)
        pairs.append((letter, department))

    return tuple(pairs) if len(pairs) > 1 else ()
