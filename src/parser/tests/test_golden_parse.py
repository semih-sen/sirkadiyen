"""Golden-file regression tests for the implemented parser profiles.

Both sources of each annual profile are covered, because the Turkish and English
workbooks differ in header wording, worksheet count and the shape of their
data-entry mistakes. A change that fixes one and breaks the other has to be
visible. The class year comes from the case rather than from a constant: the
three annual profiles share an implementation, and reading a Grade 2 workbook as
Grade 1 would silently drop every row.

The academic year is per case for the same reason it is per source: the faculty
published the 2026-2027 Grade 3 documents while Grades 1 and 2 were still on
2025-2026, and the year is source context the workbook never states (ADR-017).

Companion snapshots are part of a case because they are part of the input. The
Grade 3 Turkish annual is covered twice — once with its bedside companion and
once without — so the file proves both that the topics arrive and that the
schedule is identical when they do not (ADR-102).
"""

from collections.abc import Mapping
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


#: The year Grades 1 and 2 were captured for.
_Y2025 = "2025-2026"

#: The year the Grade 3 rollover captured (ADR-103).
_Y2026 = "2026-2027"

#: No companion evidence, which is the case for every source but two.
_ALONE: tuple[str, ...] = ()

#: The source narrows nothing, which is the case for every source but the two
#: Grade 3 Turkish annual workbooks (ADR-110).
_UNNARROWED: dict[str, list[str]] = {}

#: Each Grade 3 Turkish workbook publishes only its own half of the class. Both
#: state the sessions both halves attend, in different wordings, so without this
#: a student receives every shared session twice (ADR-110).
_OWNS_3A = {"curriculumGroup": ["3-A"]}
_OWNS_3B = {"curriculumGroup": ["3-B"]}

CASES = (
    (
        "grade1_yearly_v1",
        "real/g1-tr-annual.snapshot.json",
        1,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g1-tr-annual.json",
    ),
    (
        "grade1_yearly_v1",
        "real/g1-en-annual.snapshot.json",
        1,
        "english",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g1-en-annual.json",
    ),
    (
        "grade1_practice_v1",
        "real/g1-tr-practice.snapshot.json",
        1,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g1-tr-practice.json",
    ),
    (
        "grade2_yearly_v1",
        "real/g2-tr-annual.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-tr-annual.json",
    ),
    (
        "grade2_yearly_v1",
        "real/g2-en-annual.snapshot.json",
        2,
        "english",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-en-annual.json",
    ),
    (
        "grade2_practice_v1",
        "real/g2-tr-practice.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-tr-practice.json",
    ),
    (
        "grade2_practice_v1",
        "real/g2-en-practice.snapshot.json",
        2,
        "english",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-en-practice.json",
    ),
    # Both semesters, because the same programme is written as one 60-row table
    # in autumn and as seven tables in spring, and only spring uses subgroups.
    (
        "grade2_vertical_corridor_v1",
        "real/g2-vertical-autumn.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-vertical-autumn.json",
    ),
    (
        "grade2_vertical_corridor_v1",
        "real/g2-vertical-spring.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-vertical-spring.json",
    ),
    # Both semesters again, because the autumn document writes most of its days
    # without a merge and the spring one carries a date whose year is a year out.
    (
        "grade2_anatomy_autumn_v1",
        "real/g2-anatomy-autumn.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-anatomy-autumn.json",
    ),
    (
        "grade2_anatomy_spring_v1",
        "real/g2-anatomy-spring.snapshot.json",
        2,
        "turkish",
        _Y2025,
        _ALONE,
        _UNNARROWED,
        "parse/g2-anatomy-spring.json",
    ),
    # Grade 3, both curriculum groups, each with the bedside document its own
    # annual names as a companion. These are the cases where a practice topic
    # reaches an event's notes (ADR-100, ADR-102).
    (
        "grade3_yearly_v1",
        "real/g3-tr-a-annual.snapshot.json",
        3,
        "turkish",
        _Y2026,
        ("real/g3-tr-a-bedside.snapshot.json",),
        _OWNS_3A,
        "parse/g3-tr-a-annual.json",
    ),
    (
        "grade3_yearly_v1",
        "real/g3-tr-b-annual.snapshot.json",
        3,
        "turkish",
        _Y2026,
        ("real/g3-tr-b-bedside.snapshot.json",),
        _OWNS_3B,
        "parse/g3-tr-b-annual.json",
    ),
    # The same annual with no companion, which is what the pipeline does before
    # the bedside document has ever been acquired. Its golden must differ from
    # the case above in the notes alone: the schedule may not move because a
    # document that only annotates it is missing (ADR-102).
    (
        "grade3_yearly_v1",
        "real/g3-tr-a-annual.snapshot.json",
        3,
        "turkish",
        _Y2026,
        _ALONE,
        _OWNS_3A,
        "parse/g3-tr-a-annual-without-companion.json",
    ),
    # The English program states no A/B division, so its term cell is read only
    # for the class year and every row stays program-wide (ADR-098).
    (
        "grade3_yearly_v1",
        "real/g3-en-annual.snapshot.json",
        3,
        "english",
        _Y2026,
        _ALONE,
        _UNNARROWED,
        "parse/g3-en-annual.json",
    ),
    # Both rotation workbooks. The A file is the one carrying the contradictory
    # row that publishes six cohorts and refuses two (ADR-099).
    (
        "grade3_faculty_practice_v1",
        "real/g3-tr-a-faculty.snapshot.json",
        3,
        "turkish",
        _Y2026,
        _ALONE,
        _UNNARROWED,
        "parse/g3-tr-a-faculty.json",
    ),
    (
        "grade3_faculty_practice_v1",
        "real/g3-tr-b-faculty.snapshot.json",
        3,
        "turkish",
        _Y2026,
        _ALONE,
        _UNNARROWED,
        "parse/g3-tr-b-faculty.json",
    ),
    # Both bedside documents, which publish nothing and whose goldens therefore
    # assert on their metrics: they are the reader the annual profile calls, and
    # the metrics are how much of each catalogue it could resolve (ADR-100).
    (
        "grade3_bedside_v1",
        "real/g3-tr-a-bedside.snapshot.json",
        3,
        "turkish",
        _Y2026,
        _ALONE,
        _UNNARROWED,
        "parse/g3-tr-a-bedside.json",
    ),
    (
        "grade3_bedside_v1",
        "real/g3-tr-b-bedside.snapshot.json",
        3,
        "turkish",
        _Y2026,
        _ALONE,
        _UNNARROWED,
        "parse/g3-tr-b-bedside.json",
    ),
)

CASE_FIELDS = (
    "profile_name",
    "fixture",
    "class_year",
    "program_language",
    "academic_year",
    "auxiliary_fixtures",
    "authoritative_selectors",
    "golden",
)


def run_profile(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    academic_year: str,
    auxiliary_fixtures: tuple[str, ...],
    authoritative_selectors: Mapping[str, list[str]],
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
        academic_year=academic_year,
        class_year=class_year,
        program_language=program_language,
        auxiliary_fixtures=auxiliary_fixtures,
        authoritative_selectors=authoritative_selectors,
    )
    return parser(request, profile)


def build_document(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    academic_year: str,
    auxiliary_fixtures: tuple[str, ...],
    authoritative_selectors: Mapping[str, list[str]],
) -> dict[str, Any]:
    return build_golden_document(
        fixture=fixture,
        subject="parseResponse",
        payload=build_response_projection(
            run_profile(
                profile_name,
                fixture,
                class_year,
                program_language,
                academic_year,
                auxiliary_fixtures,
                authoritative_selectors,
            )
        ),
    )


@pytest.mark.parametrize(CASE_FIELDS, CASES)
def test_parse_matches_its_golden_file(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    academic_year: str,
    auxiliary_fixtures: tuple[str, ...],
    authoritative_selectors: Mapping[str, list[str]],
    golden: str,
) -> None:
    assert_matches_golden(
        golden,
        build_document(
            profile_name,
            fixture,
            class_year,
            program_language,
            academic_year,
            auxiliary_fixtures,
            authoritative_selectors,
        ),
    )


@pytest.mark.parametrize(CASE_FIELDS, CASES)
def test_parse_is_deterministic(
    profile_name: str,
    fixture: str,
    class_year: int,
    program_language: str,
    academic_year: str,
    auxiliary_fixtures: tuple[str, ...],
    authoritative_selectors: Mapping[str, list[str]],
    golden: str,
) -> None:
    assert_deterministic(
        lambda: build_document(
            profile_name,
            fixture,
            class_year,
            program_language,
            academic_year,
            auxiliary_fixtures,
            authoritative_selectors,
        )
    )
