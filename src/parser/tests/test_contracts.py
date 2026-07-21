from pathlib import Path

from sirkadiyen_parser.contracts.parsing import ParseSnapshotRequest
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
    assert "contract_version" not in payload
