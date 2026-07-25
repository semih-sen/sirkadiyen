import json
from pathlib import Path

from fastapi.testclient import TestClient

from sirkadiyen_parser.api import app

CONTRACT_FIXTURE = (
    Path(__file__).resolve().parents[3] / "tests" / "contracts" / "v1" / "parse-request.json"
)

client = TestClient(app)


def test_health() -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "healthy"}


def test_registered_profiles_include_independent_anatomy_group() -> None:
    response = client.get("/v1/profiles")

    assert response.status_code == 200
    anatomy_profile = next(
        profile for profile in response.json() if profile["name"] == "grade2_anatomy_autumn_v1"
    )
    assert anatomy_profile["audience_dimensions"] == ["anatomyGroup"]
    assert anatomy_profile["annual_markers"] == ["Diseksiyon"]
    assert anatomy_profile["implemented"] is False


def test_every_profile_advertises_the_numeric_date_order_it_declares() -> None:
    # An operator reading a source that writes 12/11/2026 has to be able to see
    # what the profile will do with it without reading the parser.
    response = client.get("/v1/profiles")

    declared = {profile["name"]: profile["numeric_date_order"] for profile in response.json()}
    assert set(declared.values()) <= {"dayFirst", "monthFirst", "undeclared"}
    # No committed fixture writes a numeric date, so nothing has established an
    # order yet. Changing this line means a source finally showed one.
    assert declared["grade1_yearly_v1"] == "undeclared"


def test_the_annual_profile_is_advertised_as_implemented() -> None:
    response = client.get("/v1/profiles")

    annual_profile = next(
        profile for profile in response.json() if profile["name"] == "grade1_yearly_v1"
    )

    assert annual_profile["implemented"] is True


def test_registered_but_unimplemented_profile_is_not_silent_success() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))

    response = client.post("/v1/parse", json=request)

    assert response.status_code == 501
    assert response.json()["detail"]["code"] == "parserProfileNotImplemented"


def test_an_unknown_profile_is_refused() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
    request["parserProfile"] = {"name": "grade9_imaginary_v1", "version": "1.0.0"}

    response = client.post("/v1/parse", json=request)

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "unsupportedParserProfile"


def test_a_request_without_source_context_is_refused() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
    del request["sourceContext"]

    response = client.post("/v1/parse", json=request)

    assert response.status_code == 422


def test_the_annual_profile_parses_a_snapshot_over_http() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
    request["parserProfile"] = {"name": "grade1_yearly_v1", "version": "1.4.0"}

    response = client.post("/v1/parse", json=request)

    # The shared contract fixture is an anatomy worksheet, so the annual profile
    # finds no header row and must reject rather than return an empty success.
    assert response.status_code == 200
    assert response.json()["status"] == "rejected"
