"""Golden-file regression tests for the Grade 1 annual profile.

Both sources that the catalog maps to ``grade1_yearly_v1`` are covered, because
the Turkish and English workbooks differ in header wording, worksheet count and
the shape of their data-entry mistakes. A change that fixes one and breaks the
other has to be visible.
"""

from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import ParseSnapshotResponse
from sirkadiyen_parser.parsers import get_parser
from sirkadiyen_parser.profiles import get_profile
from tests.support.golden import (
    assert_deterministic,
    assert_matches_golden,
    build_golden_document,
)
from tests.support.parse_requests import build_parse_request, build_response_projection

PROFILE_NAME = "grade1_yearly_v1"
PROFILE_VERSION = "1.0.0"

CASES = (
    ("real/g1-tr-annual.snapshot.json", "turkish", "parse/g1-tr-annual.json"),
    ("real/g1-en-annual.snapshot.json", "english", "parse/g1-en-annual.json"),
)


def run_profile(fixture: str, program_language: str) -> ParseSnapshotResponse:
    """Parse a fixture through the registered profile implementation."""
    profile = get_profile(PROFILE_NAME, PROFILE_VERSION)
    parser = get_parser(PROFILE_NAME, PROFILE_VERSION)
    assert profile is not None
    assert parser is not None, f"Profile '{PROFILE_NAME}' has no registered implementation."

    request = build_parse_request(
        fixture=fixture,
        profile_name=PROFILE_NAME,
        profile_version=PROFILE_VERSION,
        academic_year="2025-2026",
        class_year=1,
        program_language=program_language,
    )
    return parser(request, profile)


def build_document(fixture: str, program_language: str) -> dict[str, Any]:
    return build_golden_document(
        fixture=fixture,
        subject="parseResponse",
        payload=build_response_projection(run_profile(fixture, program_language)),
    )


@pytest.mark.parametrize(("fixture", "program_language", "golden"), CASES)
def test_annual_parse_matches_its_golden_file(
    fixture: str,
    program_language: str,
    golden: str,
) -> None:
    assert_matches_golden(golden, build_document(fixture, program_language))


@pytest.mark.parametrize(("fixture", "program_language", "golden"), CASES)
def test_annual_parse_is_deterministic(
    fixture: str,
    program_language: str,
    golden: str,
) -> None:
    assert_deterministic(lambda: build_document(fixture, program_language))
