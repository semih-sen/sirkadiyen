from datetime import date, time
from enum import StrEnum
from typing import Literal, Self

from pydantic import Field, model_validator

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

    #: A timed item states both times; an all-day item states neither (ADR-046).
    #: The two are validated together, so a half-stated shape cannot reach .NET.
    start_local_time: time | None = None
    end_local_time: time | None = None

    #: Whether the item occupies the whole local date instead of a time range.
    #: A dated holiday or semester-break row is all-day, because the source
    #: states no times for it and none may be invented (ADR-046).
    is_all_day: bool = False
    time_zone_id: str = Field(min_length=1)
    instructor: str | None = None
    location: str | None = None

    #: The curriculum block ("dilim") the lesson belongs to, when the source
    #: states it (ADR-047). It is lesson content, never an audience selector,
    #: and it is never derived from the lesson title.
    curriculum_block: str | None = None

    #: Every academic department the source explicitly names for this lesson, in
    #: source order (ADR-049). An integrated session names several. Empty means
    #: the source stated none; a department is never inferred.
    departments: list[str] = Field(default_factory=list)
    stable_identity: str = Field(min_length=1)
    content_hash: str = Field(min_length=1)
    confidence: float = Field(ge=0, le=1)
    identity_components: list[IdentityComponent] = Field(default_factory=list)
    evidence: list[SourceEvidence] = Field(default_factory=list)

    @model_validator(mode="after")
    def _validate_schedule_shape(self) -> Self:
        """Refuse a candidate that is neither clearly timed nor clearly all-day.

        A record with one time missing would reach Google Calendar as an event
        with no end, so the shape is an invariant of the contract rather than a
        rule each profile is trusted to remember.
        """
        if self.is_all_day:
            if self.start_local_time is not None or self.end_local_time is not None:
                raise ValueError("An all-day candidate must state no local time.")
            return self

        if self.start_local_time is None or self.end_local_time is None:
            raise ValueError("A timed candidate must state both local times.")
        if self.end_local_time <= self.start_local_time:
            raise ValueError("A timed candidate must end after it starts.")
        return self


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


class ParseSourceContext(ContractModel):
    """Facts about the source that the spreadsheet itself does not state.

    Academic year, class year, program language and interpretation timezone are
    source configuration, not source content. They are required so the parser
    never has to infer them from dates, file names or profile names, and so one
    profile can serve several sources (ADR-017).
    """

    academic_year: str = Field(min_length=1)
    class_year: int = Field(ge=1, le=6)
    program_language: ProgramLanguage
    time_zone_id: str = Field(min_length=1)


class ParseSnapshotRequest(ContractModel):
    contract_version: Literal["1.0"]
    correlation_id: str = Field(min_length=1)
    parser_profile: ParserProfileDescriptor
    source_context: ParseSourceContext
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
