from datetime import date, time
from enum import StrEnum
from typing import Literal

from pydantic import Field

from sirkadiyen_parser.contracts.base import ContractModel, OutboundContractModel
from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot


class ParserResultStatus(StrEnum):
    COMPLETED = "completed"
    COMPLETED_WITH_WARNINGS = "completedWithWarnings"
    REJECTED = "rejected"


class ProgramLanguage(StrEnum):
    TURKISH = "turkish"
    ENGLISH = "english"


class ScheduleEventType(StrEnum):
    THEORY = "theory"
    PRACTICE = "practice"
    ANATOMY_PRACTICE = "anatomyPractice"
    BEDSIDE_PRACTICE = "bedsidePractice"
    FACULTY_PRACTICE = "facultyPractice"
    VERTICAL_CORRIDOR = "verticalCorridor"
    INTEGRATED_SESSION = "integratedSession"
    EXAM = "exam"
    OTHER = "other"


class CandidateRecordStatus(StrEnum):
    SCHEDULED = "scheduled"
    CANCELLED = "cancelled"


class AudienceScope(StrEnum):
    ALL_STUDENTS_IN_PROGRAM = "allStudentsInProgram"
    SELECTED_GROUPS = "selectedGroups"


class ParserWarningSeverity(StrEnum):
    INFORMATION = "information"
    WARNING = "warning"
    ERROR = "error"


class ParserProfileDescriptor(OutboundContractModel):
    name: str = Field(min_length=1)
    version: str = Field(min_length=1)


class AudienceSelector(OutboundContractModel):
    dimension: str = Field(min_length=1)
    value: str = Field(min_length=1)


class ScheduleAudienceCandidate(OutboundContractModel):
    scope: AudienceScope
    selectors: list[AudienceSelector] = Field(default_factory=list)


class IdentityComponent(OutboundContractModel):
    name: str = Field(min_length=1)
    value: str = Field(min_length=1)


class SourceEvidence(OutboundContractModel):
    sheet_id: str = Field(min_length=1)
    sheet_title: str = Field(min_length=1)
    range: str = Field(min_length=1)
    raw_text: str | None = None
    extraction_rule: str = Field(min_length=1)


class CanonicalScheduleCandidate(OutboundContractModel):
    candidate_id: str = Field(min_length=1)
    academic_year: str = Field(min_length=1)
    class_year: int = Field(ge=1, le=6)
    program_language: ProgramLanguage
    audience: ScheduleAudienceCandidate
    event_type: ScheduleEventType
    status: CandidateRecordStatus
    normalized_course_identity: str | None = None
    display_title: str = Field(min_length=1)
    local_date: date
    start_local_time: time
    end_local_time: time
    time_zone_id: str = Field(min_length=1)
    instructor: str | None = None
    location: str | None = None
    stable_identity: str = Field(min_length=1)
    content_hash: str = Field(min_length=1)
    confidence: float = Field(ge=0, le=1)
    identity_components: list[IdentityComponent] = Field(default_factory=list)
    evidence: list[SourceEvidence] = Field(default_factory=list)


class ParserWarning(OutboundContractModel):
    severity: ParserWarningSeverity
    code: str = Field(min_length=1)
    message: str = Field(min_length=1)
    candidate_id: str | None = None
    evidence: SourceEvidence | None = None


class ParserMetric(OutboundContractModel):
    name: str = Field(min_length=1)
    value: float
    unit: str | None = None


class ConfidenceIndicator(OutboundContractModel):
    field: str = Field(min_length=1)
    score: float = Field(ge=0, le=1)
    reason: str = Field(min_length=1)
    candidate_id: str | None = None


class ParseSnapshotRequest(ContractModel):
    contract_version: Literal["1.0"]
    correlation_id: str = Field(min_length=1)
    parser_profile: ParserProfileDescriptor
    snapshot: NormalizedSpreadsheetSnapshot


class ParseSnapshotResponse(OutboundContractModel):
    contract_version: Literal["1.0"]
    correlation_id: str = Field(min_length=1)
    source_id: str = Field(min_length=1)
    snapshot_id: str = Field(min_length=1)
    parser_profile: ParserProfileDescriptor
    status: ParserResultStatus
    candidates: list[CanonicalScheduleCandidate] = Field(default_factory=list)
    warnings: list[ParserWarning] = Field(default_factory=list)
    metrics: list[ParserMetric] = Field(default_factory=list)
    confidence_indicators: list[ConfidenceIndicator] = Field(default_factory=list)
