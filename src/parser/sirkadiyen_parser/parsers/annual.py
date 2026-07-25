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
from sirkadiyen_parser.normalization.dates import (
    DateResolution,
    NumericDateOrder,
    resolve_cell_date,
)
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

#: Words that name a whole-day closure. Matched on prefixes as whole words, so
#: `TATİL`/`TATİLİ` and `BAYRAM`/`BAYRAMI` are all caught.
CLOSURE_TOKENS = frozenset({"tatil", "bayram", "holiday"})

#: Closures the sources name without any closure word. `LABOR DAY` is the English
#: workbook's rendering of `İŞÇİ BAYRAMI`; both sources state it on the same date,
#: so this is read from the sources rather than from knowing the Turkish calendar.
CLOSURE_PHRASES = ("labor day", "labour day")

#: The all-day shape is decided by a title rule rather than read from a cell, so
#: it is published below full confidence with an explicit indicator.
CONFIDENCE_ALL_DAY_CLOSURE = 0.9

#: An all-day item has no start time, and identity components may not be empty,
#: so the slot states the shape instead. Timed identities are unaffected.
IDENTITY_ALL_DAY = "allDay"

REASON_BLANK_ROW = "blankRow"
REASON_OTHER_CLASS_YEAR = "otherClassYear"
REASON_UNRESOLVED_TERM = "unresolvedTerm"
REASON_MISSING_TITLE = "missingTitle"
REASON_MISSING_DATE = "missingDate"
REASON_UNRESOLVED_DATE = "unresolvedDate"
REASON_NO_TIME_AND_NO_CLOSURE = "noScheduledTimeAndNoClosure"
REASON_UNRESOLVED_START_TIME = "unresolvedStartTime"
REASON_UNRESOLVED_END_TIME = "unresolvedEndTime"
REASON_END_NOT_AFTER_START = "endTimeNotAfterStartTime"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"
REASON_OUT_OF_SCOPE_SUBJECT = "outOfScopeSubject"
REASON_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER = "outOfScopePracticePlaceholder"
REASON_OUT_OF_SCOPE_GROUP_ROTATION = "outOfScopeGroupRotation"
REASON_NON_TEACHING_BREAK = "nonTeachingBreak"

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
METRIC_CANDIDATES_ALL_DAY = "candidates.allDayClosure"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
#: Counted per published lesson so a source that changes how it writes dates —
#: from a serial to numeric text, say — is visible before a reader has to notice
#: it row by row.
METRIC_DATE_RULE_PREFIX = "dates.rule."
METRIC_LOCATION_DEFERRED = "location.deferredToOtherProgram"
METRIC_CURRICULUM_BLOCK_STATED = "curriculumBlock.stated"
METRIC_DEPARTMENTS_STATED = "departments.stated"
METRIC_DEPARTMENTS_INTEGRATED_SESSION = "departments.integratedSession"
METRIC_DEPARTMENTS_LIST_MEMBER = "departments.unmarkedListMember"
METRIC_DEPARTMENTS_IGNORED_UNMARKED = "departments.ignored.unmarkedSegment"
#: Rows excluded because their authoritative detail belongs to the group-specific
#: practice source (ADR-030/071), and breaks excluded because they are not lessons.
METRIC_ROWS_OUT_OF_SCOPE_SUBJECT = "rows.ignored.outOfScopeSubject"
METRIC_ROWS_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER = "rows.ignored.outOfScopePracticePlaceholder"
METRIC_ROWS_OUT_OF_SCOPE_GROUP_ROTATION = "rows.ignored.outOfScopeGroupRotation"
METRIC_ROWS_NON_TEACHING_BREAK = "rows.ignored.nonTeachingBreak"

RULE_HEADER_ALIAS = "annual.headerAlias"
RULE_TERM_CELL = "annual.termCell"
RULE_DATE_CELL = "annual.dateCell"
RULE_START_TIME_CELL = "annual.startTimeCell"
RULE_END_TIME_CELL = "annual.endTimeCell"
RULE_TITLE_CELL = "annual.titleCell"
RULE_BLOCK_CELL = "annual.blockCell"
RULE_LOCATION_CELL = "annual.locationCell"
RULE_ROW = "annual.row"
RULE_ALL_DAY_CLOSURE = "annual.allDayClosureTitle"

#: Titles whose first token marks a non-teaching entry. Checked before the
#: teaching keywords, because "SERBEST ÇALIŞMA (Dönem 2 Sınav)" is free study
#: during another year's examination, not an examination.
NON_TEACHING_FIRST_TOKENS = frozenset(
    {"serbest", "free", "tatil", "bayram", "holiday", "ogle", "lunch", "ara", "arasi", "break"}
)

#: Availability blocks that are useful on a student's calendar but do not book
#: the student into teaching. Source-authored overlaps between these blocks are
#: therefore informational rather than duplicate lessons.
FREE_STUDY_FIRST_TOKENS = frozenset({"serbest", "free"})

#: Subjects owned by another program's schedule (ADR-030). PDÖ/PBL problem-based
#: learning is group-specific and published by the practice source, so an annual
#: row naming it is excluded rather than shown to the whole class, where it would
#: overlap the parallel lecture the rest of the cohort attends.
OUT_OF_SCOPE_SUBJECT_TOKENS = frozenset({"pdo", "pbl"})

#: A one-token annual title is only a whole-class slot marker. The companion
#: practice source owns its group-specific lesson name and audience. Longer titles
#: such as "Anatomi Uygulama 14/21" are real lessons and are deliberately retained.
PRACTICE_PLACEHOLDER_TITLES = frozenset({("uygulama",), ("practice",)})

#: Non-teaching break blocks. Free study ("serbest"/"free") is a real whole-class
#: entry and is deliberately kept, so it is NOT in this set.
BREAK_FIRST_TOKENS = frozenset({"ogle", "lunch", "ara", "arasi", "break"})

EXAM_TOKENS = frozenset({"sinav", "exam"})
ANATOMY_TOKENS = frozenset({"diseksiyon", "dissection"})
INTEGRATED_TOKENS = frozenset({"entegre", "integrated"})
PRACTICE_TOKENS = frozenset({"uygulama", "practice", "lab"})
VERTICAL_CORRIDOR_TOKENS = frozenset({"dikey", "vertical"})

#: Locations that point at another published program instead of naming a room.
#: They are counted for source-quality diagnostics but are never published as
#: event locations: an instruction to consult another table is not a place.
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
class _Schedule:
    """When a row happens: a time range, or the whole local date (ADR-046)."""

    start: time | None
    end: time | None
    confidence: float

    @property
    def all_day(self) -> bool:
        """Whether the row occupies its date instead of a time range."""
        return self.start is None


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
            numeric_date_order=profile.numeric_date_order,
            group_rotation_subjects=frozenset(profile.group_rotation_subjects),
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
    numeric_date_order: NumericDateOrder,
    group_rotation_subjects: frozenset[str],
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
        draft = _parse_row(
            row=row,
            context=context,
            numeric_date_order=numeric_date_order,
            group_rotation_subjects=group_rotation_subjects,
            diagnostics=diagnostics,
        )
        if draft is None:
            continue

        _accept(draft=draft, diagnostics=diagnostics, accumulator=accumulator)


def _parse_row(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    group_rotation_subjects: frozenset[str],
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

    exclusion = _out_of_scope_exclusion(title_text, group_rotation_subjects)
    if exclusion is not None:
        reason, message = exclusion
        diagnostics.record_ignored_row(
            reason,
            row.row_evidence(extraction_rule=RULE_ROW),
            message=message,
        )
        return None

    resolved_date = _resolve_date(
        row=row,
        numeric_date_order=numeric_date_order,
        diagnostics=diagnostics,
    )
    if resolved_date is None:
        return None

    schedule = _resolve_schedule(row=row, title_text=title_text, diagnostics=diagnostics)
    if schedule is None:
        return None

    return _build_draft(
        row=row,
        context=context,
        title_text=title_text,
        resolved_date=resolved_date,
        schedule=schedule,
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


def _resolve_date(
    *,
    row: _RowContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> DateResolution | None:
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
    resolution = resolve_cell_date(cell, numeric_order=numeric_date_order)
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


def _resolve_schedule(
    *,
    row: _RowContext,
    title_text: str,
    diagnostics: ParseDiagnostics,
) -> _Schedule | None:
    start_text = row.text(ROLE_START_TIME)
    end_text = row.text(ROLE_END_TIME)

    if not start_text and not end_text:
        return _resolve_all_day(row=row, title_text=title_text, diagnostics=diagnostics)

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

    return _Schedule(
        start=start.value,
        end=end.value,
        confidence=min(start.confidence, end.confidence),
    )


def _resolve_all_day(
    *,
    row: _RowContext,
    title_text: str,
    diagnostics: ParseDiagnostics,
) -> _Schedule | None:
    """Read a dated row whose time pair is empty.

    The sources write a holiday or a semester break as a dated row with no times,
    one row per closed day, and that becomes an all-day item (ADR-046). A row that
    states no times and names no closure states no schedule this profile can
    publish; inventing a time for it would put a lesson on a student's calendar at
    an hour nobody chose.
    """
    if states_closure(title_text):
        return _Schedule(start=None, end=None, confidence=CONFIDENCE_ALL_DAY_CLOSURE)

    diagnostics.record_ignored_row(
        REASON_NO_TIME_AND_NO_CLOSURE,
        row.row_evidence(extraction_rule=RULE_ROW),
        severity=ParserWarningSeverity.WARNING,
        message=(
            f"Row states a date and the title '{title_text}' but no times, and the title "
            "names no holiday or semester break, so it was published neither as a lesson "
            "nor as an all-day item."
        ),
    )
    return None


def states_closure(title: str) -> bool:
    """Report whether a title names a holiday or a semester break.

    A title alone never makes an entry a closure. This is read only for a row that
    states a date and no times at all, because that conjunction is the source's own
    statement that nothing is taught that day. The same words appear on timed rows —
    `CUMHURİYET BAYRAMI AREFESİ` is three real hours of teaching, and the English
    workbook writes its semester break as timed rows — and those are published as
    the source states them.
    """
    key = comparison_key(title)
    if any(phrase in key for phrase in CLOSURE_PHRASES):
        return True
    return _matches(_words(title), CLOSURE_TOKENS)


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
    schedule: _Schedule,
) -> _CandidateDraft:
    candidate_id = f"{row.worksheet.sheet_id}!R{row.row_index + 1}"

    course_title = normalize_course_title(title_text)
    split = split_trailing_instructors(course_title.display_title)
    display_title = split.title or course_title.display_title
    instructor = ", ".join(split.instructors) if split.instructors else None

    block_text = row.text(ROLE_BLOCK) or None
    raw_location = row.text(ROLE_LOCATION) or None
    deferred_location = raw_location is not None and _is_deferred_location(raw_location)
    location_text = None if deferred_location else raw_location

    # Classification still reads the whole cell. The block and the department
    # both carry keywords the event type depends on, and splitting the cell must
    # not silently reclassify a lesson. A closure is not teaching of any kind,
    # so its type follows from its shape rather than from its keywords.
    event_type = (
        ScheduleEventType.OTHER
        if schedule.all_day
        else classify_event_type(title=display_title, block=block_text)
    )
    block_departments = resolve_block_and_departments(block_text)

    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", resolved_date.value.isoformat() if resolved_date.value else ""),
            (
                "startLocalTime",
                IDENTITY_ALL_DAY if schedule.start is None else schedule.start.isoformat(),
            ),
            ("courseIdentity", course_identity(display_title) or ""),
        )
    )

    confidence = min(
        resolved_date.confidence,
        schedule.confidence,
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
        start_local_time=schedule.start,
        end_local_time=schedule.end,
        is_all_day=schedule.all_day,
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
            schedule=schedule,
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
        deferred_location=deferred_location,
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
    schedule: _Schedule,
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
            "isAllDay": encode_all_day(schedule.all_day),
            "startLocalTime": None if schedule.start is None else schedule.start.isoformat(),
            "endLocalTime": None if schedule.end is None else schedule.end.isoformat(),
            "timeZoneId": context.time_zone_id,
            "instructor": instructor,
            "location": location,
            "curriculumBlock": block_departments.curriculum_block,
            "departments": join_departments(block_departments.departments),
        }
    )


def encode_all_day(all_day: bool) -> str:
    """Encode the schedule shape for hashing.

    Both shapes are always present in the hash, so a timed item cannot silently
    become all-day without moving its content hash (ADR-046).
    """
    return "true" if all_day else "false"


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
    diagnostics.increment(f"{METRIC_DATE_RULE_PREFIX}{resolved_date.rule}")
    if draft.deferred_location:
        diagnostics.increment(METRIC_LOCATION_DEFERRED)

    if candidate.is_all_day:
        # The shape came from a title rule rather than from a cell, so every
        # all-day item says so on its own record.
        diagnostics.increment(METRIC_CANDIDATES_ALL_DAY)
        diagnostics.confidence(
            field_name="isAllDay",
            score=CONFIDENCE_ALL_DAY_CLOSURE,
            reason=RULE_ALL_DAY_CLOSURE,
            candidate_id=candidate.candidate_id,
        )

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

    if candidate.start_local_time is None or candidate.end_local_time is None:
        # An all-day item has no duration to find implausible.
        return

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


def _out_of_scope_exclusion(
    title: str,
    group_rotation_subjects: frozenset[str],
) -> tuple[str, str] | None:
    """Return ``(reason, message)`` when a row must be excluded, else ``None``.

    Four exclusions apply to the whole-class annual program:

    - **PDÖ/PBL** problem-based learning is group-specific and published by the
      practice source (ADR-030). An annual row naming it would otherwise be shown to
      the whole class and overlap the parallel lecture the rest of the cohort attends.
    - A subject the profile declares as a **group rotation**: the annual program
      states every slot of it, and a companion source assigns each student exactly
      one (ADR-073). Grade 2 dissection is written as three consecutive daily slots
      for one session, so publishing them all would book every student into two
      sessions they must not attend.
    - A one-token **UYGULAMA/PRACTICE** title is a whole-class slot placeholder.
      The companion practice source publishes the real group-specific lesson.
    - A **lunch or interval break** is not a lesson. Free study is deliberately kept.

    The check is deterministic and every excluded row is accounted for through the
    ignored-row record (which counts a ``rows.ignored.<reason>`` metric), never
    dropped silently (AI_GUIDELINE §9).
    """
    words = _words(title)
    if not words:
        return None
    if any(word in OUT_OF_SCOPE_SUBJECT_TOKENS for word in words):
        return (
            REASON_OUT_OF_SCOPE_SUBJECT,
            "Row names PDÖ/PBL problem-based learning, which is group-specific and "
            "published by the practice source, so it was not added to the whole-class "
            "annual program.",
        )
    if group_rotation_subjects and _matches(words, group_rotation_subjects):
        return (
            REASON_OUT_OF_SCOPE_GROUP_ROTATION,
            "Row names a subject this profile declares as a group rotation. The annual "
            "program states every slot of the rotation while a student attends exactly "
            "one, so the slots were not published to the whole class; the companion "
            "group source owns them.",
        )
    if tuple(words) in PRACTICE_PLACEHOLDER_TITLES:
        return (
            REASON_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER,
            "Row is a generic whole-class practice placeholder. The companion "
            "practice source publishes the authoritative group-specific lesson, "
            "so the placeholder was not added to the annual program.",
        )
    if words[0] in BREAK_FIRST_TOKENS:
        return (
            REASON_NON_TEACHING_BREAK,
            "Row is a non-teaching break (for example a lunch break), so no lesson was published.",
        )
    return None


def classify_event_type(*, title: str, block: str | None) -> ScheduleEventType:
    """Classify a lesson from its title and curriculum block.

    The order of the tests is the rule. A title that names an examination is an
    examination even when it also names a practice, and a non-teaching entry
    such as free study is never reclassified by a word inside a parenthesis.
    """
    title_words = _words(title)
    block_words = _words(block or "")
    all_words = title_words + block_words

    if title_words and title_words[0] in FREE_STUDY_FIRST_TOKENS:
        return ScheduleEventType.FREE_STUDY

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
