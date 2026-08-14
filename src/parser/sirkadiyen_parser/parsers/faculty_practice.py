"""Grade 3 faculty-practice rotation parser.

This is the source ADR-073 defers the Grade 3 faculty practice to. The annual
workbook writes the rotation as eight rows titled ``Öğretim üyesi Uygulama 1``
to ``8`` per curriculum block, and excludes them, because a student attends one
of the eight and publishing all of them would book them into seven hours that
are not theirs. This workbook says which one.

One worksheet holds eight of these rotations, one per curriculum block, and the
two curriculum groups order their blocks differently, so a block is found by its
own title rather than by where it sits. Each is written the same way::

    DÖNEM-3 HAREKET 2 DİLİMİ - UYGULAMA PROGRAMI (11.10 - 12.10 Uygulaması)
    Anabilim/Bilim Dalları | FİZİK TEDAVİ | FİZİK TEDAVİ | ORTOPEDİ | …
    TARİH
    46286                  | A1           | A2           | A3       | …

A column is a department and a cell is the cohort sitting with it that day, so
one cell is one session. A department may hold several columns at once, which is
ordinary: it means several cohorts are with it in parallel.

Two rules decide what reaches a calendar.

**A hyphen enumerates, it does not span.** ``A1-A2`` is A1 and A2, not A1
through A2. The workbooks prove it themselves: read that way, 127 of their 128
date rows state each of the eight cohorts exactly once, and no row anywhere
needs the other reading.

**A row that contradicts itself is refused per cohort, not whole.** The one
failing row writes ``A4`` twice and omits ``A8``, and one of those two cells is
the missing cohort — but nothing in the document says which, so both are
refused. The other six cohorts are stated once each and are published. Refusing
the row entirely would take a correct session off six calendars to punish one
typo, and repairing it from the rotation's pattern would be inventing a fact the
source does not state.
"""

import re
from collections.abc import Iterator, Sequence
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
    ScheduleAudienceCandidate,
    ScheduleEventType,
    SourceEvidence,
)
from sirkadiyen_parser.contracts.snapshot import NormalizedWorksheet
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.identity import build_identity_components, content_hash, stable_identity
from sirkadiyen_parser.normalization.courses import course_identity
from sirkadiyen_parser.normalization.dates import (
    DateResolution,
    NumericDateOrder,
    resolve_cell_date,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid, a1_address
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text
from sirkadiyen_parser.normalization.times import TimeRangeResolution, resolve_time_range_text
from sirkadiyen_parser.parsers.annual import DIMENSION_CURRICULUM_GROUP, encode_all_day
from sirkadiyen_parser.profiles import ParserProfileDefinition

#: The date sits in the first column of every block, and the departments follow.
DATE_COLUMN = 0
FIRST_DEPARTMENT_COLUMN = 1

DIMENSION_FACULTY_PRACTICE_GROUP = "facultyPracticeGroup"

#: How many cohorts a curriculum group is divided into for this practice. It is
#: also the width of the rotation: every date row states each of them once.
COHORT_COUNT = 8

#: The curriculum groups this source states, one cohort letter each. Bounded for
#: the same reason `cohort_rotation.py` bounds its alphabet: an unbounded letter
#: rule reads the letters of an ordinary word as cohorts.
COHORT_LETTERS = "AB"

#: A cohort is a group letter and a one-digit index, and nothing else. The index
#: is bounded too, so a stray ``A9`` is refused rather than published to a cohort
#: that does not exist.
_COHORT_PATTERN = re.compile(rf"^([{COHORT_LETTERS}])([1-{COHORT_COUNT}])$")

#: What separates two cohorts written into one cell. A hyphen is included on
#: purpose and means "and": see the module docstring.
_COHORT_SEPARATORS = re.compile(r"[-/+,;&]|\s+")

#: The practice hour, which a block title states only inside its parenthesis:
#: ``… UYGULAMA PROGRAMI (11.10 - 12.10 Uygulaması)``. The range is matched
#: rather than the whole parenthesis, because the source writes a word after it.
_BLOCK_HOURS_PATTERN = re.compile(r"\(\s*(\d{1,2}\s*[.:]\s*\d{2}\s*-\s*\d{1,2}\s*[.:]\s*\d{2})")

#: A block title states the block and then says it is a practice program. The
#: curriculum block is what comes before that phrase, with the grade prefix the
#: source repeats on every one of them removed.
_BLOCK_TITLE_PATTERN = re.compile(
    r"^\s*(?:DÖNEM\s*-?\s*\d+\s*)?(.+?)\s*-\s*UYGULAMA\s+PROGRAMI\b",
    re.IGNORECASE,
)

#: The header row that names the departments. The two workbooks separate the two
#: words differently — one writes ``Anabilim/Bilim Dalları`` and the other
#: ``Anabilim\Bilim Dalları`` — so the words are matched and the punctuation is
#: not.
_DEPARTMENT_HEADER_PATTERN = re.compile(r"^anabilim\s*[\\/-]?\s*bilim\s+dallari$")

DATE_MARKER_KEYS = frozenset({"tarih", "date"})

#: A cell holding only punctuation, which is how the source writes "no cohort is
#: with this department today". It states an absence deliberately, so it is
#: counted rather than reported as something the reader failed to understand.
_PLACEHOLDER_PATTERN = re.compile(r"^[\W_]+$")

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_BLOCKS_READ = "blocks.read"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CELLS_SCANNED = "cells.scanned"

REASON_BLANK_ROW = "blankRow"
REASON_TOPIC_LIST_ROW = "topicListRow"
REASON_ROW_OUTSIDE_BLOCK = "rowOutsideBlock"
REASON_UNRESOLVED_DATE = "unresolvedDate"
REASON_UNRESOLVED_COHORT = "unresolvedCohort"
REASON_NO_COHORT_STATED = "noCohortStated"
REASON_AMBIGUOUS_COHORT = "ambiguousCohort"
REASON_COHORT_NOT_STATED = "cohortNotStated"
REASON_MIXED_COHORT_LETTERS = "mixedCohortLetters"
REASON_MISSING_DEPARTMENT = "missingDepartment"

WARNING_NO_BLOCK = "noFacultyPracticeBlock"
WARNING_UNRESOLVED_BLOCK_HOURS = "unresolvedBlockPracticeHours"

RULE_BLOCK_TITLE = "facultyPractice.blockTitle"
RULE_DEPARTMENT_HEADER = "facultyPractice.departmentHeader"
RULE_DATE_CELL = "facultyPractice.dateCell"
RULE_COHORT_CELL = "facultyPractice.cohortCell"
RULE_ROW = "facultyPractice.row"

#: How the practice is titled, since the source states no title of its own: the
#: department is the only thing that distinguishes one cohort's hour from
#: another's. The topic lists between the blocks are deliberately not joined to
#: it — they name their departments differently from the matrix headers, and
#: matching them by resemblance would put one department's topics on another's
#: session.
TITLE_PREFIX = "Öğretim üyesi uygulaması"


@dataclass(frozen=True, slots=True)
class _Block:
    """One curriculum block's rotation, as its title and header row state it."""

    curriculum_block: str
    title_row: int
    start: time
    end: time
    departments: dict[int, str] = field(default_factory=dict)

    @property
    def is_readable(self) -> bool:
        return bool(self.departments)


@dataclass(frozen=True, slots=True)
class _CohortCell:
    """One matrix cell: the cohorts it names and where it sits."""

    row_index: int
    column_index: int
    department: str
    cohorts: tuple[str, ...]


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    blocks_read: int = 0


def parse_faculty_practice_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a Grade 3 faculty-practice rotation snapshot into candidate sessions."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    selected = 0

    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        if _parse_worksheet(
            worksheet=worksheet,
            grid=grid,
            context=request.source_context,
            numeric_date_order=profile.numeric_date_order,
            diagnostics=diagnostics,
            accumulator=accumulator,
        ):
            selected += 1

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    diagnostics.set_metric(METRIC_BLOCKS_READ, accumulator.blocks_read)
    diagnostics.set_metric(METRIC_CANDIDATES_EMITTED, len(accumulator.candidates))

    if accumulator.blocks_read == 0:
        diagnostics.error(
            WARNING_NO_BLOCK,
            "No worksheet in the snapshot states a faculty-practice block title, so "
            "the snapshot cannot be parsed by this profile.",
        )

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


def _parse_worksheet(
    *,
    worksheet: NormalizedWorksheet,
    grid: WorksheetGrid,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> bool:
    """Read one worksheet, classifying every row exactly once.

    Returns whether the worksheet held a block at all, so a worksheet that is
    something else entirely is reported rather than counted as empty.
    """
    block: _Block | None = None
    held_a_block = False

    for row_index in range(worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)
        first = grid.text(row_index, DATE_COLUMN)
        key = comparison_key(first)

        if not any(grid.text(row_index, column) for column in range(worksheet.column_count)):
            diagnostics.record_ignored_row(
                REASON_BLANK_ROW,
                grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_ROW),
            )
            continue

        title = _read_block_title(first, grid, row_index, diagnostics)
        if title is not None:
            block = title
            held_a_block = True
            accumulator.blocks_read += 1
            continue

        if _DEPARTMENT_HEADER_PATTERN.match(key):
            if block is not None:
                block = _with_departments(block, grid, row_index, worksheet.column_count)
            else:
                diagnostics.record_ignored_row(
                    REASON_ROW_OUTSIDE_BLOCK,
                    grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_DEPARTMENT_HEADER),
                )
            continue

        if key in DATE_MARKER_KEYS:
            # The marker only announces that the date rows follow.
            diagnostics.record_ignored_row(
                REASON_TOPIC_LIST_ROW,
                grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_ROW),
            )
            continue

        resolved = resolve_cell_date(
            grid.resolve(row_index, DATE_COLUMN).cell,
            numeric_order=numeric_date_order,
        )
        if block is not None and block.is_readable:
            if resolved.resolved:
                _parse_date_row(
                    worksheet=worksheet,
                    grid=grid,
                    row_index=row_index,
                    block=block,
                    resolved_date=resolved,
                    context=context,
                    diagnostics=diagnostics,
                    accumulator=accumulator,
                )
                continue

            if _states_cohorts(grid, row_index, block):
                # A row inside a block that names cohorts is a rotation row
                # whatever its first cell says, so an unreadable date there
                # takes eight sessions off eight calendars and has to be
                # reported rather than counted as prose. One such row existed:
                # its date was a serial the workbook had labelled with a
                # currency format.
                diagnostics.record_ignored_row(
                    REASON_UNRESOLVED_DATE,
                    grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_DATE_CELL),
                    severity=ParserWarningSeverity.WARNING,
                    message=(
                        f"Row states cohorts of the '{block.curriculum_block}' rotation but "
                        f"its date cell '{first}' could not be read as a date "
                        f"({resolved.reason}), so none of its sessions were published."
                    ),
                )
                continue

        # Everything else between the blocks is the free-text topic list. It is
        # counted rather than read: its departments are worded differently from
        # the matrix headers, so joining the two would be a guess.
        diagnostics.record_ignored_row(
            REASON_TOPIC_LIST_ROW if block is not None else REASON_ROW_OUTSIDE_BLOCK,
            grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_ROW),
        )

    return held_a_block


def _states_cohorts(grid: WorksheetGrid, row_index: int, block: _Block) -> bool:
    """Whether a row names cohorts under the block's departments.

    This is what tells a rotation row from the prose between the blocks when its
    date cell cannot be read, and the topic lists never state a bare cohort.
    """
    return any(
        _read_cohorts(text) is not None
        for column in block.departments
        if (text := grid.text(row_index, column))
    )


def _read_block_title(
    first: str,
    grid: WorksheetGrid,
    row_index: int,
    diagnostics: ParseDiagnostics,
) -> _Block | None:
    """Read a block title row, or return ``None`` when this is not one."""
    match = _BLOCK_TITLE_PATTERN.match(first)
    if match is None:
        return None

    curriculum_block = normalize_text(match.group(1)) or first
    hours = _read_block_hours(first)
    if not hours.resolved or hours.start is None or hours.end is None:
        # Without the hour there is no session to publish, only the knowledge
        # that one exists, so the block is reported and its date rows fall
        # through to the topic-list count.
        diagnostics.warning(
            WARNING_UNRESOLVED_BLOCK_HOURS,
            f"Block title '{first}' states no readable practice hour "
            f"({hours.reason}), so none of its sessions could be published.",
            evidence=grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_BLOCK_TITLE),
        )
        return None

    return _Block(
        curriculum_block=curriculum_block,
        title_row=row_index,
        start=hours.start,
        end=hours.end,
    )


def _read_block_hours(title: str) -> TimeRangeResolution:
    match = _BLOCK_HOURS_PATTERN.search(title)
    return resolve_time_range_text(match.group(1) if match else "")


def _with_departments(
    block: _Block,
    grid: WorksheetGrid,
    row_index: int,
    column_count: int,
) -> _Block:
    """The same block, with the departments its header row names.

    A department may occupy several columns. That is not a merge and not a
    mistake: it means that many cohorts sit with it at the same hour.
    """
    departments = {
        column: text
        for column in range(FIRST_DEPARTMENT_COLUMN, column_count)
        if (text := grid.text(row_index, column))
    }
    return _Block(
        curriculum_block=block.curriculum_block,
        title_row=block.title_row,
        start=block.start,
        end=block.end,
        departments=departments,
    )


def _parse_date_row(
    *,
    worksheet: NormalizedWorksheet,
    grid: WorksheetGrid,
    row_index: int,
    block: _Block,
    resolved_date: DateResolution,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    cells = list(_cohort_cells(grid, row_index, block, diagnostics))
    placements: dict[str, list[_CohortCell]] = {}
    letters: set[str] = set()
    for cell in cells:
        for cohort in cell.cohorts:
            placements.setdefault(cohort, []).append(cell)
            letters.add(cohort[0])

    if not placements:
        return

    if len(letters) > 1:
        # Two curriculum groups in one rotation row is a structural change, not
        # a typo, and there is no safe half of it to publish.
        diagnostics.record_ignored_row(
            REASON_MIXED_COHORT_LETTERS,
            grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_ROW),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Row states cohorts of more than one curriculum group "
                f"({', '.join(sorted(letters))}), so none of it was published."
            ),
        )
        return

    letter = letters.pop()
    for cohort in _expected_cohorts(letter):
        occurrences = placements.get(cohort, [])
        if len(occurrences) == 1:
            accumulator.candidates.append(
                _build_candidate(
                    worksheet=worksheet,
                    cell=occurrences[0],
                    cohort=cohort,
                    letter=letter,
                    block=block,
                    resolved_date=resolved_date,
                    context=context,
                )
            )
        elif not occurrences:
            diagnostics.record_ignored_cell(
                REASON_COHORT_NOT_STATED,
                grid.evidence(row_index, DATE_COLUMN, extraction_rule=RULE_ROW),
                severity=ParserWarningSeverity.WARNING,
                message=(
                    f"Cohort {cohort} is not stated anywhere in this rotation row, so it "
                    "has no session on this date while every other cohort does."
                ),
            )
        else:
            addresses = ", ".join(
                grid.resolve(cell.row_index, cell.column_index).value_a1_address
                for cell in occurrences
            )
            for cell in occurrences:
                diagnostics.record_ignored_cell(
                    REASON_AMBIGUOUS_COHORT,
                    grid.evidence(
                        cell.row_index,
                        cell.column_index,
                        extraction_rule=RULE_COHORT_CELL,
                    ),
                    severity=ParserWarningSeverity.WARNING,
                    message=(
                        f"Cohort {cohort} is stated in more than one department this date "
                        f"({addresses}). Only one of them can be the session it attends and "
                        "the row does not say which, so neither was published."
                    ),
                )


def _cohort_cells(
    grid: WorksheetGrid,
    row_index: int,
    block: _Block,
    diagnostics: ParseDiagnostics,
) -> Iterator[_CohortCell]:
    """The distinct cohort cells of one date row.

    A merged cell is read once, at the column its value is stored in. Reading it
    per covered column would state the same cohort twice and make an ordinary
    two-cohort session look like the contradiction this profile refuses.
    """
    for column, department in sorted(block.departments.items()):
        resolved = grid.resolve(row_index, column)
        if resolved.is_merge_expanded:
            continue

        text = resolved.text
        if not text:
            continue

        diagnostics.increment(METRIC_CELLS_SCANNED)
        if _PLACEHOLDER_PATTERN.match(text):
            diagnostics.record_ignored_cell(
                REASON_NO_COHORT_STATED,
                grid.evidence(row_index, column, extraction_rule=RULE_COHORT_CELL),
            )
            continue

        cohorts = _read_cohorts(text)
        if cohorts is None:
            diagnostics.record_ignored_cell(
                REASON_UNRESOLVED_COHORT,
                grid.evidence(row_index, column, extraction_rule=RULE_COHORT_CELL),
                severity=ParserWarningSeverity.WARNING,
                message=(
                    f"Cell '{text}' does not name cohorts of this rotation, so the session "
                    "it describes was not published."
                ),
            )
            continue

        yield _CohortCell(
            row_index=row_index,
            column_index=column,
            department=department,
            cohorts=cohorts,
        )


def _read_cohorts(text: str) -> tuple[str, ...] | None:
    """The cohorts a cell names, or ``None`` when it names something else.

    Every separator enumerates, including the hyphen: ``A1-A2`` is the two
    cohorts A1 and A2 sitting with one department, not the run A1 through A2.
    """
    tokens = [token for token in _COHORT_SEPARATORS.split(text.strip()) if token]
    if not tokens:
        return None

    cohorts: list[str] = []
    for token in tokens:
        match = _COHORT_PATTERN.match(token.upper())
        if match is None:
            return None
        cohort = f"{match.group(1)}{match.group(2)}"
        if cohort not in cohorts:
            cohorts.append(cohort)
    return tuple(cohorts)


def _expected_cohorts(letter: str) -> tuple[str, ...]:
    return tuple(f"{letter}{index}" for index in range(1, COHORT_COUNT + 1))


def _build_candidate(
    *,
    worksheet: NormalizedWorksheet,
    cell: _CohortCell,
    cohort: str,
    letter: str,
    block: _Block,
    resolved_date: DateResolution,
    context: ParseSourceContext,
) -> CanonicalScheduleCandidate:
    local_date = _require_date(resolved_date)
    display_title = f"{TITLE_PREFIX} — {cell.department}"

    # The curriculum group is the cohort's own letter: the A workbook states
    # A1-A8 and the B workbook B1-B8, so the document says which half of the
    # class it belongs to without the catalog having to.
    curriculum_group = f"{context.class_year}-{letter}"
    audience = ScheduleAudienceCandidate(
        scope=AudienceScope.SELECTED_GROUPS,
        selectors=[
            AudienceSelector(dimension=DIMENSION_CURRICULUM_GROUP, value=curriculum_group),
            AudienceSelector(dimension=DIMENSION_FACULTY_PRACTICE_GROUP, value=cohort),
        ],
    )
    audience_key = (
        f"{DIMENSION_CURRICULUM_GROUP}={curriculum_group} "
        f"{DIMENSION_FACULTY_PRACTICE_GROUP}={cohort}"
    )

    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", local_date.isoformat()),
            ("startLocalTime", block.start.isoformat()),
            ("courseIdentity", course_identity(display_title) or ""),
            ("audience", audience_key),
        )
    )

    return CanonicalScheduleCandidate(
        candidate_id=(
            f"{worksheet.sheet_id}!R{cell.row_index + 1}C{cell.column_index + 1}:{cohort}"
        ),
        academic_year=context.academic_year,
        class_year=context.class_year,
        program_language=context.program_language,
        audience=audience,
        event_type=ScheduleEventType.FACULTY_PRACTICE,
        status=CandidateRecordStatus.SCHEDULED,
        normalized_course_identity=course_identity(display_title),
        display_title=display_title,
        local_date=local_date,
        start_local_time=block.start,
        end_local_time=block.end,
        is_all_day=False,
        time_zone_id=context.time_zone_id,
        instructor=None,
        # The room is stated in a separate lookup workbook this parse never
        # sees, and its department wording does not match these headers, so no
        # location is claimed rather than one guessed.
        location=None,
        curriculum_block=block.curriculum_block,
        departments=[cell.department],
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "audience": audience_key,
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": display_title,
                "eventType": ScheduleEventType.FACULTY_PRACTICE.value,
                "localDate": local_date.isoformat(),
                "isAllDay": encode_all_day(False),
                "startLocalTime": block.start.isoformat(),
                "endLocalTime": block.end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "curriculumBlock": block.curriculum_block,
                "departments": cell.department,
            }
        ),
        confidence=resolved_date.confidence,
        identity_components=identity_components,
        evidence=list(_cell_evidence(worksheet, cell, block)),
    )


def _cell_evidence(
    worksheet: NormalizedWorksheet,
    cell: _CohortCell,
    block: _Block,
) -> Sequence[SourceEvidence]:
    """Cite the cell, its date and the block title the hour was read from."""
    return (
        SourceEvidence(
            sheet_id=worksheet.sheet_id,
            sheet_title=worksheet.title,
            range=a1_address(cell.row_index, cell.column_index),
            raw_text=None,
            extraction_rule=RULE_COHORT_CELL,
        ),
        SourceEvidence(
            sheet_id=worksheet.sheet_id,
            sheet_title=worksheet.title,
            range=a1_address(cell.row_index, DATE_COLUMN),
            raw_text=None,
            extraction_rule=RULE_DATE_CELL,
        ),
        SourceEvidence(
            sheet_id=worksheet.sheet_id,
            sheet_title=worksheet.title,
            range=a1_address(block.title_row, DATE_COLUMN),
            raw_text=None,
            extraction_rule=RULE_BLOCK_TITLE,
        ),
    )


def _require_date(resolution: DateResolution) -> date:
    if resolution.value is None:  # pragma: no cover - guarded by the caller
        raise ValueError("A resolved date is required to build a candidate.")
    return resolution.value
