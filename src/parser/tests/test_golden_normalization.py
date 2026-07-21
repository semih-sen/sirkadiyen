"""Golden-file regression test for the shared normalization primitives.

No parser profile is implemented yet, so the golden subject is the normalization
trace rather than a parse response. The same harness will compare parse
responses once the first fixture-backed profile exists.
"""

from typing import Any

from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot
from tests.support.golden import (
    assert_deterministic,
    assert_matches_golden,
    build_golden_document,
    load_fixture_json,
)
from tests.support.trace import build_normalization_trace

FIXTURE = "synthetic/annual-normalization-sample.json"
GOLDEN = "normalization/synthetic-annual.json"


def load_snapshot() -> NormalizedSpreadsheetSnapshot:
    return NormalizedSpreadsheetSnapshot.model_validate(load_fixture_json(FIXTURE))


def build_document() -> dict[str, Any]:
    return build_golden_document(
        fixture=FIXTURE,
        subject="normalizationTrace",
        payload=build_normalization_trace(load_snapshot()),
    )


def test_normalization_trace_matches_its_golden_file() -> None:
    assert_matches_golden(GOLDEN, build_document())


def test_normalization_trace_is_deterministic() -> None:
    assert_deterministic(build_document)
