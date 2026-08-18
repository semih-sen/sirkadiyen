"""Parser profile implementations and the registry that selects them.

A profile is registered here only when it has a fixture-backed implementation.
A profile that is described in :mod:`sirkadiyen_parser.profiles` but absent from
this registry is a known, named source family that cannot be parsed yet, and the
service reports that explicitly instead of returning an empty result.
"""

from collections.abc import Callable

from sirkadiyen_parser.contracts.parsing import ParseSnapshotRequest, ParseSnapshotResponse
from sirkadiyen_parser.parsers.anatomy import parse_anatomy_snapshot
from sirkadiyen_parser.parsers.annual import parse_annual_snapshot
from sirkadiyen_parser.parsers.bedside import parse_bedside_snapshot
from sirkadiyen_parser.parsers.faculty_practice import parse_faculty_practice_snapshot
from sirkadiyen_parser.parsers.practice import parse_practice_snapshot
from sirkadiyen_parser.parsers.practice_slots import parse_practice_slot_snapshot
from sirkadiyen_parser.parsers.vertical_corridor import parse_vertical_corridor_snapshot
from sirkadiyen_parser.profiles import ParserProfileDefinition

ParserImplementation = Callable[
    [ParseSnapshotRequest, ParserProfileDefinition],
    ParseSnapshotResponse,
]

_IMPLEMENTATIONS: dict[tuple[str, str], ParserImplementation] = {
    ("grade1_yearly_v1", "1.5.0"): parse_annual_snapshot,
    ("grade1_practice_v1", "1.0.0"): parse_practice_snapshot,
    # The Grade 2 annual workbooks are the same row-oriented layout as Grade 1 in
    # both languages, so they share the implementation and differ only in what the
    # profile definition declares (ADR-073).
    ("grade2_yearly_v1", "1.0.0"): parse_annual_snapshot,
    # The Grade 2 practice table is a different rotation layout, not a variant of
    # the Grade 1 one, so it has its own implementation (ADR-074). Only the
    # Turkish source is registered: the committed English fixture is from the
    # previous academic year.
    ("grade2_practice_v1", "1.2.0"): parse_practice_slot_snapshot,
    # The skill-practice calendar the annual and practice profiles both defer to.
    # It is published as a Word document and reaches the parser on the same
    # normalized snapshot contract as a workbook (ADR-076, ADR-077).
    ("grade2_vertical_corridor_v1", "1.0.0"): parse_vertical_corridor_snapshot,
    # The two anatomy group lists are one document per semester with one layout,
    # so they share an implementation the way the Grade 2 annual profile serves
    # both languages. The profiles stay separate because the sources are
    # separate: each states its own semester's dates (ADR-078).
    ("grade2_anatomy_autumn_v1", "1.0.0"): parse_anatomy_snapshot,
    ("grade2_anatomy_spring_v1", "1.0.0"): parse_anatomy_snapshot,
    # The Grade 3 annual workbooks are the same row-oriented layout again, in both
    # languages and for both curriculum groups. What they add is an audience: the
    # class is split in two, so the profile declares `curriculumGroup` and the
    # shared implementation reads it from the term cell (ADR-098).
    ("grade3_yearly_v1", "1.1.0"): parse_annual_snapshot,
    # The rotation those workbooks defer their `Öğretim üyesi Uygulama` rows to.
    # One implementation serves both curriculum groups: the workbooks differ only
    # in their cohort letter and in the order they write their blocks (ADR-099).
    ("grade3_faculty_practice_v1", "1.0.0"): parse_faculty_practice_snapshot,
    # Registered even though it publishes nothing. The bedside document states no
    # per-session time, so the annual profile keeps those events and this one
    # supplies their topics (ADR-100); reading it here is what proves the reader
    # the annual profile calls, and what accounts for the document in the metrics.
    ("grade3_bedside_v1", "1.0.0"): parse_bedside_snapshot,
}


def get_parser(name: str, version: str) -> ParserImplementation | None:
    """Return the implementation for a profile version, if one is registered."""
    return _IMPLEMENTATIONS.get((name, version))


def implemented_profiles() -> tuple[tuple[str, str], ...]:
    """Return the registered profile name and version pairs, in a stable order."""
    return tuple(sorted(_IMPLEMENTATIONS))
