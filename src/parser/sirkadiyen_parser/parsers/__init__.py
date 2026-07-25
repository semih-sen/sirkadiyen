"""Parser profile implementations and the registry that selects them.

A profile is registered here only when it has a fixture-backed implementation.
A profile that is described in :mod:`sirkadiyen_parser.profiles` but absent from
this registry is a known, named source family that cannot be parsed yet, and the
service reports that explicitly instead of returning an empty result.
"""

from collections.abc import Callable

from sirkadiyen_parser.contracts.parsing import ParseSnapshotRequest, ParseSnapshotResponse
from sirkadiyen_parser.parsers.annual import parse_annual_snapshot
from sirkadiyen_parser.parsers.practice import parse_practice_snapshot
from sirkadiyen_parser.parsers.practice_slots import parse_practice_slot_snapshot
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
    ("grade2_practice_v1", "1.1.0"): parse_practice_slot_snapshot,
}


def get_parser(name: str, version: str) -> ParserImplementation | None:
    """Return the implementation for a profile version, if one is registered."""
    return _IMPLEMENTATIONS.get((name, version))


def implemented_profiles() -> tuple[tuple[str, str], ...]:
    """Return the registered profile name and version pairs, in a stable order."""
    return tuple(sorted(_IMPLEMENTATIONS))
