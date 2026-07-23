"""Row-oriented annual program parser.

An annual program lists one lesson per row: which term the row belongs to, the
date, a start and end time, the lesson title, the curriculum block with the
owning department, and where the lesson takes place. The Turkish and English
Grade 1 workbooks share this layout and differ only in header wording, so the
profile is described by header aliases rather than by column positions.

The profile never repairs what the source got wrong. A cell that cannot be read
under an explicit rule leaves the row unpublished and records a warning with the
address of the offending cell. That matters here: several time cells in the real
workbooks were silently converted to dates by the spreadsheet software, and
reading them as times would publish lessons at midnight.
"""

import re
from collections.abc import Iterator, Mapping, Sequence
from dataclasses import dataclass, field
from datetime import date, time

from sirkadiyen_parser.contracts.parsing import (
    AudienceScope,
    CandidateRecordStatus,
    CanonicalScheduleCandidate,
    ParserProfileDescriptor,
    ParserWarningSeverity,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ParseSourceContext,
    ScheduleAudienceCandidate,
    ScheduleEventType,
    SourceEvidence,
)
from sirkadiyen_parser.contracts.snapshot import NormalizedWorksheet
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.identity import build_identity_components, content_hash, stable_identity
from sirkadiyen_parser.normalization.courses import course_identity, normalize_course_title
from sirkadiyen_parser.normalization.dates import DateResolution, resolve_cell_date
from sirkadiyen_parser.normalization.departments import (
    RULE_DEPARTMENT_LIST_MEMBER,
    BlockDepartmentResolution,
    resolve_block_and_departments,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.instructors import split_trailing_instructors
from sirkadiyen_parser.normalization.text import comparison_key
from sirkadiyen_parser.normalization.times import (
    TimeResolution,
    duration_minutes,
    resolve_cell_time,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition

ROLE_TERM = "term"
ROLE_DATE = "date"
ROLE_START_TIME = "startTime"
ROLE_END_TIME = "endTime"
ROLE_TITLE = "title"
ROLE_BLOCK = "block"
ROLE_LOCATION = "location"

REQUIRED_ROLES = (ROLE_TERM, ROLE_DATE, ROLE_START_TIME, ROLE_END_TIME, ROLE_TITLE)
OPTIONAL_ROLES = (ROLE_BLOCK, ROLE_LOCATION)

#: How far into a worksheet the header row is searched for. A header further
#: down than this is a structural change that must be reviewed, not guessed at.
HEADER_SEARCH_ROW_LIMIT = 20

#: Header text is matched on folded comparison keys, so casing, Turkish letters
#: and spacing differences in the source do not break detection.
HEADER_ALIASES: Mapping[str, frozenset[str]] = {
    ROLE_TERM: frozenset({"donem", "term", "time table", "timetable", "class"}),
    ROLE_DATE: frozenset({"tarih", "start date", "date"}),
    ROLE_START_TIME: frozenset({"baslama saati", "baslangic saati", "start time"}),
    ROLE_END_TIME: frozenset({"bitis saati", "end time"}),
    ROLE_TITLE: frozenset({"konu", "subject", "ders", "course"}),
    ROLE_BLOCK: frozenset(
        {
            "dilim adi / anabilim dali",
            "dilim adi/anabilim dali",
            "dilim adi",
            "description",
            "aciklama",
        }
    ),
    ROLE_LOCATION: frozenset({"yer", "location", "place"}),
}

#: Plausible lesson duration. Anything outside it is published with a warning so
#: revision validation can decide, because the source does occasionally hold a
#: full-day block that is genuinely a full-day examination.
MIN_PLAUSIBLE_DURATION_MINUTES = 5
MAX_PLAUSIBLE_DURATION_MINUTES = 12 * 60

REASON_BLANK_ROW = "blankRow"
REASON_OTHER_CLASS_YEAR = "otherClassYear"
REASON_UNRESOLVED_TERM = "unresolvedTerm"
REASON_MISSING_TITLE = "missingTitle"
REASON_MISSING_DATE = "missingDate"
REASON_UNRESOLVED_DATE = "unresolvedDate"
REASON_NO_SCHEDULED_TIME = "noScheduledTime"
REASON_UNRESOLVED_START_TIME = "unresolvedStartTime"
REASON_UNRESOLVED_END_TIME = "unresolvedEndTime"
REASON_END_NOT_AFTER_START = "endTimeNotAfterStartTime"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"
WARNING_IMPLAUSIBLE_DURATION = "implausibleLessonDuration"
WARNING_WEEKDAY_MISMATCH = "weekdayMismatch"
WARNING_NO_HEADER_ROW = "worksheetWithoutHeaderRow"
WARNING_NO_WORKSHEET = "noParsableWorksheet"
WARNING_UNMARKED_BLOCK_SEGMENT = "unmarkedBlockDepartmentSegment"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED_NO_HEADER = "worksheets.ignored.noHeaderRow"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_ROWS_HIDDEN = "rows.hidden"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
METRIC_LOCATION_DEFERRED = "location.deferredToOtherProgram"
METRIC_CURRICULUM_BLOCK_STATED = "curriculumBlock.stated"
METRIC_DEPARTMENTS_STATED = "departments.stated"
METRIC_DEPARTMENTS_INTEGRATED_SESSION = "departments.integratedSession"
METRIC_DEPARTMENTS_LIST_MEMBER = "departments.unmarkedListMember"
METRIC_DEPARTMENTS_IGNORED_UNMARKED = "departments.ignored.unmarkedSegment"

RULE_HEADER_ALIAS = "annual.headerAlias"
RULE_TERM_CELL = "annual.termCell"
RULE_DATE_CELL = "annual.dateCell"
RULE_START_TIME_CELL = "annual.startTimeCell"
RULE_END_TIME_CELL = "annual.endTimeCell"
RULE_TITLE_CELL = "annual.titleCell"
RULE_BLOCK_CELL = "annual.blockCell"
RULE_LOCATION_CELL = "annual.locationCell"
RULE_ROW = "annual.row"

#: Titles whose first token marks a non-teaching entry. Checked before the
#: teaching keywords, because "SERBEST ÇALIŞMA (Dönem 2 Sınav)" is free study
#: during another year's examination, not an examination.
NON_TEACHING_FIRST_TOKENS = frozenset(
    {"serbest", "free", "tatil", "bayram", "holiday", "ogle", "lunch", "ara", "arasi", "break"}
)

EXAM_TOKENS = frozenset({"sinav", "exam"})
ANATOMY_TOKENS = frozenset({"diseksiyon", "dissection"})
INTEGRATED_TOKENS = frozenset({"entegre", "integrated"})
PRACTICE_TOKENS = frozenset({"uygulama", "practice", "lab"})
VERTICAL_CORRIDOR_TOKENS = frozenset({"dikey", "vertical"})

#: Locations that point at another published program instead of naming a room.
#: They are kept verbatim, but counted, because they quantify how much of the
#: schedule still depends on amphitheatre and practice enrichment.
DEFERRED_LOCATION_TOKENS = frozenset({"bakiniz", "see"})

_CLASS_YEAR_PATTERN = re.compile(r"(?<!\d)(\d{1,2})(?!\d)")
_WORD_PATTERN = re.compile(r"[^\W_]+", re.UNICODE)


@dataclass(frozen=True, slots=True)
class _RowContext:
    """Everything one source row contributed, before validation."""

    worksheet: NormalizedWorksheet
    grid: WorksheetGrid
    row_index: int
    columns: Mapping[str, int]

    def text(self, role: str) -> str:
        column = self.columns.get(role)
        return "" if column is None else self.grid.text(self.row_index, column)

    def evidence(self, role: str, *, extraction_rule: str) -> SourceEvidence:
        column = self.columns.get(role)
        if column is None:
            return self.row_evidence(extraction_rule=extraction_rule)
        return self.grid.evidence(self.row_index, column, extraction_rule=extraction_rule)

    def row_evidence(self, *, extraction_rule: str) -> SourceEvidence:
        columns = sorted(self.columns.values())
        return self.grid.range_evidence(
            self.row_index,
            columns[0],
            self.row_index + 1,
            columns[-1] + 1,
            extraction_rule=extraction_rule,
        )


@dataclass(frozen=True, slots=True)
class _CandidateDraft:
    """A candidate that still has to survive the duplicate check.

    Metrics and per-candidate findings are held here rather than recorded while
    the row is read, so a candidate that is rejected as a duplicate cannot leave
    counters or warnings behind that no published candidate accounts for.
    """

    candidate: CanonicalScheduleCandidate
    row: _RowContext
    resolved_date: DateResolution
    title_confidence: float
    deferred_location: bool
    block_departments: BlockDepartmentResolution


@dataclass(slots=True)
class _Accumulator:
    """Candidates kept so far, indexed by stable identity for duplicate checks."""

    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)

    #: Block-cell segments already reported as naming no marked department. The
    #: same wording repeats on dozens of rows, so it is reported once with the
    #: first row that carries it and counted for the rest.
    reported_unmarked_segments: set[str] = field(default_factory=set)


def parse_annual_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a row-oriented annual program snapshot into candidate lessons."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    selected = 0

    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        columns = _detect_columns(grid)
        if columns is None:
            diagnostics.increment(METRIC_WORKSHEETS_IGNORED_NO_HEADER)
            diagnostics.information(
                WARNING_NO_HEADER_ROW,
                f"Worksheet '{worksheet.title}' has no recognizable annual header row "
                f"in its first {HEADER_SEARCH_ROW_LIMIT} rows and was not parsed.",
                evidence=_worksheet_evidence(worksheet),
            )
            continue

        selected += 1
        header_row, mapping = columns
        _parse_worksheet(
            worksheet=worksheet,
            grid=grid,
            header_row=header_row,
            columns=mapping,
            context=request.source_context,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    if selected == 0:
        diagnostics.error(
            WARNING_NO_WORKSHEET,
            "No worksheet in the snapshot exposes an annual header row, so the "
            "snapshot cannot be parsed by this profile.",
        )

    diagnostics.set_metric(METRIC_CANDIDATES_EMITTED, len(accumulator.candidates))

    return ParseSnapshotResponse(
        contract_version=request.contract_version,
        correlation_id=request.correlation_id,
        source_id=request.snapshot.source_id,
        snapshot_id=request.snapshot.snapshot_id,
        parser_profile=ParserProfileDescriptor(name=profile.name, version=profile.version),
        status=diagnostics.status(),
        candidates=accumulator.candidates,
        warnings=list(diagnostics.warnings),
        metrics=list(diagnostics.metrics),
        confidence_indicators=list(diagnostics.confidence_indicators),
    )


def _worksheet_evidence(worksheet: NormalizedWorksheet) -> SourceEvidence:
    """Cite a whole worksheet, for findings that are not about one row."""
    return SourceEvidence(
        sheet_id=worksheet.sheet_id,
        sheet_title=worksheet.title,
        range=worksheet.requested_ranges[0] if worksheet.requested_ranges else "A1",
        raw_text=None,
        extraction_rule=RULE_HEADER_ALIAS,
    )


def _detect_columns(grid: WorksheetGrid) -> tuple[int, dict[str, int]] | None:
    """Find the header row and map roles to columns, or return ``None``."""
    worksheet = grid.worksheet
    row_limit = min(worksheet.row_count, HEADER_SEARCH_ROW_LIMIT)

    for row_index in range(row_limit):
        mapping: dict[str, int] = {}
        for column_index in range(worksheet.column_count):
            key = comparison_key(grid.text(row_index, column_index))
            if not key:
                continue
            for role, aliases in HEADER_ALIASES.items():
                if key in aliases and role not in mapping:
                    mapping[role] = column_index

        if all(role in mapping for role in REQUIRED_ROLES):
            return row_index, mapping

    return None


def _parse_worksheet(
    *,
    worksheet: NormalizedWorksheet,
    grid: WorksheetGrid,
    header_row: int,
    columns: Mapping[str, int],
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    for row_index in range(header_row + 1, worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)
        if grid.is_row_hidden(row_index):
            # A hidden row is a display decision. It is still parsed, because
            # the source has also used hiding for rows that carry the only copy
            # of a lesson, but the count is reported so a reviewer can check.
            diagnostics.increment(METRIC_ROWS_HIDDEN)

        row = _RowContext(
            worksheet=worksheet,
            grid=grid,
            row_index=row_index,
            columns=columns,
        )
        draft = _parse_row(row=row, context=context, diagnostics=diagnostics)
        if draft is None:
            continue

        _accept(draft=draft, diagnostics=diagnostics, accumulator=accumulator)


def _parse_row(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
) -> _CandidateDraft | None:
    if not any(row.text(role) for role in (*REQUIRED_ROLES, *OPTIONAL_ROLES)):
        diagnostics.record_ignored_row(
            REASON_BLANK_ROW,
            row.row_evidence(extraction_rule=RULE_ROW),
        )
        return None

    if not _matches_class_year(row=row, context=context, diagnostics=diagnostics):
        return None

    title_text = row.text(ROLE_TITLE)
    if not title_text:
        diagnostics.record_ignored_row(
            REASON_MISSING_TITLE,
            row.row_evidence(extraction_rule=RULE_ROW),
            severity=ParserWarningSeverity.WARNING,
            message="Row carries schedule data but no lesson title, so no lesson was published.",
        )
        return None

    resolved_date = _resolve_date(row=row, diagnostics=diagnostics)
    if resolved_date is None:
        return None

    times = _resolve_times(row=row, diagnostics=diagnostics)
    if times is None:
        return None

    start_time, end_time, time_confidence = times
    return _build_draft(
        row=row,
        context=context,
        title_text=title_text,
        resolved_date=resolved_date,
        start_time=start_time,
        end_time=end_time,
        time_confidence=time_confidence,
    )


def _matches_class_year(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
) -> bool:
    term_text = row.text(ROLE_TERM)
    class_year = _read_class_year(term_text)

    if class_year is None:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_TERM,
            row.evidence(ROLE_TERM, extraction_rule=RULE_TERM_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Term cell '{term_text}' does not state exactly one supported class year, "
                "so the row could not be assigned to a cohort."
            ),
        )
        return False

    if class_year != context.class_year:
        diagnostics.record_ignored_row(
            REASON_OTHER_CLASS_YEAR,
            row.evidence(ROLE_TERM, extraction_rule=RULE_TERM_CELL),
        )
        return False

    return True


def _read_class_year(term_text: str) -> int | None:
    """Read the class year stated by a term cell.

    ``Dönem 1`` and ``Time Table 1`` both state year one. A cell holding no
    one- or two-digit number, several of them, or a number outside the
    supported range states nothing usable and is never guessed at.
    """
    matches = _CLASS_YEAR_PATTERN.findall(term_text)
    if len(matches) != 1:
        return None

    value = int(matches[0])
    return value if 1 <= value <= 6 else None


def _resolve_date(*, row: _RowContext, diagnostics: ParseDiagnostics) -> DateResolution | None:
    column = row.columns[ROLE_DATE]
    cell = row.grid.resolve(row.row_index, column).cell
    date_text = row.text(ROLE_DATE)

    if not date_text:
        diagnostics.record_ignored_row(
            REASON_MISSING_DATE,
            row.evidence(ROLE_DATE, extraction_rule=RULE_DATE_CELL),
        )
        return None

    # The date column is declared to hold dates, but a bare number is still not
    # read as a serial: the source has put serials into neighbouring columns by
    # accident, and a wrongly typed cell must surface rather than resolve.
    resolution = resolve_cell_date(cell)
    if not resolution.resolved:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_DATE,
            row.evidence(ROLE_DATE, extraction_rule=RULE_DATE_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Date cell '{date_text}' could not be read as a date "
                f"({resolution.reason}), so the row was not published."
            ),
        )
        return None

    return resolution


def _resolve_times(
    *,
    row: _RowContext,
    diagnostics: ParseDiagnostics,
) -> tuple[time, time, float] | None:
    start_text = row.text(ROLE_START_TIME)
    end_text = row.text(ROLE_END_TIME)

    if not start_text and not end_text:
        # Holidays and semester breaks are written as a dated row without times.
        # They are real schedule information but not timed lessons, and this
        # profile only publishes timed lessons.
        diagnostics.record_ignored_row(
            REASON_NO_SCHEDULED_TIME,
            row.row_evidence(extraction_rule=RULE_ROW),
        )
        return None

    start = _resolve_time_cell(
        row=row,
        role=ROLE_START_TIME,
        rule=RULE_START_TIME_CELL,
        reason=REASON_UNRESOLVED_START_TIME,
        diagnostics=diagnostics,
    )
    if start is None:
        return None

    end = _resolve_time_cell(
        row=row,
        role=ROLE_END_TIME,
        rule=RULE_END_TIME_CELL,
        reason=REASON_UNRESOLVED_END_TIME,
        diagnostics=diagnostics,
    )
    if end is None:
        return None

    if start.value is None or end.value is None:
        return None

    if end.value <= start.value:
        diagnostics.record_ignored_row(
            REASON_END_NOT_AFTER_START,
            row.row_evidence(extraction_rule=RULE_ROW),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"End time {end.value.isoformat()} does not follow start time "
                f"{start.value.isoformat()}, so the row was not published."
            ),
        )
        return None

    return start.value, end.value, min(start.confidence, end.confidence)


def _resolve_time_cell(
    *,
    row: _RowContext,
    role: str,
    rule: str,
    reason: str,
    diagnostics: ParseDiagnostics,
) -> TimeResolution | None:
    column = row.columns[role]
    cell = row.grid.resolve(row.row_index, column).cell
    resolution = resolve_cell_time(cell)
    if resolution.resolved:
        return resolution

    diagnostics.record_ignored_row(
        reason,
        row.evidence(role, extraction_rule=rule),
        severity=ParserWarningSeverity.WARNING,
        message=(
            f"Time cell '{row.text(role)}' could not be read as a time "
            f"({resolution.reason}), so the row was not published."
        ),
    )
    return None


def _build_draft(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    title_text: str,
    resolved_date: DateResolution,
    start_time: time,
    end_time: time,
    time_confidence: float,
) -> _CandidateDraft:
    candidate_id = f"{row.worksheet.sheet_id}!R{row.row_index + 1}"

    course_title = normalize_course_title(title_text)
    split = split_trailing_instructors(course_title.display_title)
    display_title = split.title or course_title.display_title
    instructor = ", ".join(split.instructors) if split.instructors else None

    block_text = row.text(ROLE_BLOCK) or None
    location_text = row.text(ROLE_LOCATION) or None

    # Classification still reads the whole cell. The block and the department
    # both carry keywords the event type depends on, and splitting the cell must
    # not silently reclassify a lesson.
    event_type = classify_event_type(title=display_title, block=block_text)
    block_departments = resolve_block_and_departments(block_text)

    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", resolved_date.value.isoformat() if resolved_date.value else ""),
            ("startLocalTime", start_time.isoformat()),
            ("courseIdentity", course_identity(display_title) or ""),
        )
    )

    confidence = min(
        resolved_date.confidence,
        time_confidence,
        course_title.confidence,
        split.confidence if split.resolved else 1.0,
    )

    candidate = CanonicalScheduleCandidate(
        candidate_id=candidate_id,
        academic_year=context.academic_year,
        class_year=context.class_year,
        program_language=context.program_language,
        audience=ScheduleAudienceCandidate(scope=AudienceScope.ALL_STUDENTS_IN_PROGRAM),
        event_type=event_type,
        status=CandidateRecordStatus.SCHEDULED,
        normalized_course_identity=course_identity(display_title),
        display_title=display_title,
        local_date=_require_date(resolved_date),
        start_local_time=start_time,
        end_local_time=end_time,
        time_zone_id=context.time_zone_id,
        instructor=instructor,
        location=location_text,
        curriculum_block=block_departments.curriculum_block,
        departments=list(block_departments.departments),
        stable_identity=stable_identity(identity_components),
        content_hash=_content_hash(
            context=context,
            display_title=display_title,
            event_type=event_type,
            local_date=_require_date(resolved_date),
            start_time=start_time,
            end_time=end_time,
            instructor=instructor,
            location=location_text,
            block_departments=block_departments,
        ),
        confidence=confidence,
        identity_components=identity_components,
        evidence=list(_row_evidence(row)),
    )

    return _CandidateDraft(
        candidate=candidate,
        row=row,
        resolved_date=resolved_date,
        title_confidence=course_title.confidence,
        deferred_location=location_text is not None and _is_deferred_location(location_text),
        block_departments=block_departments,
    )


def _require_date(resolution: DateResolution) -> date:
    if resolution.value is None:  # pragma: no cover - guarded by the caller
        raise ValueError("A resolved date is required to build a candidate.")
    return resolution.value


def _content_hash(
    *,
    context: ParseSourceContext,
    display_title: str,
    event_type: ScheduleEventType,
    local_date: date,
    start_time: time,
    end_time: time,
    instructor: str | None,
    location: str | None,
    block_departments: BlockDepartmentResolution,
) -> str:
    return content_hash(
        {
            "academicYear": context.academic_year,
            "classYear": str(context.class_year),
            "programLanguage": context.program_language.value,
            "displayTitle": display_title,
            "eventType": event_type.value,
            "localDate": local_date.isoformat(),
            "startLocalTime": start_time.isoformat(),
            "endLocalTime": end_time.isoformat(),
            "timeZoneId": context.time_zone_id,
            "instructor": instructor,
            "location": location,
            "curriculumBlock": block_departments.curriculum_block,
            "departments": join_departments(block_departments.departments),
        }
    )


def join_departments(departments: tuple[str, ...]) -> str | None:
    """Encode a department list for hashing, or ``None`` when none was stated.

    A newline is the separator because normalized display text cannot contain
    one, so no two different department lists can encode to the same string.
    """
    return "\n".join(departments) if departments else None


def _row_evidence(row: _RowContext) -> Iterator[SourceEvidence]:
    for role, rule in (
        (ROLE_DATE, RULE_DATE_CELL),
        (ROLE_START_TIME, RULE_START_TIME_CELL),
        (ROLE_END_TIME, RULE_END_TIME_CELL),
        (ROLE_TITLE, RULE_TITLE_CELL),
        (ROLE_BLOCK, RULE_BLOCK_CELL),
        (ROLE_LOCATION, RULE_LOCATION_CELL),
    ):
        if role in row.columns and row.text(role):
            yield row.evidence(role, extraction_rule=rule)


def _record_candidate_diagnostics(
    *,
    draft: _CandidateDraft,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    candidate = draft.candidate
    row = draft.row
    resolved_date = draft.resolved_date

    diagnostics.increment(f"{METRIC_CANDIDATE_EVENT_TYPE_PREFIX}{candidate.event_type.value}")
    if draft.deferred_location:
        diagnostics.increment(METRIC_LOCATION_DEFERRED)

    _record_block_department_diagnostics(
        draft=draft,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )

    if resolved_date.confidence < 1.0:
        diagnostics.confidence(
            field_name="localDate",
            score=resolved_date.confidence,
            reason=resolved_date.reason or resolved_date.rule,
            candidate_id=candidate.candidate_id,
        )

    if draft.title_confidence < 1.0:
        diagnostics.confidence(
            field_name="displayTitle",
            score=draft.title_confidence,
            reason="titleJoinedFromMultipleLines",
            candidate_id=candidate.candidate_id,
        )

    if resolved_date.weekday_matches is False:
        diagnostics.warning(
            WARNING_WEEKDAY_MISMATCH,
            f"Date cell names weekday '{resolved_date.weekday_text}' but "
            f"{candidate.local_date.isoformat()} is a different weekday.",
            candidate_id=candidate.candidate_id,
            evidence=row.evidence(ROLE_DATE, extraction_rule=RULE_DATE_CELL),
        )

    duration = duration_minutes(candidate.start_local_time, candidate.end_local_time)
    if not MIN_PLAUSIBLE_DURATION_MINUTES <= duration <= MAX_PLAUSIBLE_DURATION_MINUTES:
        diagnostics.warning(
            WARNING_IMPLAUSIBLE_DURATION,
            f"Lesson lasts {duration} minutes, outside the plausible range of "
            f"{MIN_PLAUSIBLE_DURATION_MINUTES} to {MAX_PLAUSIBLE_DURATION_MINUTES} minutes.",
            candidate_id=candidate.candidate_id,
            evidence=row.row_evidence(extraction_rule=RULE_ROW),
        )


def _record_block_department_diagnostics(
    *,
    draft: _CandidateDraft,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    """Account for everything the block/department cell did and did not yield."""
    candidate = draft.candidate
    resolution = draft.block_departments

    if resolution.curriculum_block is not None:
        diagnostics.increment(METRIC_CURRICULUM_BLOCK_STATED)

    if resolution.resolved:
        diagnostics.increment(METRIC_DEPARTMENTS_STATED)
    if resolution.names_several_departments:
        diagnostics.increment(METRIC_DEPARTMENTS_INTEGRATED_SESSION)

    if resolution.rule == RULE_DEPARTMENT_LIST_MEMBER:
        diagnostics.increment(METRIC_DEPARTMENTS_LIST_MEMBER)
        diagnostics.confidence(
            field_name="departments",
            score=resolution.confidence,
            reason=resolution.rule,
            candidate_id=candidate.candidate_id,
        )

    for segment in resolution.unmarked_segments:
        diagnostics.increment(METRIC_DEPARTMENTS_IGNORED_UNMARKED)
        key = comparison_key(segment)
        if key in accumulator.reported_unmarked_segments:
            continue

        accumulator.reported_unmarked_segments.add(key)
        diagnostics.information(
            WARNING_UNMARKED_BLOCK_SEGMENT,
            f"Block cell segment '{segment}' names no academic department marker, so it "
            "was not published as a department. Widening the rule requires source "
            "evidence, not a guess.",
            candidate_id=candidate.candidate_id,
            evidence=draft.row.evidence(ROLE_BLOCK, extraction_rule=RULE_BLOCK_CELL),
        )


def _accept(
    *,
    draft: _CandidateDraft,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    """Keep a candidate unless an earlier row already claimed its identity."""
    candidate = draft.candidate
    row = draft.row

    existing = accumulator.by_identity.get(candidate.stable_identity)
    if existing is None:
        accumulator.by_identity[candidate.stable_identity] = candidate
        accumulator.candidates.append(candidate)
        _record_candidate_diagnostics(
            draft=draft,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )
        return

    if existing.content_hash == candidate.content_hash:
        # The source repeats a row verbatim. Publishing it twice would create a
        # duplicate calendar event, so the first occurrence is kept.
        diagnostics.record_ignored_row(
            REASON_DUPLICATE_IDENTITY,
            row.row_evidence(extraction_rule=RULE_ROW),
        )
        return

    # Two rows describe the same lesson differently. Choosing between them would
    # be a guess, so the first is kept and the conflict is reported.
    diagnostics.record_ignored_row(
        REASON_DUPLICATE_IDENTITY,
        row.row_evidence(extraction_rule=RULE_ROW),
        severity=ParserWarningSeverity.WARNING,
        message=(
            f"Row repeats the lesson already published as candidate "
            f"'{existing.candidate_id}' with different content, so it was not published."
        ),
    )
    diagnostics.warn(
        severity=ParserWarningSeverity.WARNING,
        code=WARNING_CONFLICTING_DUPLICATE,
        message=(
            "Two rows share one lesson identity but disagree on content. "
            "The first occurrence was kept."
        ),
        candidate_id=existing.candidate_id,
        evidence=row.row_evidence(extraction_rule=RULE_ROW),
    )


def classify_event_type(*, title: str, block: str | None) -> ScheduleEventType:
    """Classify a lesson from its title and curriculum block.

    The order of the tests is the rule. A title that names an examination is an
    examination even when it also names a practice, and a non-teaching entry
    such as free study is never reclassified by a word inside a parenthesis.
    """
    title_words = _words(title)
    block_words = _words(block or "")
    all_words = title_words + block_words

    if title_words and title_words[0] in NON_TEACHING_FIRST_TOKENS:
        return ScheduleEventType.OTHER

    if _matches(title_words, EXAM_TOKENS):
        return ScheduleEventType.EXAM

    if _matches(all_words, ANATOMY_TOKENS):
        return ScheduleEventType.ANATOMY_PRACTICE

    if _matches(title_words, INTEGRATED_TOKENS):
        return ScheduleEventType.INTEGRATED_SESSION

    if _matches(all_words, PRACTICE_TOKENS):
        if _matches(block_words, VERTICAL_CORRIDOR_TOKENS):
            return ScheduleEventType.VERTICAL_CORRIDOR
        return ScheduleEventType.PRACTICE

    return ScheduleEventType.THEORY


def _is_deferred_location(location: str) -> bool:
    return any(word in DEFERRED_LOCATION_TOKENS for word in _words(location))


def _words(value: str) -> list[str]:
    return _WORD_PATTERN.findall(comparison_key(value))


def _matches(words: Sequence[str], tokens: frozenset[str]) -> bool:
    return any(word.startswith(token) for word in words for token in tokens)
