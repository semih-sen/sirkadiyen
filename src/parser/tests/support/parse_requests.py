"""Builders and golden projections for whole parse runs.

A parse response for a real workbook holds hundreds of candidates. Committing
every field of every candidate would produce a golden file nobody reads, so the
projection keeps three separate guarantees:

- a digest line per candidate, so a moved or changed lesson is visible in the
  diff by title, date, time and hashes
- the complete warnings, metrics and confidence indicators, because they are the
  parser's own account of what it refused to publish
- a digest of the entire serialized response, so a change to a field that no
  digest line covers still fails the comparison
"""

import hashlib
from typing import Any

from sirkadiyen_parser.contracts.parsing import (
    CanonicalScheduleCandidate,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
)
from tests.support.golden import load_fixture_json

#: Enough hash to identify a lesson without filling the golden file with hex.
HASH_PREFIX_LENGTH = 16

#: Enough title to recognize the lesson in a diff.
TITLE_PREFIX_LENGTH = 60


def build_parse_request(
    *,
    fixture: str,
    profile_name: str,
    profile_version: str,
    academic_year: str,
    class_year: int,
    program_language: str,
    correlation_id: str = "golden-run",
    time_zone_id: str = "Europe/Istanbul",
) -> ParseSnapshotRequest:
    """Build a parse request from a committed snapshot fixture.

    The request is validated through the camel-case wire path, so the test
    exercises the same validation a real producer would.
    """
    return ParseSnapshotRequest.model_validate(
        {
            "contractVersion": "1.0",
            "correlationId": correlation_id,
            "parserProfile": {"name": profile_name, "version": profile_version},
            "sourceContext": {
                "academicYear": academic_year,
                "classYear": class_year,
                "programLanguage": program_language,
                "timeZoneId": time_zone_id,
            },
            "snapshot": load_fixture_json(fixture),
        }
    )


def candidate_digest(candidate: CanonicalScheduleCandidate) -> str:
    """Render one candidate as a single reviewable line."""
    return "|".join(
        (
            candidate.candidate_id,
            candidate.local_date.isoformat(),
            f"{candidate.start_local_time:%H:%M}-{candidate.end_local_time:%H:%M}",
            candidate.event_type.value,
            f"id:{_short(candidate.stable_identity)}",
            f"content:{_short(candidate.content_hash)}",
            candidate.display_title[:TITLE_PREFIX_LENGTH],
        )
    )


def build_response_projection(response: ParseSnapshotResponse) -> dict[str, Any]:
    """Project a parse response into its golden representation."""
    payload = response.model_dump(mode="json", by_alias=True)
    return {
        "status": payload["status"],
        "sourceId": payload["sourceId"],
        "snapshotId": payload["snapshotId"],
        "parserProfile": payload["parserProfile"],
        "candidateCount": len(response.candidates),
        "responseDigest": _digest(response),
        "firstCandidate": payload["candidates"][0] if payload["candidates"] else None,
        "lastCandidate": payload["candidates"][-1] if payload["candidates"] else None,
        "candidateDigests": [candidate_digest(candidate) for candidate in response.candidates],
        "warnings": payload["warnings"],
        "metrics": payload["metrics"],
        "confidenceIndicators": payload["confidenceIndicators"],
    }


def _digest(response: ParseSnapshotResponse) -> str:
    serialized = response.model_dump_json(by_alias=True).encode("utf-8")
    return "sha256:" + hashlib.sha256(serialized).hexdigest()


def _short(hash_value: str) -> str:
    _, _, hex_digest = hash_value.partition(":")
    return hex_digest[:HASH_PREFIX_LENGTH]
