"""Golden-file regression tests for the implemented parser profiles.

Both sources of each annual profile are covered, because the Turkish and English
workbooks differ in header wording, worksheet count and the shape of their
data-entry mistakes. A change that fixes one and breaks the other has to be
visible. The class year comes from the case rather than from a constant: the two
annual profiles share an implementation, and reading a Grade 2 workbook as
Grade 1 would silently drop every row.
"""

from typing import Any

import pytest

from sirkadiyen_parser.contracts.parsing import ParseSnapshotResponse
from sirkadiyen_parser.parsers import get_parser, implemented_profiles
from sirkadiyen_parser.profiles import get_profile
from tests.support.golden import (
    assert_deterministic,
    assert_matches_golden,
    build_golden_document,
)
from tests.support.parse_requests import build_parse_request, build_response_projection


def _registered_version(profile_name: str) -> str:
    """The single registered version for a profile, so cases need not repeat it.

    Profiles are versioned independently (a behaviour change bumps only its own
    profile, e.g. grade1_yearly_v1 to 1.5.0), so the version is read
    from the registry rather than shared across every case.
    """
    versions = [version for (name, version) in implemented_profiles() if name == profile_name]
    assert len(versions) == 1, f"Expected exactly one registered version for '{profile_name}'."
    return versions[0]


CASES = (
    (
        "grade1_yearly_v1",
        "real/g1-tr-annual.snapshot.json",
        1,
        "turkish",
        "parse/g1-tr-annual.json",
    ),
    (
        "grade1_yearly_v1",
        "real/g1-en-annual.snapshot.json",
        1,
        "english",
        "parse/g1-en-annual.json",
    ),
    (
        "grade1_practice_v1",
        "real/g1-tr-practice.snapshot.json",
        1,
        "turkish",
        "parse/g1-tr-practice.json",
    ),
    (
        "grade2_yearly_v1",
        "real/g2-tr-annual.snapshot.json",
        2,
        "turkish",
        "parse/g2-tr-annual.json",
    ),
    (
        "grade2_yearly_v1",
        "real/g2-en-annual.snapshot.json",
        2,
        "english",
        "parse/g2-en-annual.json",
    ),
    (
        "grade2_practice_v1",
        "real/g2-tr-practice.snapshot.json",
        2,
        "turkish",
        "parse/g2-tr-practice.json",
    ),
    # Both semesters, because the same programme is written as one 60-row table
    # in autumn and as seven tables in spring, and only spring uses subgroups.
    (
        "grade2_vertical_corridor_v1",
        "real/g2-vertical-autumn.snapshot.json",
        2,
        "turkish",
        "parse/g2-vertical-autumn.json",
    ),
    (
        "grade2_vertical_corridor_v1",
        "real/g2-vertical-spring.snapshot.json",
        2,
        "turkish",
        "parse/g2-vertical-spring.json",
    ),
)

CASE_FIELDS = ("profile_name", "fixture", "class_year", "program_language", "golden")


def run_profile(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
) -> ParseSnapshotResponse:
    """Parse a fixture through the registered profile implementation."""
    version = _registered_version(profile_name)
    profile = get_profile(profile_name, version)
    parser = get_parser(profile_name, version)
    assert profile is not None
    assert parser is not None, f"Profile '{profile_name}' has no registered implementation."

    request = build_parse_request(
        fixture=fixture,
        profile_name=profile_name,
        profile_version=version,
        academic_year="2025-2026",
        class_year=class_year,
        program_language=program_language,
    )
    return parser(request, profile)


def build_document(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
) -> dict[str, Any]:
    return build_golden_document(
        fixture=fixture,
        subject="parseResponse",
        payload=build_response_projection(
            run_profile(profile_name, fixture, class_year, program_language)
        ),
    )


@pytest.mark.parametrize(CASE_FIELDS, CASES)
def test_parse_matches_its_golden_file(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    golden: str,
) -> None:
    assert_matches_golden(
        golden,
        build_document(profile_name, fixture, class_year, program_language),
    )


@pytest.mark.parametrize(CASE_FIELDS, CASES)
def test_parse_is_deterministic(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    golden: str,
) -> None:
    assert_deterministic(
        lambda: build_document(profile_name, fixture, class_year, program_language)
    )
