"""Rotation-matrix practice program parser.

A practice program does not list lessons; it lists rotations. One worksheet
holds several blocks, one per curriculum block. Each block has its own header
row naming the practice subjects across the columns, and each following row
gives a date and a time range. A cell where a subject column meets a dated row
holds the group or groups attending that practice in that slot, so **a candidate
is a cell, not a row**.

The value in a cell decides which students receive a calendar event, so the
profile is stricter here than anywhere else. A cell it cannot fully read is
refused with its own warning and its own address. Refusing a cell loses one
practice session for one group; misreading it puts a lesson in the wrong
students' calendars, and nobody would see the mistake.
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
from sirkadiyen_parser.normalization.courses import course_identity, normalize_course_title
from sirkadiyen_parser.normalization.dates import DateResolution, resolve_cell_date
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.groups import GroupExpression, parse_group_expression
from sirkadiyen_parser.normalization.instructors import starts_with_academic_title
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text, text_lines
from sirkadiyen_parser.normalization.times import duration_minutes, resolve_cell_time_range
from sirkadiyen_parser.parsers.annual import (
    MAX_PLAUSIBLE_DURATION_MINUTES,
    MIN_PLAUSIBLE_DURATION_MINUTES,
    WARNING_IMPLAUSIBLE_DURATION,
    WARNING_WEEKDAY_MISMATCH,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition

#: The two columns every block header names, in this order.
DATE_HEADER_ALIASES = frozenset({"uygulama tarihi", "tarih", "practice date", "date"})
TIME_HEADER_ALIASES = frozenset({"saat", "saati", "time", "hour"})

#: Markers that end a block. Totals and topic lists follow the rotation rows and
#: are not schedule data.
BLOCK_TERMINATOR_KEYS = frozenset(
    {
        "uygulama sayisi",
        "uygulama konu basliklari",
        "practice count",
        "practice topics",
    }
)

#: A merged run at least this wide introduces a heading or a note rather than a
#: dated rotation row, so it ends the block above it.
HEADING_MERGE_WIDTH = 3

DIMENSION_PRACTICE_GROUP = "practiceGroup"
DIMENSION_PRACTICE_SUBGROUP = "practiceSubgroup"

VERTICAL_CORRIDOR_KEYS = frozenset({"dikey", "vertical"})
ANATOMY_KEYS = frozenset({"anatomi", "anatomy", "diseksiyon", "dissection"})

REASON_BLANK_ROW = "blankRow"
REASON_MISSING_DATE = "missingDate"
REASON_UNRESOLVED_DATE = "unresolvedDate"
REASON_MISSING_TIME = "missingTimeRange"
REASON_UNRESOLVED_TIME = "unresolvedTimeRange"
REASON_UNRESOLVED_GROUP = "unresolvedGroupExpression"
REASON_UNSUPPORTED_GROUP_VALUE = "unsupportedGroupValueShape"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_NO_BLOCK = "worksheetWithoutPracticeBlock"
WARNING_NESTED_TABLE = "nestedScheduleTable"
WARNING_BLOCK_WITHOUT_SUBJECTS = "blockWithoutSubjectColumns"
WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED_NO_BLOCK = "worksheets.ignored.noPracticeBlock"
METRIC_BLOCKS_DETECTED = "blocks.detected"
METRIC_BLOCKS_IGNORED_NO_SUBJECTS = "blocks.ignored.noSubjectColumns"
METRIC_NESTED_TABLES = "blocks.nestedTables"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_CELLS_SCANNED = "cells.scanned"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
METRIC_AUDIENCE_DIMENSION_PREFIX = "audience.dimension."

RULE_BLOCK_HEADING = "practice.blockHeading"
RULE_SUBJECT_HEADER = "practice.subjectHeader"
RULE_DATE_CELL = "practice.dateCell"
RULE_TIME_CELL = "practice.timeRangeCell"
RULE_GROUP_CELL = "practice.groupCell"
RULE_ROW = "practice.row"

#: A group value is either a whole practice group (``A``) or one subgroup of it
#: (``A2``). Anything else names a cohort this profile does not model.
_GROUP_VALUE_PATTERN = re.compile(r"^([A-Z])(\d?)$")

#: A trailing parenthetical in a group cell is a note about the session, not
#: part of the group expression.
_TRAILING_NOTE_PATTERN = re.compile(r"\s*\(([^()]*)\)\s*$")

_WORD_PATTERN = re.compile(r"[^\W_]+", re.UNICODE)


@dataclass(frozen=True, slots=True)
class _Block:
    """One rotation table: its heading row, header row, and subject columns."""

    heading_row: int | None
    header_row: int
    date_column: int
    time_column: int
    subject_columns: tuple[int, ...]
    end_row_exclusive: int


@dataclass(frozen=True, slots=True)
class _Subject:
    """A practice subject read from a block header column."""

    column: int
    display_title: str
    instructor: str | None
    block_title: str | None


@dataclass(frozen=True, slots=True)
class _Slot:
    """A dated row inside a block."""

    row_index: int
    resolved_date: DateResolution
    start: time
    end: time
    confidence: float


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)


def parse_practice_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a rotation-matrix practice snapshot into candidate lessons."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    selected = 0

    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        blocks = _detect_blocks(grid, diagnostics)
        if not blocks:
            diagnostics.increment(METRIC_WORKSHEETS_IGNORED_NO_BLOCK)
            diagnostics.information(
                WARNING_NO_BLOCK,
                f"Worksheet '{worksheet.title}' has no recognizable practice block header "
                "and was not parsed.",
                evidence=_worksheet_evidence(worksheet),
            )
            continue

        selected += 1
        for block in blocks:
            _parse_block(
                grid=grid,
                block=block,
                context=request.source_context,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    if selected == 0:
        diagnostics.error(
            WARNING_NO_BLOCK,
            "No worksheet in the snapshot exposes a practice block header, so the "
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
    return SourceEvidence(
        sheet_id=worksheet.sheet_id,
        sheet_title=worksheet.title,
        range=worksheet.requested_ranges[0] if worksheet.requested_ranges else "A1",
        raw_text=None,
        extraction_rule=RULE_BLOCK_HEADING,
    )


def _detect_blocks(grid: WorksheetGrid, diagnostics: ParseDiagnostics) -> tuple[_Block, ...]:
    """Find every rotation table in a worksheet.

    A block runs from its header row to the first terminator, the next header
    row that starts in the same date column, or the end of the worksheet.
    """
    worksheet = grid.worksheet
    headers = [
        (row_index, columns)
        for row_index in range(worksheet.row_count)
        if (columns := _header_columns(grid, row_index)) is not None
    ]
    if not headers:
        return ()

    blocks: list[_Block] = []
    for position, (header_row, (date_column, time_column)) in enumerate(headers):
        end_row = _block_end(
            grid=grid,
            header_row=header_row,
            date_column=date_column,
            later_headers=headers[position + 1 :],
        )
        nested_columns = _nested_table_columns(
            headers=headers,
            header_row=header_row,
            date_column=date_column,
            end_row_exclusive=end_row,
        )
        subject_columns = tuple(
            column
            for column in range(time_column + 1, worksheet.column_count)
            if grid.text(header_row, column) and (nested_columns is None or column < nested_columns)
        )

        diagnostics.increment(METRIC_BLOCKS_DETECTED)
        if nested_columns is not None:
            diagnostics.increment(METRIC_NESTED_TABLES)
            diagnostics.information(
                WARNING_NESTED_TABLE,
                "A schedule table nested inside another block starts here. Its columns "
                "are not read, because their rows state dates rather than groups.",
                evidence=grid.evidence(
                    header_row,
                    nested_columns,
                    extraction_rule=RULE_BLOCK_HEADING,
                ),
            )

        if not subject_columns:
            diagnostics.increment(METRIC_BLOCKS_IGNORED_NO_SUBJECTS)
            diagnostics.information(
                WARNING_BLOCK_WITHOUT_SUBJECTS,
                "A block header names no practice subject columns, so it carries no "
                "rotation to publish.",
                evidence=grid.evidence(header_row, date_column, extraction_rule=RULE_BLOCK_HEADING),
            )
            continue

        blocks.append(
            _Block(
                heading_row=_heading_row(grid, header_row),
                header_row=header_row,
                date_column=date_column,
                time_column=time_column,
                subject_columns=subject_columns,
                end_row_exclusive=end_row,
            )
        )

    return tuple(blocks)


def _header_columns(grid: WorksheetGrid, row_index: int) -> tuple[int, int] | None:
    """Return the date and time columns of a block header row."""
    date_column: int | None = None
    for column in range(grid.worksheet.column_count):
        key = comparison_key(grid.text(row_index, column))
        if not key:
            continue
        if date_column is None:
            if key in DATE_HEADER_ALIASES:
                date_column = column
            continue
        if key in TIME_HEADER_ALIASES:
            return date_column, column
    return None


def _block_end(
    *,
    grid: WorksheetGrid,
    header_row: int,
    date_column: int,
    later_headers: Sequence[tuple[int, tuple[int, int]]],
) -> int:
    next_sibling = next(
        (row for row, (column, _) in later_headers if column == date_column),
        grid.worksheet.row_count,
    )
    for row_index in range(header_row + 1, next_sibling):
        if _is_terminator(grid, row_index):
            return row_index
    return next_sibling


def _is_terminator(grid: WorksheetGrid, row_index: int) -> bool:
    resolved = grid.resolve(row_index, 0)
    if comparison_key(resolved.text) in BLOCK_TERMINATOR_KEYS:
        return True

    merged = resolved.merged_range
    if merged is None or not resolved.text:
        return False
    width = merged.end_column_index_exclusive - merged.start_column_index
    return width >= HEADING_MERGE_WIDTH


def _nested_table_columns(
    *,
    headers: Sequence[tuple[int, tuple[int, int]]],
    header_row: int,
    date_column: int,
    end_row_exclusive: int,
) -> int | None:
    """Return the first column owned by a table nested inside this block."""
    nested = [
        column
        for row, (column, _) in headers
        if header_row < row < end_row_exclusive and column > date_column
    ]
    return min(nested) if nested else None


def _heading_row(grid: WorksheetGrid, header_row: int) -> int | None:
    """Return the row holding the block title above a header row."""
    for row_index in range(header_row - 1, -1, -1):
        if grid.text(row_index, 0):
            return None if _header_columns(grid, row_index) is not None else row_index
    return None


def _parse_block(
    *,
    grid: WorksheetGrid,
    block: _Block,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    subjects = tuple(_read_subjects(grid, block))

    for row_index in range(block.header_row + 1, block.end_row_exclusive):
        diagnostics.increment(METRIC_ROWS_SCANNED)
        slot = _read_slot(grid=grid, block=block, row_index=row_index, diagnostics=diagnostics)
        if slot is None:
            continue

        for subject in subjects:
            diagnostics.increment(METRIC_CELLS_SCANNED)
            _parse_cell(
                grid=grid,
                block=block,
                slot=slot,
                subject=subject,
                context=context,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )


def _read_subjects(grid: WorksheetGrid, block: _Block) -> Iterator[_Subject]:
    for column in block.subject_columns:
        resolved = grid.resolve(block.header_row, column)
        lines = text_lines(resolved.display_text or "")
        if not lines:
            continue

        # A header cell wraps its subject over several lines and may end with a
        # parenthesised instructor that wraps too, so the lines are rejoined
        # before the trailing note is separated. A note that does not name an
        # instructor stays in the title rather than being discarded.
        joined = " ".join(lines)
        without_note, note = _split_trailing_note(joined)
        if note is not None and starts_with_academic_title(note):
            title_text, instructor = without_note, note
        else:
            title_text, instructor = joined, None

        title = normalize_course_title(title_text)
        if not title.display_title:
            continue

        heading = grid.text(block.heading_row, column) if block.heading_row is not None else ""
        yield _Subject(
            column=column,
            display_title=title.display_title,
            instructor=instructor,
            block_title=heading or None,
        )


def _read_slot(
    *,
    grid: WorksheetGrid,
    block: _Block,
    row_index: int,
    diagnostics: ParseDiagnostics,
) -> _Slot | None:
    date_text = grid.text(row_index, block.date_column)
    time_text = grid.text(row_index, block.time_column)
    has_values = any(grid.text(row_index, column) for column in block.subject_columns)

    if not date_text and not time_text and not has_values:
        diagnostics.record_ignored_row(
            REASON_BLANK_ROW,
            _row_evidence(grid, block, row_index),
        )
        return None

    if not date_text:
        # A row with group values but no date is an anomaly; an empty spacer row
        # that only carries a time is expected layout.
        diagnostics.record_ignored_row(
            REASON_MISSING_DATE,
            _row_evidence(grid, block, row_index),
            severity=(
                ParserWarningSeverity.WARNING if has_values else ParserWarningSeverity.INFORMATION
            ),
            message="Row states no practice date, so its cells could not be scheduled.",
        )
        return None

    resolved_date = resolve_cell_date(grid.resolve(row_index, block.date_column).cell)
    if not resolved_date.resolved or resolved_date.value is None:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_DATE,
            grid.evidence(row_index, block.date_column, extraction_rule=RULE_DATE_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Practice date '{date_text}' could not be read as a date "
                f"({resolved_date.reason}), so the whole row was not published."
            ),
        )
        return None

    if not time_text:
        diagnostics.record_ignored_row(
            REASON_MISSING_TIME,
            _row_evidence(grid, block, row_index),
            severity=ParserWarningSeverity.WARNING,
            message="Row states a practice date but no time range, so it was not published.",
        )
        return None

    time_range = resolve_cell_time_range(grid.resolve(row_index, block.time_column).cell)
    if not time_range.resolved or time_range.start is None or time_range.end is None:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_TIME,
            grid.evidence(row_index, block.time_column, extraction_rule=RULE_TIME_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Practice time range '{time_text}' could not be read "
                f"({time_range.reason}), so the whole row was not published."
            ),
        )
        return None

    return _Slot(
        row_index=row_index,
        resolved_date=resolved_date,
        start=time_range.start,
        end=time_range.end,
        confidence=min(resolved_date.confidence, time_range.confidence),
    )


def _row_evidence(grid: WorksheetGrid, block: _Block, row_index: int) -> SourceEvidence:
    columns = (block.date_column, block.time_column, *block.subject_columns)
    return grid.range_evidence(
        row_index,
        min(columns),
        row_index + 1,
        max(columns) + 1,
        extraction_rule=RULE_ROW,
    )


def _parse_cell(
    *,
    grid: WorksheetGrid,
    block: _Block,
    slot: _Slot,
    subject: _Subject,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    cell_text = grid.text(slot.row_index, subject.column)
    if not cell_text:
        return

    evidence = grid.evidence(slot.row_index, subject.column, extraction_rule=RULE_GROUP_CELL)
    # Only the first line states the groups; a second line names the room.
    group_text, _ = _split_trailing_note(text_lines(cell_text)[0])
    expression = parse_group_expression(
        group_text,
        dimension=DIMENSION_PRACTICE_GROUP,
        letter_groups=True,
    )

    if not expression.resolved:
        diagnostics.record_ignored_cell(
            REASON_UNRESOLVED_GROUP,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{cell_text}' does not state which groups attend "
                f"({expression.reason}), so no lesson was published for it."
            ),
        )
        return

    selectors = _audience_selectors(expression)
    if selectors is None:
        diagnostics.record_ignored_cell(
            REASON_UNSUPPORTED_GROUP_VALUE,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{cell_text}' names a cohort this profile does not model, so no "
                "lesson was published for it."
            ),
        )
        return

    candidate = _build_candidate(
        grid=grid,
        block=block,
        slot=slot,
        subject=subject,
        context=context,
        selectors=selectors,
        expression=expression,
        evidence=evidence,
    )
    _accept(
        candidate=candidate,
        evidence=evidence,
        slot=slot,
        expression=expression,
        grid=grid,
        block=block,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


def _split_trailing_note(text: str) -> tuple[str, str | None]:
    """Separate a trailing parenthetical note from the text before it."""
    normalized = normalize_text(text)
    match = _TRAILING_NOTE_PATTERN.search(normalized)
    if match is None:
        return normalized, None
    return normalized[: match.start()].strip(), match.group(1).strip() or None


def _audience_selectors(expression: GroupExpression) -> list[AudienceSelector] | None:
    """Turn a resolved group expression into audience selectors.

    ``A`` selects a whole practice group and ``A2`` selects one subgroup of it,
    so the two are reported under different dimensions. A value of any other
    shape names a cohort this profile does not model, and the caller refuses the
    cell rather than assigning it to the nearest dimension.
    """
    if expression.covers_all:
        return []

    selectors: list[AudienceSelector] = []
    for value in expression.values:
        match = _GROUP_VALUE_PATTERN.match(value)
        if match is None:
            return None
        dimension = DIMENSION_PRACTICE_SUBGROUP if match.group(2) else DIMENSION_PRACTICE_GROUP
        selectors.append(AudienceSelector(dimension=dimension, value=value))
    return selectors


def _build_candidate(
    *,
    grid: WorksheetGrid,
    block: _Block,
    slot: _Slot,
    subject: _Subject,
    context: ParseSourceContext,
    selectors: Sequence[AudienceSelector],
    expression: GroupExpression,
    evidence: SourceEvidence,
) -> CanonicalScheduleCandidate:
    covers_all = expression.covers_all
    local_date = _require_date(slot.resolved_date)
    event_type = classify_event_type(subject=subject.display_title, block=subject.block_title)
    audience_key = (
        "*"
        if covers_all
        else "+".join(f"{selector.dimension}:{selector.value}" for selector in selectors)
    )
    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", local_date.isoformat()),
            ("startLocalTime", slot.start.isoformat()),
            ("courseIdentity", course_identity(subject.display_title) or ""),
            ("audience", audience_key),
        )
    )

    return CanonicalScheduleCandidate(
        candidate_id=(f"{grid.worksheet.sheet_id}!R{slot.row_index + 1}C{subject.column + 1}"),
        academic_year=context.academic_year,
        class_year=context.class_year,
        program_language=context.program_language,
        audience=ScheduleAudienceCandidate(
            scope=(
                AudienceScope.ALL_STUDENTS_IN_PROGRAM
                if covers_all
                else AudienceScope.SELECTED_GROUPS
            ),
            selectors=list(selectors),
        ),
        event_type=event_type,
        status=CandidateRecordStatus.SCHEDULED,
        normalized_course_identity=course_identity(subject.display_title),
        display_title=subject.display_title,
        local_date=local_date,
        start_local_time=slot.start,
        end_local_time=slot.end,
        time_zone_id=context.time_zone_id,
        instructor=subject.instructor,
        location=None,
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": subject.display_title,
                "eventType": event_type.value,
                "localDate": local_date.isoformat(),
                "startLocalTime": slot.start.isoformat(),
                "endLocalTime": slot.end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "instructor": subject.instructor,
                "block": subject.block_title,
                "audience": audience_key,
            }
        ),
        confidence=min(slot.confidence, expression.confidence),
        identity_components=identity_components,
        evidence=[
            grid.evidence(block.header_row, subject.column, extraction_rule=RULE_SUBJECT_HEADER),
            grid.evidence(slot.row_index, block.date_column, extraction_rule=RULE_DATE_CELL),
            grid.evidence(slot.row_index, block.time_column, extraction_rule=RULE_TIME_CELL),
            evidence,
        ],
    )


def _require_date(resolution: DateResolution) -> date:
    if resolution.value is None:  # pragma: no cover - guarded by the caller
        raise ValueError("A resolved date is required to build a candidate.")
    return resolution.value


def _accept(
    *,
    candidate: CanonicalScheduleCandidate,
    evidence: SourceEvidence,
    slot: _Slot,
    expression: GroupExpression,
    grid: WorksheetGrid,
    block: _Block,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    existing = accumulator.by_identity.get(candidate.stable_identity)
    if existing is not None:
        severity = (
            ParserWarningSeverity.INFORMATION
            if existing.content_hash == candidate.content_hash
            else ParserWarningSeverity.WARNING
        )
        diagnostics.record_ignored_cell(
            REASON_DUPLICATE_IDENTITY,
            evidence,
            severity=severity,
            message=(
                f"Cell repeats the lesson already published as candidate "
                f"'{existing.candidate_id}', so it was not published again."
            ),
        )
        if severity is ParserWarningSeverity.WARNING:
            diagnostics.warn(
                severity=ParserWarningSeverity.WARNING,
                code=WARNING_CONFLICTING_DUPLICATE,
                message=(
                    "Two cells share one lesson identity but disagree on content. "
                    "The first occurrence was kept."
                ),
                candidate_id=existing.candidate_id,
                evidence=evidence,
            )
        return

    accumulator.by_identity[candidate.stable_identity] = candidate
    accumulator.candidates.append(candidate)

    diagnostics.increment(f"{METRIC_CANDIDATE_EVENT_TYPE_PREFIX}{candidate.event_type.value}")
    for selector in candidate.audience.selectors:
        diagnostics.increment(f"{METRIC_AUDIENCE_DIMENSION_PREFIX}{selector.dimension}")

    if slot.resolved_date.confidence < 1.0:
        diagnostics.confidence(
            field_name="localDate",
            score=slot.resolved_date.confidence,
            reason=slot.resolved_date.reason or slot.resolved_date.rule,
            candidate_id=candidate.candidate_id,
        )

    if expression.confidence < 1.0:
        # The audience decides whose calendar changes, so a value read under a
        # cohort-model rule rather than as written is always reported.
        diagnostics.confidence(
            field_name="audience",
            score=expression.confidence,
            reason=expression.rule,
            candidate_id=candidate.candidate_id,
        )

    if slot.resolved_date.weekday_matches is False:
        diagnostics.warning(
            WARNING_WEEKDAY_MISMATCH,
            f"Date cell names weekday '{slot.resolved_date.weekday_text}' but "
            f"{candidate.local_date.isoformat()} is a different weekday.",
            candidate_id=candidate.candidate_id,
            evidence=grid.evidence(
                slot.row_index,
                block.date_column,
                extraction_rule=RULE_DATE_CELL,
            ),
        )

    duration = duration_minutes(candidate.start_local_time, candidate.end_local_time)
    if not MIN_PLAUSIBLE_DURATION_MINUTES <= duration <= MAX_PLAUSIBLE_DURATION_MINUTES:
        diagnostics.warning(
            WARNING_IMPLAUSIBLE_DURATION,
            f"Practice lasts {duration} minutes, outside the plausible range of "
            f"{MIN_PLAUSIBLE_DURATION_MINUTES} to {MAX_PLAUSIBLE_DURATION_MINUTES} minutes.",
            candidate_id=candidate.candidate_id,
            evidence=evidence,
        )


def classify_event_type(*, subject: str, block: str | None) -> ScheduleEventType:
    """Classify a practice from its subject and its curriculum block."""
    words = _words(subject) + _words(block or "")
    if any(word.startswith(key) for word in words for key in ANATOMY_KEYS):
        return ScheduleEventType.ANATOMY_PRACTICE
    if any(word.startswith(key) for word in _words(block or "") for key in VERTICAL_CORRIDOR_KEYS):
        return ScheduleEventType.VERTICAL_CORRIDOR
    return ScheduleEventType.PRACTICE


def _words(value: str) -> list[str]:
    return _WORD_PATTERN.findall(comparison_key(value))
