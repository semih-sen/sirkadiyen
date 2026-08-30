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
    # The anatomy group is a dimension of its own: it is independent of the
    # practice group a student is in, and the two must never be read into one
    # another. The annual marker is the title the annual program gives the same
    # lesson, and it is what this source's rows are published under (ADR-078).
    assert anatomy_profile["audience_dimensions"] == ["anatomyGroup"]
    assert anatomy_profile["annual_markers"] == ["Diseksiyon"]
    assert anatomy_profile["implemented"] is True


def test_every_profile_advertises_the_numeric_date_order_it_declares() -> None:
    # An operator reading a source that writes 12/11/2026 has to be able to see
    # what the profile will do with it without reading the parser.
    response = client.get("/v1/profiles")

    declared = {profile["name"]: profile["numeric_date_order"] for profile in response.json()}
    assert set(declared.values()) <= {"dayFirst", "monthFirst", "undeclared"}
    # One committed source writes a numeric date, and the Grade 2 annual workbook
    # dates that same session as a serial, so that profile declares an order
    # (ADR-075). Every other profile still states that nothing has established one.
    assert declared["grade2_practice_v1"] == "dayFirst"
    assert declared["grade1_yearly_v1"] == "undeclared"


def test_a_profile_advertises_the_group_rotation_subjects_it_excludes() -> None:
    # An operator asking why the Grade 2 calendars hold no dissection must be able
    # to see that the profile defers those rows to the anatomy source (ADR-073),
    # without reading the parser.
    response = client.get("/v1/profiles")

    declared = {profile["name"]: profile["group_rotation_subjects"] for profile in response.json()}
    assert declared["grade2_yearly_v1"] == ["diseksiyon", "dissection"]
    assert declared["grade1_yearly_v1"] == []


def test_a_profile_advertises_whether_it_publishes_an_uncovered_rotation_itself() -> None:
    # Whether a rotation is deferred outright or only for the dates the companion
    # has published is the difference between a student seeing nothing and seeing
    # all three hours (ADR-126), so the profile states which it does.
    response = client.get("/v1/profiles")

    declared = {profile["name"]: profile["group_rotation_fallback"] for profile in response.json()}
    assert declared["grade2_yearly_v1"] is True
    assert declared["grade3_yearly_v1"] is False


def test_a_profile_advertises_whether_it_reads_an_unlabelled_term_column() -> None:
    # A source that stopped labelling its term column is read only where the
    # profile declares it, and rejected everywhere else (ADR-128), so which
    # profiles declare it is part of how a source is read.
    response = client.get("/v1/profiles")

    declared = {
        profile["name"]: profile["term_column_may_be_unlabelled"] for profile in response.json()
    }
    # All three annual profiles now declare it: Grade 3 has never labelled the
    # column, and the Grade 1 Turkish and Grade 2 English workbooks stopped.
    assert declared["grade1_yearly_v1"] is True
    assert declared["grade2_yearly_v1"] is True
    assert declared["grade3_yearly_v1"] is True
    # A profile reading a different layout must not inherit the exception.
    assert declared["grade1_practice_v1"] is False
    assert declared["grade2_practice_v1"] is False


def test_the_annual_profile_is_advertised_as_implemented() -> None:
    response = client.get("/v1/profiles")

    annual_profile = next(
        profile for profile in response.json() if profile["name"] == "grade1_yearly_v1"
    )

    assert annual_profile["implemented"] is True


def test_registered_but_unimplemented_profile_is_not_silent_success() -> None:
    request = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
    # Named here rather than taken from the shared contract fixture, which the
    # .NET contract tests read too: the profile it names became implemented, and
    # this test needs one that is still only described. `grade3_bedside_v1` was
    # named here until it was implemented in turn, and `weekly_amphitheatre_v1`
    # after it (ADR-133).
    request["parserProfile"] = {"name": "grade3_faculty_locations_v1", "version": "1.0.0"}

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
    request["parserProfile"] = {"name": "grade1_yearly_v1", "version": "1.7.0"}

    response = client.post("/v1/parse", json=request)

    # The shared contract fixture is an anatomy worksheet, so the annual profile
    # finds no header row and must reject rather than return an empty success.
    assert response.status_code == 200
    assert response.json()["status"] == "rejected"
