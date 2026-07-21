import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from sirkadiyen_parser.contracts.parsing import ParseSnapshotRequest, ProgramLanguage
from sirkadiyen_parser.contracts.snapshot import CellScalarKind

CONTRACT_FIXTURE = (
    Path(__file__).resolve().parents[3] / "tests" / "contracts" / "v1" / "parse-request.json"
)


def test_dotnet_contract_fixture_is_accepted() -> None:
    request = ParseSnapshotRequest.model_validate_json(CONTRACT_FIXTURE.read_text(encoding="utf-8"))

    assert request.snapshot.source_id == "G2-ANATOMY-AUTUMN"
    assert request.snapshot.worksheets[0].cells[0].effective_value is not None
    assert request.snapshot.worksheets[0].cells[0].effective_value.kind is CellScalarKind.NUMBER


def test_contract_serialization_uses_camel_case_aliases() -> None:
    request = ParseSnapshotRequest.model_validate_json(CONTRACT_FIXTURE.read_text(encoding="utf-8"))

    payload = request.model_dump(mode="json", by_alias=True)

    assert payload["contractVersion"] == "1.0"
    assert payload["snapshot"]["sourceId"] == "G2-ANATOMY-AUTUMN"
    assert payload["sourceContext"]["academicYear"] == "2025-2026"
    assert "contract_version" not in payload
    assert "source_context" not in payload


def test_source_context_carries_what_the_spreadsheet_does_not_state() -> None:
    request = ParseSnapshotRequest.model_validate_json(CONTRACT_FIXTURE.read_text(encoding="utf-8"))

    assert request.source_context.academic_year == "2025-2026"
    assert request.source_context.class_year == 2
    assert request.source_context.program_language is ProgramLanguage.TURKISH
    assert request.source_context.time_zone_id == "Europe/Istanbul"


def test_a_request_without_source_context_is_a_producer_defect() -> None:
    payload = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
    del payload["sourceContext"]

    with pytest.raises(ValidationError):
        ParseSnapshotRequest.model_validate(payload)
