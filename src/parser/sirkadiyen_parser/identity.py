"""Stable identity and content hashing for canonical candidates.

Identity and content are separate concepts. The stable identity answers "which
logical lesson is this?" and must survive a room change, an instructor change or
a reworded title. The content hash answers "did anything a student would see
change?" and must move whenever any published field moves.

Both are derived only from explicit component lists so a reader can see exactly
what was included, and both are hashed over a canonical JSON encoding so the
value is stable across Python versions and platforms.
"""

import hashlib
import json
from collections.abc import Iterable, Mapping

from sirkadiyen_parser.contracts.parsing import IdentityComponent

HASH_ALGORITHM = "sha256"
HASH_PREFIX = f"{HASH_ALGORITHM}:"


def _digest(payload: object) -> str:
    encoded = json.dumps(
        payload,
        ensure_ascii=False,
        sort_keys=False,
        separators=(",", ":"),
    ).encode("utf-8")
    return HASH_PREFIX + hashlib.sha256(encoded).hexdigest()


def build_identity_components(pairs: Iterable[tuple[str, str]]) -> list[IdentityComponent]:
    """Build identity components, preserving the order they were declared in."""
    return [IdentityComponent(name=name, value=value) for name, value in pairs]


def stable_identity(components: Iterable[IdentityComponent]) -> str:
    """Hash the ordered identity components into a stable lesson identity.

    Component order is part of the identity, so a profile must always declare
    its components in the same order.
    """
    return _digest([[component.name, component.value] for component in components])


def content_hash(fields: Mapping[str, str | None]) -> str:
    """Hash the published content of a candidate.

    Keys are sorted so a profile can add a field without reordering the rest,
    and ``None`` is encoded distinctly from an empty string because "no
    instructor" and "an empty instructor cell" are different source states.
    """
    return _digest([[name, fields[name]] for name in sorted(fields)])
