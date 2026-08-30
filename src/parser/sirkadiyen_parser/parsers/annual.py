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
    AudienceSelector,
    CandidateRecordStatus,
    CanonicalScheduleCandidate,
    ParserProfileDescriptor,
    ParserWarningSeverity,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ParseSourceContext,
    ProgramLanguage,
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
    resolve_group_departments,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.instructors import split_trailing_instructors
from sirkadiyen_parser.normalization.text import comparison_key
from sirkadiyen_parser.normalization.times import (
    TimeResolution,
    duration_minutes,
    resolve_cell_time,
)
from sirkadiyen_parser.parsers.amphitheatre import (
    AmphitheatreAssignment,
    AmphitheatreDocument,
    AmphitheatreIndex,
    RoomResolution,
    read_amphitheatre_document,
)
from sirkadiyen_parser.parsers.bedside import read_bedside_document
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

#: How many rows below the header an unlabelled term column is probed for. A few
#: rows are enough to find the first value a column states, and staying shallow
#: keeps the probe from reaching data that has nothing to do with the header.
TERM_COLUMN_PROBE_ROWS = 5

#: Header text is matched on folded comparison keys, so casing, Turkish letters
#: and spacing differences in the source do not break detection.
HEADER_ALIASES: Mapping[str, frozenset[str]] = {
    ROLE_TERM: frozenset({"donem", "term", "time table", "timetable", "class"}),
    ROLE_DATE: frozenset({"tarih", "start date", "date"}),
    ROLE_START_TIME: frozenset({"baslama saati", "baslangic saati", "start time", "start"}),
    ROLE_END_TIME: frozenset({"bitis saati", "end time", "end"}),
    ROLE_TITLE: frozenset({"konu", "subject", "ders", "course", "course name"}),
    ROLE_BLOCK: frozenset(
        {
            "dilim adi / anabilim dali",
            "dilim adi/anabilim dali",
            "dilim adi",
            "description",
            "aciklama",
            "department",
            # The Grade 3 English workbook misspells its own header, and matching
            # what the source wrote is what reads the column (ADR-017).
            "departmend",
        }
    ),
    ROLE_LOCATION: frozenset({"yer", "location", "place", "amfi"}),
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
#: A rotation row a fallback-capable profile left to its companion because the
#: companion has published that date (ADR-126). It is counted apart from
#: `outOfScopeGroupRotation` so "the group list owns this day" and "this profile
#: never publishes rotations" are never read as the same decision.
REASON_GROUP_ROTATION_COVERED = "groupRotationCoveredByCompanion"
REASON_NON_TEACHING_BREAK = "nonTeachingBreak"
REASON_UNRESOLVED_CURRICULUM_GROUP = "unresolvedCurriculumGroup"
REASON_AUDIENCE_NOT_OWNED = "audienceNotOwnedBySource"

WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"
WARNING_IMPLAUSIBLE_DURATION = "implausibleLessonDuration"
WARNING_WEEKDAY_MISMATCH = "weekdayMismatch"
WARNING_NO_HEADER_ROW = "worksheetWithoutHeaderRow"
WARNING_NO_WORKSHEET = "noParsableWorksheet"
WARNING_UNMARKED_BLOCK_SEGMENT = "unmarkedBlockDepartmentSegment"
WARNING_GROUP_ROTATION_FALLBACK = "groupRotationPublishedWithoutCompanion"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED_NO_HEADER = "worksheets.ignored.noHeaderRow"
METRIC_ROWS_SCANNED = "rows.scanned"

#: How many bedside topics the companion documents supplied for this workbook.
METRIC_COMPANION_TOPICS = "companion.bedsideTopics"
METRIC_ROWS_HIDDEN = "rows.hidden"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATES_ALL_DAY = "candidates.allDayClosure"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
#: Counted per published lesson so a source that changes how it writes dates —
#: from a serial to numeric text, say — is visible before a reader has to notice
#: it row by row.
METRIC_DATE_RULE_PREFIX = "dates.rule."
METRIC_LOCATION_DEFERRED = "location.deferredToOtherProgram"

#: How many lessons took their room from the weekly amphitheatre companion,
#: and how many could not. Every deferred location is accounted for by exactly
#: one of these, so "the room is still missing" is a number rather than a guess.
METRIC_AMPHITHEATRE_ASSIGNMENTS = "companion.amphitheatreAssignments"
METRIC_LOCATION_FROM_AMPHITHEATRE = "location.fromAmphitheatreProgram"
METRIC_LOCATION_UNRESOLVED_PREFIX = "location.amphitheatreUnresolved."
METRIC_CURRICULUM_BLOCK_STATED = "curriculumBlock.stated"
METRIC_DEPARTMENTS_STATED = "departments.stated"

#: Departments taken from the title rather than from the block cell, which is
#: where the Grade 3 sources state the department each half of the class sits
#: with (ADR-113).
METRIC_DEPARTMENTS_FROM_TITLE = "departments.statedInTitle"
METRIC_DEPARTMENTS_INTEGRATED_SESSION = "departments.integratedSession"
METRIC_DEPARTMENTS_LIST_MEMBER = "departments.unmarkedListMember"
METRIC_DEPARTMENTS_IGNORED_UNMARKED = "departments.ignored.unmarkedSegment"
#: Rows excluded because their authoritative detail belongs to the group-specific
#: practice source (ADR-030/071), and breaks excluded because they are not lessons.
METRIC_ROWS_OUT_OF_SCOPE_SUBJECT = "rows.ignored.outOfScopeSubject"
METRIC_ROWS_OUT_OF_SCOPE_PRACTICE_PLACEHOLDER = "rows.ignored.outOfScopePracticePlaceholder"
METRIC_ROWS_OUT_OF_SCOPE_GROUP_ROTATION = "rows.ignored.outOfScopeGroupRotation"
METRIC_ROWS_GROUP_ROTATION_COVERED = "rows.ignored.groupRotationCoveredByCompanion"
#: Rotation slots this profile published itself because no companion had
#: published their date (ADR-126). Counted so the fallback's reach is a number a
#: reviewer can read rather than something inferred from the candidate total.
METRIC_ROWS_GROUP_ROTATION_FALLBACK = "rows.publishedGroupRotationFallback"
#: Distinct dates that fallback covered, which is the count that matters
#: operationally: it is how many teaching days are missing a group list.
METRIC_GROUP_ROTATION_FALLBACK_DAYS = "groupRotationFallback.days"
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
RULE_CURRICULUM_GROUP_CELL = "annual.curriculumGroupCell"

#: The audience dimension a class split into parallel curriculum groups selects
#: on. Grade 3 runs two, each with its own workbook and its own timetable.
DIMENSION_CURRICULUM_GROUP = "curriculumGroup"

#: The curriculum groups this family states, bounded on purpose. The term cell is
#: prose (``Dönem 3A+3B Grubu``), so an unbounded letter rule would read the ``A``
#: of an ordinary word as a group and address a lesson to half the class. The same
#: reasoning bounds the cohort alphabet in `cohort_rotation.py`.
CURRICULUM_GROUP_LETTERS = "AB"

#: A class year immediately followed by one of those letters, and not by more
#: letters or digits, so ``3A`` reads as a group while ``3. Sınıf`` and ``A1`` do
#: not. Both parts are kept: the group is named ``3-A``, not ``A``, because the
#: same letter means a different cohort in a different year.
_CURRICULUM_GROUP_PATTERN = re.compile(
    rf"(?<!\d)(\d)\s*([{CURRICULUM_GROUP_LETTERS}])(?![A-Za-z0-9])"
)

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

#: How a fallback dissection slot names which of the day's consecutive hours it
#: is (ADR-126). The hour is what tells a student whether the event is theirs, and
#: without it the three copies are indistinguishable in a calendar view.
GROUP_ROTATION_SLOT_LABELS = {
    ProgramLanguage.TURKISH: "{ordinal}. saat",
    ProgramLanguage.ENGLISH: "Hour {ordinal}",
}

#: Separates the slot label from the title the source wrote. An em dash, so the
#: label reads as an addition rather than as part of the lesson name.
GROUP_ROTATION_SLOT_SEPARATOR = " — "

#: What a fallback slot says about itself. A student is seeing three consecutive
#: hours where they will attend one, and nothing else on the calendar could tell
#: them why, so the event says it in the program's own language.
GROUP_ROTATION_FALLBACK_NOTES = {
    ProgramLanguage.TURKISH: (
        "Bu tarih için anatomi salon grup programı henüz yayımlanmadı. Diseksiyonun "
        "üç saatinin üçü de takvime eklendi; kendi anatomi grubunuza ayrılan saate "
        "katılın. Grup programı sisteme yüklendiğinde yalnızca kendi saatiniz kalır."
    ),
    ProgramLanguage.ENGLISH: (
        "The anatomy group list for this date has not been published yet. All three "
        "dissection hours were added; attend the one assigned to your anatomy group. "
        "Only your own hour remains once the group list is uploaded."
    ),
}

EXAM_TOKENS = frozenset({"sinav", "exam"})
ANATOMY_TOKENS = frozenset({"diseksiyon", "dissection"})
INTEGRATED_TOKENS = frozenset({"entegre", "integrated"})

#: A practice held at a patient's bedside on a hospital ward, which the Grade 3
#: annual workbooks title `Hasta Başı Uygulama-N`. It is tested before the
#: general practice tokens because that title contains `Uygulama` too, and it is
#: a phrase because `hasta` alone is an ordinary word in a clinical title.
BEDSIDE_TOKENS = frozenset({"hasta basi", "bedside"})

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
    title_departments: tuple[str, ...]

    #: How the amphitheatre companion answered for this lesson, or ``None`` when
    #: it was not consulted (the row states its own room, or has no time at all).
    room_resolution: "RoomResolution | None" = None

    #: Everything needed to build this draft again, held only by a group-rotation
    #: slot published as a fallback (ADR-126). Which of the day's hours a slot is
    #: cannot be known while the row is read — it follows from the other rows of
    #: the same session — so the draft is built once without the label and once
    #: with it. Rebuilding is safe because building a draft is a pure function of
    #: these inputs and records nothing.
    rotation_rebuild: "_RotationRebuild | None" = None


@dataclass(frozen=True, slots=True)
class _RotationRebuild:
    """The inputs `_build_draft` read, kept so a slot can be labelled later."""

    row: _RowContext
    context: ParseSourceContext
    title_text: str
    resolved_date: DateResolution
    schedule: _Schedule
    audience: ScheduleAudienceCandidate
    bedside_topics: Mapping[tuple[str, date], str]
    amphitheatre: AmphitheatreIndex


@dataclass(slots=True)
class _Accumulator:
    """Candidates kept so far, indexed by stable identity for duplicate checks."""

    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)

    #: Block-cell segments already reported as naming no marked department. The
    #: same wording repeats on dozens of rows, so it is reported once with the
    #: first row that carries it and counted for the rest.
    reported_unmarked_segments: set[str] = field(default_factory=set)

    #: Dates whose group rotation this profile published itself because no
    #: companion had published them (ADR-126). Reported once for the snapshot
    #: rather than per row: 159 identical warnings would bury every other finding.
    group_rotation_fallback_dates: set[date] = field(default_factory=set)


def parse_annual_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a row-oriented annual program snapshot into candidate lessons."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    selected = 0

    # The English program is not divided into curriculum groups anywhere — it has
    # one cohort and one workbook — but 49 of its rows name the Turkish A group,
    # because those lectures are given jointly. Selecting on that group would
    # hide them from every English student, who has no such group to declare, so
    # for that program the term cell states only the class year (ADR-098).
    curriculum_group_audience = (
        DIMENSION_CURRICULUM_GROUP in profile.audience_dimensions
        and request.source_context.program_language is not ProgramLanguage.ENGLISH
    )

    bedside_topics = _read_companion_topics(request, profile, diagnostics)
    amphitheatre = _read_amphitheatre_companion(request, profile, diagnostics)

    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        columns = _detect_columns(
            grid,
            term_column_may_be_unlabelled=profile.term_column_may_be_unlabelled,
        )
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
            group_rotation_fallback=profile.group_rotation_fallback,
            group_rotation_covered_dates=frozenset(
                request.source_context.group_rotation_covered_dates
            ),
            curriculum_group_audience=curriculum_group_audience,
            bedside_topics=bedside_topics,
            amphitheatre=amphitheatre,
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

    if profile.group_rotation_fallback:
        _report_group_rotation_fallback(accumulator, request.source_context, diagnostics)

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


def _detect_columns(
    grid: WorksheetGrid,
    *,
    term_column_may_be_unlabelled: bool = False,
) -> tuple[int, dict[str, int]] | None:
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

        if (
            term_column_may_be_unlabelled
            and ROLE_TERM not in mapping
            and all(role in mapping for role in REQUIRED_ROLES if role != ROLE_TERM)
        ):
            term_column = _unlabelled_term_column(grid, row_index, mapping[ROLE_DATE])
            if term_column is not None:
                mapping[ROLE_TERM] = term_column
                return row_index, mapping

    return None


def _unlabelled_term_column(
    grid: WorksheetGrid,
    header_row: int,
    date_column: int,
) -> int | None:
    """The unlabelled column that states the term, or ``None``.

    Only columns left of the date are considered, and only the first value each
    of them states below the header is read: a term column says what it is on
    every row, so its first value already does. Exactly one column may qualify.
    Two would mean the layout states the term twice, or that something else in
    the row happens to read as a class year, and adopting either would be a
    guess about which one addresses the students.
    """
    candidates = [
        column_index
        for column_index in range(date_column)
        if _states_a_class_year(grid, header_row, column_index)
    ]
    return candidates[0] if len(candidates) == 1 else None


def _states_a_class_year(grid: WorksheetGrid, header_row: int, column: int) -> bool:
    limit = min(grid.worksheet.row_count, header_row + 1 + TERM_COLUMN_PROBE_ROWS)
    for row_index in range(header_row + 1, limit):
        text = grid.text(row_index, column)
        if text:
            return _read_class_year(text) is not None
    return False


def _parse_worksheet(
    *,
    worksheet: NormalizedWorksheet,
    grid: WorksheetGrid,
    header_row: int,
    columns: Mapping[str, int],
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    group_rotation_subjects: frozenset[str],
    group_rotation_fallback: bool,
    group_rotation_covered_dates: frozenset[date],
    curriculum_group_audience: bool,
    bedside_topics: Mapping[tuple[str, date], str],
    amphitheatre: AmphitheatreIndex,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    # A fallback rotation slot is labelled with its position among the other
    # slots of the same session, which is only known once the whole worksheet has
    # been read, so those drafts alone wait here and are accepted afterwards
    # (ADR-126). Every other row is accepted as it is read, so no profile without
    # the fallback sees any change in the order of its candidates or findings.
    pending_rotation: list[_CandidateDraft] = []

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
            group_rotation_fallback=group_rotation_fallback,
            group_rotation_covered_dates=group_rotation_covered_dates,
            curriculum_group_audience=curriculum_group_audience,
            bedside_topics=bedside_topics,
            amphitheatre=amphitheatre,
            diagnostics=diagnostics,
        )
        if draft is None:
            continue

        if draft.rotation_rebuild is not None:
            pending_rotation.append(draft)
            continue

        _accept(draft=draft, diagnostics=diagnostics, accumulator=accumulator)

    for draft in _label_rotation_slots(pending_rotation):
        # Accepted last, so a rotation slot can never displace a lesson the
        # source states in its own right: the earlier row keeps the identity.
        _accept(draft=draft, diagnostics=diagnostics, accumulator=accumulator)


def _parse_row(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    group_rotation_subjects: frozenset[str],
    group_rotation_fallback: bool,
    group_rotation_covered_dates: frozenset[date],
    curriculum_group_audience: bool,
    bedside_topics: Mapping[tuple[str, date], str],
    amphitheatre: AmphitheatreIndex,
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

    audience = _resolve_audience(
        row=row,
        context=context,
        curriculum_group_audience=curriculum_group_audience,
        diagnostics=diagnostics,
    )
    if audience is None:
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

    # A rotation row is only excluded outright by a profile that never publishes
    # rotations. A fallback-capable one has to know the row's date before it can
    # decide, because the companion owns some dates and not others (ADR-126), so
    # its rotation rows carry on to the date rules below.
    rotation_row = _states_group_rotation(title_text, group_rotation_subjects)
    exclusion = _out_of_scope_exclusion(
        title_text,
        frozenset() if group_rotation_fallback else group_rotation_subjects,
    )
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

    if rotation_row and resolved_date.value in group_rotation_covered_dates:
        # The group list has published this day, so it says which hour each
        # student attends and this row must not add the other two back.
        diagnostics.record_ignored_row(
            REASON_GROUP_ROTATION_COVERED,
            row.row_evidence(extraction_rule=RULE_ROW),
            message=(
                "Row names a group rotation the companion group source has already "
                "published for this date, so the slot was left to it rather than "
                "published to the whole class."
            ),
        )
        return None

    schedule = _resolve_schedule(row=row, title_text=title_text, diagnostics=diagnostics)
    if schedule is None:
        return None

    if rotation_row:
        # Published to the whole class on purpose: with no group list, a student
        # who sees nothing has no way to attend, while a student who sees all
        # three hours can read their own off the note the slot carries.
        return _build_draft(
            row=row,
            context=context,
            title_text=title_text,
            resolved_date=resolved_date,
            schedule=schedule,
            audience=audience,
            bedside_topics=bedside_topics,
            amphitheatre=amphitheatre,
            rotation_rebuild=_RotationRebuild(
                row=row,
                context=context,
                title_text=title_text,
                resolved_date=resolved_date,
                schedule=schedule,
                audience=audience,
                bedside_topics=bedside_topics,
                amphitheatre=amphitheatre,
            ),
        )

    return _build_draft(
        row=row,
        context=context,
        title_text=title_text,
        resolved_date=resolved_date,
        schedule=schedule,
        audience=audience,
        bedside_topics=bedside_topics,
        amphitheatre=amphitheatre,
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


def _read_companion_topics(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
    diagnostics: ParseDiagnostics,
) -> Mapping[tuple[str, date], str]:
    """The bedside topics the companion snapshots state, keyed by group and date.

    Empty whenever the profile declares no companion or none was supplied, and
    then this profile publishes exactly what it published before companions
    existed. That is deliberate: the annual program is the only source of these
    sessions' times, and it must never wait on a document it merely enriches
    from (ADR-102).
    """
    if not profile.companion_source_family or not request.auxiliary_snapshots:
        return {}

    topics: dict[tuple[str, date], str] = {}
    for snapshot in request.auxiliary_snapshots:
        document = read_bedside_document(
            snapshot,
            class_year=request.source_context.class_year,
            numeric_date_order=profile.companion_numeric_date_order,
        )
        topics.update(document.topics_by_date())

    diagnostics.set_metric(METRIC_COMPANION_TOPICS, len(topics))
    return topics


def _curriculum_groups(audience: ScheduleAudienceCandidate) -> tuple[str, ...]:
    """The curriculum groups a candidate's audience names, in selector order."""
    return tuple(
        selector.value
        for selector in audience.selectors
        if selector.dimension == DIMENSION_CURRICULUM_GROUP
    )


def _read_amphitheatre_companion(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
    diagnostics: ParseDiagnostics,
) -> AmphitheatreIndex:
    """The rooms the weekly amphitheatre companion states, indexed for lookup.

    Empty whenever the profile declares no amphitheatre companion or none was
    supplied, and then this profile publishes exactly what it published before
    companions existed (ADR-102). The annual program is the only source of these
    sessions and must not wait on a document that merely says where they are.

    Every auxiliary snapshot is offered to the reader. A snapshot of some other
    companion family states no dated room grid, so it yields nothing rather than
    being misread, and the metric below reports how much was actually found.
    """
    if not profile.amphitheatre_companion or not request.auxiliary_snapshots:
        return AmphitheatreIndex(AmphitheatreDocument())

    assignments: list[AmphitheatreAssignment] = []
    for snapshot in request.auxiliary_snapshots:
        document = read_amphitheatre_document(snapshot)
        assignments.extend(document.assignments)

    index = AmphitheatreIndex(AmphitheatreDocument(assignments=tuple(assignments)))
    diagnostics.set_metric(METRIC_AMPHITHEATRE_ASSIGNMENTS, len(index))
    return index


def _bedside_topic(
    topics: Mapping[tuple[str, date], str],
    audience: ScheduleAudienceCandidate,
    resolved_date: DateResolution,
) -> str | None:
    """The topic stated for this session, or ``None``.

    A session both curriculum groups attend would have two topics, one per
    group, and nothing says which of them this row means — so a row that names
    more than one group takes no topic rather than one of them.
    """
    if not topics or resolved_date.value is None:
        return None

    groups = [
        selector.value
        for selector in audience.selectors
        if selector.dimension == DIMENSION_CURRICULUM_GROUP
    ]
    if len(groups) != 1:
        return None

    return topics.get((groups[0], resolved_date.value))


def _stated_departments(
    *,
    block_departments: BlockDepartmentResolution,
    title_text: str,
    audience: ScheduleAudienceCandidate,
) -> tuple[str, ...]:
    """Every department the source states for this record, in source order.

    Most rows state their department in the block cell and nowhere else. The
    Grade 3 bedside and patient-practice rows state it in the title instead, once
    per half of the class — ``... A Grubu (İç H.) B Grubu (ÇSvH)`` — because one
    row carries the session both halves attend, each with its own department
    (ADR-113).

    Which of those pairs belongs on the record follows from who the record
    addresses, and is not a guess: a row published to one curriculum group takes
    the department stated for that group, and a program-wide row takes every
    department the title states, in the order the title states them, because it
    is published to all the groups named. Nothing is inferred for a group the
    title does not mention.

    The block cell keeps precedence in the order because it is where the
    convention puts the department; a department stated in both places is kept
    once.
    """
    stated = resolve_group_departments(title_text)
    if not stated:
        return block_departments.departments

    letters = _audience_group_letters(audience)
    departments = list(block_departments.departments)
    seen = {comparison_key(department) for department in departments}
    for letter, department in stated:
        if letters is not None and letter not in letters:
            continue
        key = comparison_key(department)
        if key in seen:
            continue
        seen.add(key)
        departments.append(department)

    return tuple(departments)


def _audience_group_letters(audience: ScheduleAudienceCandidate) -> frozenset[str] | None:
    """The curriculum-group letters a record is addressed to.

    ``None`` when it names no group, which means the record addresses the whole
    program rather than that it addresses nobody — the two must not be confused,
    because a title's departments apply to every group when no group narrows
    them.
    """
    letters = {
        selector.value.rpartition("-")[2].upper()
        for selector in audience.selectors
        if selector.dimension == DIMENSION_CURRICULUM_GROUP
    }
    return frozenset(letters) if letters else None


def _resolve_audience(
    *,
    row: _RowContext,
    context: ParseSourceContext,
    curriculum_group_audience: bool,
    diagnostics: ParseDiagnostics,
) -> ScheduleAudienceCandidate | None:
    """Who the row addresses, or ``None`` when it cannot be said.

    A workbook written for a whole program addresses all of its students, and
    that is every source but Grade 3. Grade 3 splits the class into two
    curriculum groups whose timetables differ, states which one each row belongs
    to in the same cell as the class year, and gives each group its own workbook
    — so a row read without its group would put the A group's lessons in a B
    student's calendar.

    A row that states no group is refused rather than widened to the whole
    program. Every row of the real workbooks that states a class year also names
    a group, so this refuses nothing today; if the source ever stops naming one,
    the reader must say so instead of addressing the wrong half of the class.

    The groups a row names are then narrowed to the ones this source owns
    (ADR-110). Both Grade 3 workbooks carry the sessions both halves of the class
    attend, each in its own wording, so publishing both copies puts the same
    session twice on a student's calendar. Narrowing leaves each workbook
    addressing only its own half, which is the half it was written for.
    """
    if not curriculum_group_audience:
        return ScheduleAudienceCandidate(scope=AudienceScope.ALL_STUDENTS_IN_PROGRAM)

    term_text = row.text(ROLE_TERM)
    groups = _read_curriculum_groups(term_text)
    if not groups:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_CURRICULUM_GROUP,
            row.evidence(ROLE_TERM, extraction_rule=RULE_CURRICULUM_GROUP_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Term cell '{term_text}' names no curriculum group, so the row could "
                "not be addressed to one half of the class and was not published."
            ),
        )
        return None

    owned = _narrow_to_owned(groups, context)
    if not owned:
        diagnostics.record_ignored_row(
            REASON_AUDIENCE_NOT_OWNED,
            row.evidence(ROLE_TERM, extraction_rule=RULE_CURRICULUM_GROUP_CELL),
            severity=ParserWarningSeverity.INFORMATION,
            message=(
                f"Term cell '{term_text}' addresses only curriculum group(s) "
                f"{', '.join(groups)}, which this source does not own, so the row was "
                "not published. The owning source states this session itself."
            ),
        )
        return None

    return ScheduleAudienceCandidate(
        scope=AudienceScope.SELECTED_GROUPS,
        selectors=[
            AudienceSelector(dimension=DIMENSION_CURRICULUM_GROUP, value=group) for group in owned
        ],
    )


def _narrow_to_owned(
    groups: tuple[str, ...],
    context: ParseSourceContext,
) -> tuple[str, ...]:
    """The stated groups this source is the authority for (ADR-110).

    A source that declares no authority over the dimension keeps every group it
    states, so a profile given no configuration produces exactly what it produced
    before ownership existed. Order follows the stated groups, which
    `_read_curriculum_groups` has already sorted, so the audience key stays stable.
    """
    owned = context.authoritative_audience_selectors.get(DIMENSION_CURRICULUM_GROUP)
    if owned is None:
        return groups

    permitted = frozenset(owned)
    return tuple(group for group in groups if group in permitted)


def _read_curriculum_groups(term_text: str) -> tuple[str, ...]:
    """The curriculum groups a term cell names, in a stable order.

    ``Dönem 3A Grubu`` names one. A session both groups attend is written
    ``Dönem 3A+3B Grubu``, ``Dönem 3A +3B Grubu``, ``Dönem 3B+3A Grubu`` and
    ``Dönem 3B/3A Grubu`` in the same workbook, and all four name the same pair;
    sorting makes the four spellings produce one identity rather than four.
    """
    matches = _CURRICULUM_GROUP_PATTERN.findall(term_text)
    return tuple(sorted({f"{year}-{letter}" for year, letter in matches}))


def _audience_key(audience: ScheduleAudienceCandidate) -> str:
    """Encode the audience for identity and content hashing.

    Empty when the row addresses the whole program, which keeps the identity and
    the hash of every source that states no audience exactly as they were.
    """
    return " ".join(f"{selector.dimension}={selector.value}" for selector in audience.selectors)


def _read_class_year(term_text: str) -> int | None:
    """Read the class year stated by a term cell.

    ``Dönem 1`` and ``Time Table 1`` both state year one. A cell holding no
    one- or two-digit number, several *different* ones, or a number outside the
    supported range states nothing usable and is never guessed at.

    Repeating the same year is not ambiguity. The Grade 3 workbooks write a
    session both groups attend as ``Dönem 3A+3B Grubu``, which states year three
    twice and nothing else; reading that as two class years would refuse about
    sixty real lessons per workbook. Two *different* years remain unreadable,
    because nothing in the cell says which one the row belongs to.
    """
    years = {int(match) for match in _CLASS_YEAR_PATTERN.findall(term_text)}
    if len(years) != 1:
        return None

    value = years.pop()
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
    audience: ScheduleAudienceCandidate,
    bedside_topics: Mapping[tuple[str, date], str],
    amphitheatre: AmphitheatreIndex,
    rotation_rebuild: "_RotationRebuild | None" = None,
    rotation_slot: int | None = None,
) -> _CandidateDraft:
    candidate_id = f"{row.worksheet.sheet_id}!R{row.row_index + 1}"
    audience_key = _audience_key(audience)

    course_title = normalize_course_title(title_text)
    split = split_trailing_instructors(course_title.display_title)
    display_title = split.title or course_title.display_title
    instructor = ", ".join(split.instructors) if split.instructors else None

    # A fallback rotation slot says which hour of the session it is (ADR-126).
    # The label is part of the title rather than of the note alone because a
    # calendar shows the title, and three identically named events an hour apart
    # are what a student would otherwise have to tell apart from the clock.
    if rotation_slot is not None:
        display_title += GROUP_ROTATION_SLOT_SEPARATOR + GROUP_ROTATION_SLOT_LABELS[
            context.program_language
        ].format(ordinal=rotation_slot)

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
    departments = _stated_departments(
        block_departments=block_departments,
        title_text=title_text,
        audience=audience,
    )

    # The workbook writes `AMFİ PROGRAMINA BAKINIZ` where a room would go, and
    # that instruction names the document this companion is (ADR-133). Resolving
    # it here rather than where the cell was read is what makes the department
    # available to match on: it is the fact that tells one of a cohort's two
    # lessons in an hour from the other.
    #
    # Only a deferred or absent location is filled. A room the workbook states
    # itself is what the source asserts, and a weekly grid does not overrule it.
    room = None
    if location_text is None and not schedule.all_day and resolved_date.value is not None:
        room = amphitheatre.resolve(
            local_date=resolved_date.value,
            class_year=context.class_year,
            program_language=context.program_language,
            curriculum_groups=_curriculum_groups(audience),
            departments=departments,
            start_local_time=schedule.start,
            end_local_time=schedule.end,
        )
        location_text = room.room

    # The bedside document states what each of these sessions is about, and this
    # workbook states when it is. Joining them on the date and the group is what
    # puts the topic on the event a student actually has (ADR-100). A session
    # whose topic the companion does not state keeps no note at all.
    notes = (
        _bedside_topic(bedside_topics, audience, resolved_date)
        if event_type is ScheduleEventType.BEDSIDE_PRACTICE
        else None
    )

    # A fallback slot explains itself instead of leaving a student to work out
    # why three dissections appeared on one afternoon (ADR-126). It is a note
    # like any other, so it is content and moves the event when it changes.
    if rotation_slot is not None:
        notes = GROUP_ROTATION_FALLBACK_NOTES[context.program_language]

    # The audience joins the identity only when the source states one. Two
    # curriculum groups share a date, a time and a title whenever they sit the
    # same examination, and without the audience those two rows would collapse
    # into one lesson addressed to whichever group was read second.
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
            *((("audience", audience_key),) if audience_key else ()),
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
        audience=audience,
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
        departments=list(departments),
        notes=notes,
        stable_identity=stable_identity(identity_components),
        content_hash=_content_hash(
            context=context,
            display_title=display_title,
            event_type=event_type,
            local_date=_require_date(resolved_date),
            schedule=schedule,
            instructor=instructor,
            location=location_text,
            curriculum_block=block_departments.curriculum_block,
            departments=departments,
            audience_key=audience_key,
            notes=notes,
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
        room_resolution=room,
        block_departments=block_departments,
        title_departments=departments[len(block_departments.departments) :],
        rotation_rebuild=rotation_rebuild,
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
    curriculum_block: str | None,
    departments: tuple[str, ...],
    audience_key: str,
    notes: str | None,
) -> str:
    # As in the identity: the audience is content only when the source states
    # one, so a program-wide source hashes exactly the keys it always did. The
    # note is added on the same terms, and for the same reason it belongs in the
    # hash at all: a corrected topic must move the event a student has.
    audience = {"audience": audience_key} if audience_key else {}
    note = {"notes": notes} if notes is not None else {}
    return content_hash(
        {
            **audience,
            **note,
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
            "curriculumBlock": curriculum_block,
            "departments": join_departments(departments),
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

    # Every consulted lesson lands in exactly one counter, so a missing room is
    # always explainable: it was matched, it was ambiguous, the document said
    # nothing about it, or the lesson named no department to match on.
    if draft.room_resolution is not None:
        if draft.room_resolution.room is not None:
            diagnostics.increment(METRIC_LOCATION_FROM_AMPHITHEATRE)
        else:
            diagnostics.increment(METRIC_LOCATION_UNRESOLVED_PREFIX + draft.room_resolution.reason)

    if draft.rotation_rebuild is not None:
        # Counted here rather than where the fallback was decided, so a slot
        # dropped as a duplicate leaves no counter no published candidate
        # accounts for (ADR-126).
        diagnostics.increment(METRIC_ROWS_GROUP_ROTATION_FALLBACK)
        accumulator.group_rotation_fallback_dates.add(candidate.local_date)

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
    if draft.title_departments:
        diagnostics.increment(METRIC_DEPARTMENTS_FROM_TITLE)
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


def _states_group_rotation(title: str, group_rotation_subjects: frozenset[str]) -> bool:
    """Whether this title names a subject the profile declares as a rotation."""
    if not group_rotation_subjects:
        return False
    words = _words(title)
    return bool(words) and _matches(words, group_rotation_subjects)


def _label_rotation_slots(drafts: Sequence[_CandidateDraft]) -> list[_CandidateDraft]:
    """Name each fallback rotation slot's place among the hours of its session.

    A session is written as consecutive hours of one day carrying one title, so
    the hours of ``(date, course identity)`` are the slots a student chooses
    between, and their order is their start time. Ordering by start time rather
    than by row keeps the label stable if the source ever reorders its rows: the
    identity of these records is their start time, and the label must agree with
    it (ADR-126).

    The drafts are returned in the order they were read, labelled.
    """
    positions: dict[tuple[date, str], list[int]] = {}
    for index, draft in enumerate(drafts):
        if draft.rotation_rebuild is None:  # pragma: no cover - caller filters
            continue
        candidate = draft.candidate
        session = candidate.normalized_course_identity or candidate.display_title
        key = (candidate.local_date, session)
        positions.setdefault(key, []).append(index)

    if not positions:
        return list(drafts)

    labelled = list(drafts)
    for indices in positions.values():
        ordered = sorted(
            indices,
            key=lambda index: (
                labelled[index].candidate.start_local_time or time.min,
                index,
            ),
        )
        for ordinal, index in enumerate(ordered, start=1):
            rebuild = labelled[index].rotation_rebuild
            if rebuild is None:  # pragma: no cover - guarded by the collection above
                continue
            labelled[index] = _build_draft(
                row=rebuild.row,
                context=rebuild.context,
                title_text=rebuild.title_text,
                resolved_date=rebuild.resolved_date,
                schedule=rebuild.schedule,
                audience=rebuild.audience,
                bedside_topics=rebuild.bedside_topics,
                amphitheatre=rebuild.amphitheatre,
                rotation_rebuild=rebuild,
                rotation_slot=ordinal,
            )

    return labelled


def _report_group_rotation_fallback(
    accumulator: _Accumulator,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
) -> None:
    """Say once, for the whole snapshot, which days had no group list.

    Called only by a profile that declares the fallback, so a profile that never
    publishes a rotation reports no metric about one: a zero here means "no day
    was left uncovered", which is a different statement from "this profile does
    not do this at all".

    The count is the operational fact: it is how many teaching days a student is
    being shown every hour of instead of their own. It is a warning rather than
    information because it describes evidence that is missing, and a reviewer
    should see it without going looking (AI_GUIDELINE §9).
    """
    days = accumulator.group_rotation_fallback_dates
    diagnostics.set_metric(METRIC_GROUP_ROTATION_FALLBACK_DAYS, len(days))
    if not days:
        return

    diagnostics.warn(
        severity=ParserWarningSeverity.WARNING,
        code=WARNING_GROUP_ROTATION_FALLBACK,
        message=(
            f"No companion group source has published {len(days)} rotation day(s) "
            f"between {min(days).isoformat()} and {max(days).isoformat()}, so every "
            f"slot of those sessions was published to the whole "
            f"{context.program_language.value} class year {context.class_year} with "
            f"its hour named. Uploading the group list returns those days to it."
        ),
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

    if _matches(title_words, BEDSIDE_TOKENS):
        return ScheduleEventType.BEDSIDE_PRACTICE

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
    """Whether the title's words contain any of the declared tokens.

    A token may be several words, and then all of them must appear consecutively.
    Grade 3 needs that: its faculty rotation is titled ``Öğretim üyesi Uygulama
    N``, while ``Öğretim Üyesi`` on its own is how the source writes an academic
    title, so dozens of ordinary lectures name their lecturer as
    ``Dr. Öğretim Üyesi …`` inside the very cell this reads. Matching the first
    word alone would exclude those lectures from the calendar.
    """
    return any(_matches_token(words, token.split()) for token in tokens)


def _matches_token(words: Sequence[str], token_words: Sequence[str]) -> bool:
    if not token_words:
        return False
    span = len(token_words)
    return any(
        all(
            words[offset + position].startswith(token_word)
            for position, token_word in enumerate(token_words)
        )
        for offset in range(len(words) - span + 1)
    )
