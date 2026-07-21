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


def test_registered_but_unimplemented_profile_is_not_silent_success() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))

    response = client.post("/v1/parse", json=request)

    assert response.status_code == 501
    assert response.json()["detail"]["code"] == "parserProfileNotImplemented"
