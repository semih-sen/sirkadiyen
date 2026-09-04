from datetime import date, time
from enum import StrEnum
from typing import Any, Literal, Self

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
    FREE_STUDY = "freeStudy"
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

    #: Free text a source states about this session that has no field of its own
    #: — today, the topic the Grade 3 bedside document gives a session the annual
    #: program schedules (ADR-101). It is content, not identity: correcting a
    #: topic must change the event a student already has rather than replace it
    #: with a second one, so it is part of the content hash and never of the
    #: stable identity.
    notes: str | None = None

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

    #: Machine-readable evidence for warnings whose meaning cannot be recovered
    #: from prose (ADR-139). A date-repair suggestion states the date the source
    #: wrote, the anchors that bound it and every date it may have meant, and an
    #: operator has to be able to act on one of them rather than retype it. Every
    #: other warning states nothing here: the message is the evidence, and a
    #: field that every producer filled in with something would stop being
    #: readable by any consumer.
    detail: dict[str, Any] | None = None


class ParserMetric(OutboundContractModel):
    name: str = Field(min_length=1)
    value: float
    unit: str | None = None


class ConfidenceIndicator(OutboundContractModel):
    field: str = Field(min_length=1)
    score: float = Field(ge=0, le=1)
    reason: str = Field(min_length=1)
    candidate_id: str | None = None


class SourceDateCorrection(ContractModel):
    """One date an operator has decided the source states out of sequence (ADR-139).

    ``original`` is the date the document resolves to today and ``corrected`` is
    what it means. When they differ the parser reads the first as the second
    wherever the source writes it. When they are the same the operator is
    confirming that the date the document states is right despite sitting out of
    sequence, so nothing is rewritten and the row simply stops being flagged.
    """

    original: date
    corrected: date

    #: Who accepted the correction, and when, so a published date that no
    #: document states can always be traced to a person.
    decided_by: str = Field(min_length=1)
    decided_at: str = Field(min_length=1)


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

    #: The audience values this source is the authority for, keyed by selector
    #: dimension (ADR-110). Two documents may state the same session — the Grade 3
    #: A and B workbooks both carry the sessions both halves of the class attend —
    #: and each states it in its own wording, so neither can be recognized as the
    #: other's copy. Naming the half each document owns is what makes one of them
    #: publish it, and that is source configuration in exactly the sense ADR-017
    #: means: the workbook does not say which half it belongs to.
    #:
    #: A dimension absent from this mapping is not narrowed at all. That is
    #: deliberately different from a dimension mapped to an empty list, which says
    #: the source may address nobody in it. Silence must not be read as "nothing is
    #: permitted", because almost every source declares nothing here and must keep
    #: publishing exactly what it published before.
    authoritative_audience_selectors: dict[str, list[str]] = Field(default_factory=dict)

    #: The dates on which the companion sources that own this source's group
    #: rotation have already published (ADR-126). It is orchestration knowledge in
    #: exactly the sense ADR-017 means — the workbook cannot say whether another
    #: document exists — so it travels with the rest of the source context.
    #:
    #: Only a profile declaring ``group_rotation_fallback`` reads it. An empty list
    #: means no companion has published any date, not that coverage is unknown: the
    #: caller states the coverage it found, and a source with no rotation companion
    #: configured simply sends nothing and parses as it always did.
    group_rotation_covered_dates: list[date] = Field(default_factory=list)

    #: Dates an operator has decided this source states wrongly, keyed by the
    #: value the document writes (ADR-139). A source typo is a fact about the
    #: source that the source itself cannot state, which is exactly what ADR-017
    #: means by source context, and holding the decision here rather than editing
    #: a parsed record keeps a parse a pure function of its snapshot, its profile
    #: and its context.
    #:
    #: A correction is keyed by the wrong value rather than by a cell address,
    #: because the document is re-acquired and its rows move while the mistyped
    #: value does not. It is applied wherever the source writes that date, which
    #: is the intended reading of "this document says 2020-11-20 and means
    #: 2026-11-20".
    date_corrections: list["SourceDateCorrection"] = Field(default_factory=list)


class ParseSnapshotRequest(ContractModel):
    contract_version: Literal["1.0"]
    correlation_id: str = Field(min_length=1)
    parser_profile: ParserProfileDescriptor
    source_context: ParseSourceContext
    snapshot: NormalizedSpreadsheetSnapshot

    #: Snapshots of companion sources this profile reads alongside its own, in
    #: the order the catalog declares them (ADR-102). A companion is never the
    #: subject of the parse: no candidate is ever published from one, and a
    #: profile that receives none must produce exactly what it produced before
    #: companions existed. That is why this is optional rather than required —
    #: the annual program must not wait for a document it only enriches from.
    auxiliary_snapshots: list[NormalizedSpreadsheetSnapshot] = Field(default_factory=list)


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
