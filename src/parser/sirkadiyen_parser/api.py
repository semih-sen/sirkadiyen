from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel, ConfigDict

from sirkadiyen_parser.contracts.parsing import ParseSnapshotRequest, ParseSnapshotResponse
from sirkadiyen_parser.parsers import get_parser
from sirkadiyen_parser.profiles import get_profile, list_profiles


class HealthResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: str


class ProfileResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str
    version: str
    source_family: str
    audience_dimensions: tuple[str, ...]
    annual_markers: tuple[str, ...]
    implemented: bool


app = FastAPI(title="Sirkadiyen Parser", version="0.1.0")


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="healthy")


@app.get("/v1/profiles", response_model=list[ProfileResponse])
def profiles() -> list[ProfileResponse]:
    return [
        ProfileResponse(
            name=profile.name,
            version=profile.version,
            source_family=profile.source_family,
            audience_dimensions=profile.audience_dimensions,
            annual_markers=profile.annual_markers,
            implemented=get_parser(profile.name, profile.version) is not None,
        )
        for profile in list_profiles()
    ]


@app.post("/v1/parse", response_model=ParseSnapshotResponse)
def parse_snapshot(request: ParseSnapshotRequest) -> ParseSnapshotResponse:
    profile = get_profile(request.parser_profile.name, request.parser_profile.version)
    if profile is None:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail={
                "code": "unsupportedParserProfile",
                "message": "The requested parser profile and version are not registered.",
            },
        )

    parser = get_parser(profile.name, profile.version)
    if parser is None:
        raise HTTPException(
            status_code=status.HTTP_501_NOT_IMPLEMENTED,
            detail={
                "code": "parserProfileNotImplemented",
                "message": f"Parser profile '{profile.name}' is registered but not implemented.",
            },
        )

    return parser(request, profile)
