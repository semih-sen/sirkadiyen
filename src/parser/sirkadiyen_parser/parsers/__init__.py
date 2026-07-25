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
from sirkadiyen_parser.profiles import ParserProfileDefinition

ParserImplementation = Callable[
    [ParseSnapshotRequest, ParserProfileDefinition],
    ParseSnapshotResponse,
]

_IMPLEMENTATIONS: dict[tuple[str, str], ParserImplementation] = {
    ("grade1_yearly_v1", "1.3.0"): parse_annual_snapshot,
    ("grade1_practice_v1", "1.0.0"): parse_practice_snapshot,
}


def get_parser(name: str, version: str) -> ParserImplementation | None:
    """Return the implementation for a profile version, if one is registered."""
    return _IMPLEMENTATIONS.get((name, version))


def implemented_profiles() -> tuple[tuple[str, str], ...]:
    """Return the registered profile name and version pairs, in a stable order."""
    return tuple(sorted(_IMPLEMENTATIONS))
