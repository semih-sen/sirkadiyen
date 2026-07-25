"""Slot-column practice program parser.

This is the second rotation layout the faculty publishes, and it is the
transpose of the one :mod:`sirkadiyen_parser.parsers.practice` reads. There, a
row is a dated slot and a column is a practice subject. Here, a **column is a
dated slot** — its header cell holds a slot label, a date and a time range on
separate lines — and a **row is a practice subject**, naming the subject in the
first column and its room in the second. The cell where they meet holds the
group or groups attending, so a candidate is still a cell.

One worksheet holds several curriculum blocks. A block opens with a wide merged
heading (``KAN LENFOİD 1``), carries one or more slot-header rows, and is
followed by topic lists that are not schedule data. Every row of the worksheet
is classified exactly once, so ``rows.scanned`` equals the worksheet's row count
and no region disappears unexplained.

Two rules are stricter here than in the annual profiles, because this source is
the one that decides *which* students receive an event:

- A slot whose header states a weekday that contradicts its own date is refused.
  These headers are typed by hand, the weekday is the only corroboration the
  cell carries, and the real workbook contains four dates whose year is a year
  out — each caught exactly this way. Correcting the year would be inference.
- A cell that states a session but not who attends — a bare ``*``, a make-up
  marker — publishes nothing. Refusing loses one session for one group;
  publishing it to everyone puts a lesson in the wrong students' calendars.
"""

import re
from collections.abc import Sequence
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
from sirkadiyen_parser.normalization.dates import (
    DateResolution,
    NumericDateOrder,
    resolve_date_text,
)
from sirkadiyen_parser.normalization.grid import ResolvedCell, WorksheetGrid
from sirkadiyen_parser.normalization.groups import GroupExpression, parse_group_expression
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text, text_lines
from sirkadiyen_parser.normalization.times import (
    TimeRangeResolution,
    duration_minutes,
    resolve_time_range_text,
)
from sirkadiyen_parser.parsers.annual import (
    MAX_PLAUSIBLE_DURATION_MINUTES,
    METRIC_DATE_RULE_PREFIX,
    MIN_PLAUSIBLE_DURATION_MINUTES,
    WARNING_IMPLAUSIBLE_DURATION,
    encode_all_day,
)
from sirkadiyen_parser.parsers.practice import (
    DIMENSION_PRACTICE_GROUP,
    DIMENSION_PRACTICE_SUBGROUP,
    OUT_OF_SCOPE_SUBJECT_KEYS,
)
from sirkadiyen_parser.parsers.practice import classify_event_type as classify_practice_type
from sirkadiyen_parser.profiles import ParserProfileDefinition

#: The first two columns of every slot-header row, which is how a header row is
#: told apart from a subject row. The second header is abbreviated to
#: ``Uygulama`` in one block of the real workbook.
SUBJECT_HEADER_ALIASES = frozenset({"uygulama adi", "practice name"})
PLACE_HEADER_ALIASES = frozenset({"uygulama yeri", "uygulama", "practice place", "place"})

SUBJECT_COLUMN = 0
PLACE_COLUMN = 1
FIRST_SLOT_COLUMN = 2

#: A merged run at least this wide opens a curriculum block.
HEADING_MERGE_WIDTH = 3

#: A curriculum block is a short name. The same wide merge also carries the
#: source's paragraph-long note about the skill practices, and reading that as a
#: block would put a paragraph on every event of the table below it.
MAX_HEADING_LENGTH = 60

#: This source writes eight lettered cohorts and concatenates them (``ABCD``),
#: so a run may be as long as the cohort list itself.
MAX_LETTER_RUN = 8

#: A trailing ``1/3`` in a group cell numbers the session within the subject's
#: own series. It is not part of the audience, and leaving it in would make the
#: whole cell unreadable rather than merely unlabelled.
_SESSION_MARKER_PATTERN = re.compile(r"\s*\d+\s*/\s*\d+\s*$")

#: The slot label that opens a header cell, on its own line.
_SLOT_LABEL_PATTERN = re.compile(r"^\d+\s*/\s*\d+$")

#: A cell stating that the slot holds no session for this subject.
_NO_SESSION_PATTERN = re.compile(r"^[-–—]+$")

#: A cell stating that a session happens but that its groups are published
#: elsewhere. The source says so in its own note: "Uygulama grupları ve uygulama
#: salonları ayrı bir tablo ile duyurulacaktır."
_ANNOUNCED_ELSEWHERE_PATTERN = re.compile(r"^\*+$")

#: A room that names a future announcement rather than a place.
DEFERRED_PLACE_KEYS = frozenset({"yayinlanacak", "announced"})

EXAM_KEYS = frozenset({"sinav", "exam"})

REASON_NOT_A_SUBJECT_ROW = "notAPracticeSubjectRow"
REASON_SUBJECT_ROW_WITHOUT_SLOTS = "subjectRowOutsideAnySlotTable"
REASON_UNRESOLVED_SLOT_DATE = "unresolvedSlotDate"
REASON_UNRESOLVED_SLOT_TIME = "unresolvedSlotTimeRange"
REASON_WEEKDAY_CONTRADICTS_DATE = "weekdayContradictsSlotDate"
REASON_GROUPS_ANNOUNCED_ELSEWHERE = "groupsAnnouncedElsewhere"
REASON_NO_SESSION = "noSessionInSlot"
REASON_OUT_OF_SCOPE_SUBJECT = "outOfScopeSubject"
REASON_OUT_OF_SCOPE_GROUP_ROTATION = "outOfScopeGroupRotation"
REASON_UNRESOLVED_GROUP = "unresolvedGroupExpression"
REASON_UNSUPPORTED_GROUP_VALUE = "unsupportedGroupValueShape"
REASON_UNDATED_CELL = "cellOutsideAnyDatedSlot"
REASON_CELL_IN_REFUSED_SLOT = "cellInRefusedSlot"
REASON_UNRESOLVED_SELF_DATED_CELL = "unresolvedSelfDatedCell"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_NO_TABLE = "worksheetWithoutSlotTable"
WARNING_SLOT_REFUSED = "slotHeaderRefused"
WARNING_OUT_OF_SCOPE_SUBJECT = "outOfScopeSubjectRow"
WARNING_GROUP_ROTATION_SUBJECT = "groupRotationSubjectRow"
WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED_NO_TABLE = "worksheets.ignored.noSlotTable"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_ROWS_BLOCK_HEADING = "rows.blockHeading"
METRIC_ROWS_SLOT_HEADER = "rows.slotHeader"
METRIC_ROWS_SUBJECT = "rows.subject"
METRIC_SLOTS_DETECTED = "slots.detected"
METRIC_SLOTS_REFUSED_PREFIX = "slots.ignored."
METRIC_SUBJECTS_OUT_OF_SCOPE = "subjects.ignored.outOfScope"
METRIC_SUBJECTS_GROUP_ROTATION = "subjects.ignored.groupRotation"
METRIC_CELLS_SCANNED = "cells.scanned"
#: Cells of a merged run whose anchor already stated the session, so the
#: scanned total still reconciles with candidates plus ignored cells.
METRIC_CELLS_MERGE_CONTINUATION = "cells.mergeContinuation"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
METRIC_AUDIENCE_DIMENSION_PREFIX = "audience.dimension."
METRIC_CANDIDATES_SELF_DATED = "candidates.selfDatedCell"
METRIC_PLACE_DEFERRED = "place.deferredToAnnouncement"

RULE_BLOCK_HEADING = "practiceSlots.blockHeading"
RULE_SLOT_HEADER = "practiceSlots.slotHeader"
RULE_SUBJECT_CELL = "practiceSlots.subjectCell"
RULE_PLACE_CELL = "practiceSlots.placeCell"
RULE_GROUP_CELL = "practiceSlots.groupCell"
RULE_ROW = "practiceSlots.row"

#: A group value is either a whole practice group (``A``) or one subgroup of it
#: (``A2``), exactly as in the Grade 1 rotation table (ADR-020).
#:
#: The letter is bounded by the eight cohorts this source states (ADR-048), and
#: that bound is what makes reading a run such as ``ABCD`` safe: without it the
#: same rule reads the word ``SINAV`` as five cohorts, one of which is a real
#: group. A cell naming a letter outside the eight is refused with its address
#: rather than published to whichever cohorts happen to exist.
COHORT_LETTERS = "A-H"
_GROUP_VALUE_PATTERN = re.compile(rf"^([{COHORT_LETTERS}])(\d?)$")

_WORD_PATTERN = re.compile(r"[^\W_]+", re.UNICODE)


@dataclass(frozen=True, slots=True)
class _Slot:
    """One dated column of a slot table."""

    column: int
    header_row: int
    label: str | None
    local_date: date
    start: time
    end: time
    confidence: float
    date_rule: str


@dataclass(frozen=True, slots=True)
class _SlotTable:
    """A slot-header row and the dated columns it declares."""

    header_row: int
    heading: str | None
    slots: tuple[_Slot, ...]
    #: Columns whose header was refused. Their cells are still accounted for,
    #: but the refusal was already reported once with the header's address, so
    #: they are not warned about a second time each.
    refused_columns: frozenset[int]

    @property
    def slots_by_column(self) -> dict[int, _Slot]:
        return {slot.column: slot for slot in self.slots}


@dataclass(frozen=True, slots=True)
class _Subject:
    """A practice subject read from the first two columns of a row."""

    row_index: int
    display_title: str
    place: str | None
    deferred_place: bool
    out_of_scope: bool
    group_rotation: bool


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)


def parse_practice_slot_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a slot-column practice snapshot into candidate lessons."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    selected = 0

    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        if _parse_worksheet(
            grid=grid,
            context=request.source_context,
            profile=profile,
            diagnostics=diagnostics,
            accumulator=accumulator,
        ):
            selected += 1
            continue

        diagnostics.increment(METRIC_WORKSHEETS_IGNORED_NO_TABLE)
        diagnostics.information(
            WARNING_NO_TABLE,
            f"Worksheet '{worksheet.title}' has no recognizable slot-header row and "
            "was not parsed.",
            evidence=_worksheet_evidence(worksheet),
        )

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    if selected == 0:
        diagnostics.error(
            WARNING_NO_TABLE,
            "No worksheet in the snapshot exposes a slot-header row, so the snapshot "
            "cannot be parsed by this profile.",
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
        extraction_rule=RULE_SLOT_HEADER,
    )


def _parse_worksheet(
    *,
    grid: WorksheetGrid,
    context: ParseSourceContext,
    profile: ParserProfileDefinition,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> bool:
    """Walk a worksheet once, classifying every row. Returns whether it parsed.

    The single pass is deliberate. A block heading, a slot-header row and a
    subject row are recognized by their own shape rather than by a range
    computed in advance, so every row is accounted for exactly once and a
    structural surprise cannot silently swallow the rows after it.
    """
    heading: str | None = None
    table: _SlotTable | None = None
    parsed_any = False

    for row_index in range(grid.worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)

        block_heading = _read_block_heading(grid, row_index)
        if block_heading is not None:
            diagnostics.increment(METRIC_ROWS_BLOCK_HEADING)
            heading, table = block_heading, None
            continue

        if _is_slot_header_row(grid, row_index):
            diagnostics.increment(METRIC_ROWS_SLOT_HEADER)
            table = _read_slot_table(
                grid=grid,
                row_index=row_index,
                heading=heading,
                numeric_date_order=profile.numeric_date_order,
                diagnostics=diagnostics,
            )
            parsed_any = True
            continue

        subject = _read_subject(
            grid=grid,
            row_index=row_index,
            group_rotation_subjects=frozenset(profile.group_rotation_subjects),
        )
        if subject is None:
            diagnostics.record_ignored_row(
                REASON_NOT_A_SUBJECT_ROW,
                _row_evidence(grid, row_index),
            )
            continue

        if table is None:
            # A subject row with no slot-header row above it states a lesson
            # nothing can date. It is reported rather than assumed to belong to
            # the previous block's table.
            diagnostics.record_ignored_row(
                REASON_SUBJECT_ROW_WITHOUT_SLOTS,
                _row_evidence(grid, row_index),
                severity=ParserWarningSeverity.WARNING,
                message=(
                    f"Row states the practice subject '{subject.display_title}' but no "
                    "slot-header row precedes it in this block, so its cells could not "
                    "be dated."
                ),
            )
            continue

        diagnostics.increment(METRIC_ROWS_SUBJECT)
        _parse_subject_row(
            grid=grid,
            table=table,
            subject=subject,
            context=context,
            numeric_date_order=profile.numeric_date_order,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )

    return parsed_any


def _read_block_heading(grid: WorksheetGrid, row_index: int) -> str | None:
    """Return the curriculum block a row opens, if it opens one."""
    resolved = grid.resolve(row_index, SUBJECT_COLUMN)
    merged = resolved.merged_range
    if merged is None or not resolved.text:
        return None
    if merged.end_column_index_exclusive - merged.start_column_index < HEADING_MERGE_WIDTH:
        return None
    text = normalize_text(resolved.text)
    # A wide merge also carries the source's note about how the skill practices
    # are organized. It opens no block, and the rows below it keep the block
    # they already had.
    return text if len(text) <= MAX_HEADING_LENGTH else None


def _is_slot_header_row(grid: WorksheetGrid, row_index: int) -> bool:
    return (
        comparison_key(grid.text(row_index, SUBJECT_COLUMN)) in SUBJECT_HEADER_ALIASES
        and comparison_key(grid.text(row_index, PLACE_COLUMN)) in PLACE_HEADER_ALIASES
    )


def _read_slot_table(
    *,
    grid: WorksheetGrid,
    row_index: int,
    heading: str | None,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> _SlotTable:
    slots: list[_Slot] = []
    refused: set[int] = set()
    for column in range(FIRST_SLOT_COLUMN, grid.worksheet.column_count):
        header_text = grid.text(row_index, column)
        if not header_text:
            continue

        diagnostics.increment(METRIC_SLOTS_DETECTED)
        slot = _read_slot(
            grid=grid,
            row_index=row_index,
            column=column,
            numeric_date_order=numeric_date_order,
            diagnostics=diagnostics,
        )
        if slot is None:
            refused.add(column)
        else:
            slots.append(slot)

    return _SlotTable(
        header_row=row_index,
        heading=heading,
        slots=tuple(slots),
        refused_columns=frozenset(refused),
    )


def _read_slot(
    *,
    grid: WorksheetGrid,
    row_index: int,
    column: int,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> _Slot | None:
    """Read one dated column header, or refuse it with its address.

    The header wraps a slot label, a date and a time range over separate lines,
    and the date itself sometimes wraps. The time range is therefore taken from
    the last line and the date from everything between the label and it.
    """
    resolved = grid.resolve(row_index, column)
    lines = list(text_lines(resolved.display_text or ""))
    label = lines[0] if lines and _SLOT_LABEL_PATTERN.match(lines[0]) else None
    body = lines[1:] if label is not None else lines

    evidence = grid.evidence(row_index, column, extraction_rule=RULE_SLOT_HEADER)
    if not body:
        _refuse_slot(
            REASON_UNRESOLVED_SLOT_TIME,
            "Slot header states no date or time range, so its column was not read.",
            evidence=evidence,
            diagnostics=diagnostics,
        )
        return None

    time_range = resolve_time_range_text(body[-1])
    if not time_range.resolved or time_range.start is None or time_range.end is None:
        _refuse_slot(
            REASON_UNRESOLVED_SLOT_TIME,
            f"Slot header time range '{body[-1]}' could not be read "
            f"({time_range.reason}), so its column was not read.",
            evidence=evidence,
            diagnostics=diagnostics,
        )
        return None

    resolved_date = resolve_date_text(" ".join(body[:-1]), numeric_order=numeric_date_order)
    refusal = _date_refusal(resolved_date, " ".join(body[:-1]))
    if refusal is not None:
        reason, message = refusal
        _refuse_slot(reason, message, evidence=evidence, diagnostics=diagnostics)
        return None

    return _Slot(
        column=column,
        header_row=row_index,
        label=label,
        local_date=_require_date(resolved_date),
        start=time_range.start,
        end=time_range.end,
        confidence=min(resolved_date.confidence, time_range.confidence),
        date_rule=resolved_date.rule,
    )


def _date_refusal(
    resolved: DateResolution,
    date_text: str,
    *,
    subject: str = "Slot header",
) -> tuple[str, str] | None:
    """Return why a slot date may not be published, or ``None`` when it may.

    A stated weekday that contradicts its own date is refused rather than
    published with a warning. The dates in these headers are typed by hand, the
    weekday is the only thing in the cell that can corroborate them, and the
    real workbook proves the point: four of them name a year that is a year out,
    and every one of them disagrees with its own weekday. Publishing them would
    put practices a year in the past on real calendars and quarantine the whole
    revision for a date outside the academic year.
    """
    if not resolved.resolved or resolved.value is None:
        return (
            REASON_UNRESOLVED_SLOT_DATE,
            f"{subject} date '{date_text}' could not be read as a date "
            f"({resolved.reason}), so nothing was published from it.",
        )

    if resolved.weekday_matches is False:
        return (
            REASON_WEEKDAY_CONTRADICTS_DATE,
            f"{subject} states '{date_text}', but {resolved.value.isoformat()} is not a "
            f"'{resolved.weekday_text}'. The cell contradicts itself, so nothing was "
            "published from it; correcting either part would be a guess.",
        )

    return None


def _refuse_slot(
    reason: str,
    message: str,
    *,
    evidence: SourceEvidence,
    diagnostics: ParseDiagnostics,
) -> None:
    """Account for a slot column that will publish nothing."""
    diagnostics.increment(f"{METRIC_SLOTS_REFUSED_PREFIX}{reason}")
    diagnostics.warning(WARNING_SLOT_REFUSED, message, evidence=evidence)


def _read_subject(
    *,
    grid: WorksheetGrid,
    row_index: int,
    group_rotation_subjects: frozenset[str],
) -> _Subject | None:
    """Read the subject a row states, or ``None`` when it states none.

    A subject row names the practice in the first column and its room in the
    second. The topic lists below each table also carry text in the first
    column, but they either leave the second empty or span both through one
    merge, so the second column has to hold a value of its own.
    """
    subject_text = grid.text(row_index, SUBJECT_COLUMN)
    if not subject_text:
        return None

    place_cell = grid.resolve(row_index, PLACE_COLUMN)
    if not place_cell.text or _is_merge_continuation(place_cell):
        return None

    title = normalize_course_title(subject_text)
    if not title.display_title:
        return None

    place_text = normalize_text(place_cell.text)
    deferred_place = _states_future_announcement(place_text)
    words = _words(title.display_title)
    return _Subject(
        row_index=row_index,
        display_title=title.display_title,
        place=None if deferred_place else place_text or None,
        deferred_place=deferred_place,
        out_of_scope=any(word in OUT_OF_SCOPE_SUBJECT_KEYS for word in words),
        group_rotation=bool(group_rotation_subjects)
        and any(word.startswith(key) for word in words for key in group_rotation_subjects),
    )


def _is_merge_continuation(resolved: ResolvedCell) -> bool:
    """Whether a cell only repeats the value of the merge that covers it."""
    return resolved.merged_range is not None and resolved.value_a1_address != resolved.a1_address


def _states_future_announcement(place: str) -> bool:
    return any(word.startswith(key) for word in _words(place) for key in DEFERRED_PLACE_KEYS)


def _parse_subject_row(
    *,
    grid: WorksheetGrid,
    table: _SlotTable,
    subject: _Subject,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    if subject.out_of_scope:
        diagnostics.increment(METRIC_SUBJECTS_OUT_OF_SCOPE)
        diagnostics.information(
            WARNING_OUT_OF_SCOPE_SUBJECT,
            f"Subject '{subject.display_title}' is deliberately not synchronized "
            "(ADR-030), so its cells are counted as ignored rather than published.",
            evidence=grid.evidence(
                subject.row_index,
                SUBJECT_COLUMN,
                extraction_rule=RULE_SUBJECT_CELL,
            ),
        )

    if subject.group_rotation:
        diagnostics.increment(METRIC_SUBJECTS_GROUP_ROTATION)
        diagnostics.information(
            WARNING_GROUP_ROTATION_SUBJECT,
            f"Subject '{subject.display_title}' is a group rotation this profile defers "
            "to its own source (ADR-073); its cells state dates rather than groups, so "
            "they are counted as ignored rather than published.",
            evidence=grid.evidence(
                subject.row_index,
                SUBJECT_COLUMN,
                extraction_rule=RULE_SUBJECT_CELL,
            ),
        )

    if subject.deferred_place:
        diagnostics.increment(METRIC_PLACE_DEFERRED)

    slots = table.slots_by_column
    for column in range(FIRST_SLOT_COLUMN, grid.worksheet.column_count):
        resolved = grid.resolve(subject.row_index, column)
        if not resolved.text:
            continue

        diagnostics.increment(METRIC_CELLS_SCANNED)
        _parse_cell(
            grid=grid,
            resolved=resolved,
            table=table,
            slot=slots.get(column),
            subject=subject,
            context=context,
            numeric_date_order=numeric_date_order,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )


def _parse_cell(
    *,
    grid: WorksheetGrid,
    resolved: ResolvedCell,
    table: _SlotTable,
    slot: _Slot | None,
    subject: _Subject,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    text = normalize_text(resolved.text)
    evidence = grid.evidence(
        resolved.row_index,
        resolved.column_index,
        extraction_rule=RULE_GROUP_CELL,
    )

    if subject.out_of_scope or subject.group_rotation:
        diagnostics.record_ignored_cell(
            REASON_OUT_OF_SCOPE_GROUP_ROTATION
            if subject.group_rotation
            else REASON_OUT_OF_SCOPE_SUBJECT,
            evidence,
        )
        return

    if _ANNOUNCED_ELSEWHERE_PATTERN.match(text):
        # The source marks the date and says the groups and rooms follow in a
        # separate table. It states a session, not an audience, so nothing here
        # can decide whose calendar changes.
        diagnostics.record_ignored_cell(REASON_GROUPS_ANNOUNCED_ELSEWHERE, evidence)
        return

    if _NO_SESSION_PATTERN.match(text):
        diagnostics.record_ignored_cell(REASON_NO_SESSION, evidence)
        return

    self_dated = _read_self_dated_cell(
        lines=text_lines(resolved.display_text or ""),
        numeric_date_order=numeric_date_order,
    )
    if self_dated is not None:
        if _is_merge_continuation(resolved):
            # A whole-cohort session is written once across a merged run of
            # columns. Its date and time come from the cell, so the run is
            # presentation and only its anchor states the session. The rest of
            # the run is counted so the scanned cells still add up.
            diagnostics.increment(METRIC_CELLS_MERGE_CONTINUATION)
            return
        _publish_self_dated(
            grid=grid,
            resolved=resolved,
            table=table,
            subject=subject,
            context=context,
            self_dated=self_dated,
            evidence=evidence,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )
        return

    if slot is None:
        if resolved.column_index in table.refused_columns:
            # The header of this column was already refused with its own
            # warning and address, so the cell is counted rather than raising a
            # second alarm about the same cause.
            diagnostics.record_ignored_cell(REASON_CELL_IN_REFUSED_SLOT, evidence)
            return

        diagnostics.record_ignored_cell(
            REASON_UNDATED_CELL,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{text}' sits in a column whose slot header states no date, and "
                "the cell states none of its own, so no lesson was published for it."
            ),
        )
        return

    expression = _read_groups(text)
    selectors = _audience_selectors(expression)
    if not expression.resolved or selectors is None:
        diagnostics.record_ignored_cell(
            REASON_UNRESOLVED_GROUP if not expression.resolved else REASON_UNSUPPORTED_GROUP_VALUE,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{text}' does not state which groups attend "
                f"({expression.reason or 'unsupportedGroupValueShape'}), so no lesson "
                "was published for it."
            ),
        )
        return

    candidate = _build_candidate(
        grid=grid,
        table=table,
        subject=subject,
        context=context,
        local_date=slot.local_date,
        start=slot.start,
        end=slot.end,
        confidence=min(slot.confidence, expression.confidence),
        selectors=selectors,
        covers_all=expression.covers_all,
        cell_row=resolved.row_index,
        cell_column=resolved.column_index,
        cell_evidence=evidence,
        slot_evidence=grid.evidence(
            slot.header_row,
            slot.column,
            extraction_rule=RULE_SLOT_HEADER,
        ),
    )
    _accept(
        candidate=candidate,
        evidence=evidence,
        expression=expression,
        date_rule=slot.date_rule,
        start=slot.start,
        end=slot.end,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


@dataclass(frozen=True, slots=True)
class _SelfDatedCell:
    """A cell that states its own audience, date and time range."""

    expression: GroupExpression
    resolved_date: DateResolution
    time_range: TimeRangeResolution


def _read_self_dated_cell(
    *,
    lines: Sequence[str],
    numeric_date_order: NumericDateOrder,
) -> _SelfDatedCell | None:
    """Read a cell that carries its own date, or ``None`` when it carries none.

    A whole-cohort session is written into the table as ``TÜM GRUPLAR`` followed
    by its own date and time, and that date is not always the one its column
    header states. The cell is the more specific statement, so when it dates
    itself it is read from the cell and the column header is not consulted.

    The cell's own line structure carries the separation, so the caller passes
    the lines rather than the flattened text.
    """
    if len(lines) < 3:
        return None

    expression = _read_groups(lines[0])
    if not expression.resolved:
        return None

    time_range = resolve_time_range_text(lines[-1])
    if not time_range.resolved:
        return None

    resolved_date = resolve_date_text(" ".join(lines[1:-1]), numeric_order=numeric_date_order)
    return _SelfDatedCell(
        expression=expression,
        resolved_date=resolved_date,
        time_range=time_range,
    )


def _publish_self_dated(
    *,
    grid: WorksheetGrid,
    resolved: ResolvedCell,
    table: _SlotTable,
    subject: _Subject,
    context: ParseSourceContext,
    self_dated: _SelfDatedCell,
    evidence: SourceEvidence,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    refusal = _date_refusal(
        self_dated.resolved_date,
        normalize_text(resolved.text),
        subject="Cell",
    )
    if refusal is not None:
        diagnostics.record_ignored_cell(
            REASON_UNRESOLVED_SELF_DATED_CELL,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=refusal[1],
        )
        return

    selectors = _audience_selectors(self_dated.expression)
    if selectors is None:
        diagnostics.record_ignored_cell(
            REASON_UNSUPPORTED_GROUP_VALUE,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{normalize_text(resolved.text)}' names a cohort this profile does "
                "not model, so no lesson was published for it."
            ),
        )
        return

    start = self_dated.time_range.start
    end = self_dated.time_range.end
    if start is None or end is None:  # pragma: no cover - guarded by the reader
        return

    diagnostics.increment(METRIC_CANDIDATES_SELF_DATED)
    candidate = _build_candidate(
        grid=grid,
        table=table,
        subject=subject,
        context=context,
        local_date=_require_date(self_dated.resolved_date),
        start=start,
        end=end,
        confidence=min(
            self_dated.resolved_date.confidence,
            self_dated.time_range.confidence,
            self_dated.expression.confidence,
        ),
        selectors=selectors,
        covers_all=self_dated.expression.covers_all,
        cell_row=resolved.row_index,
        cell_column=resolved.column_index,
        cell_evidence=evidence,
        slot_evidence=evidence,
    )
    _accept(
        candidate=candidate,
        evidence=evidence,
        expression=self_dated.expression,
        date_rule=self_dated.resolved_date.rule,
        start=start,
        end=end,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


def _read_groups(text: str) -> GroupExpression:
    """Read the audience a cell states, ignoring its session number."""
    return parse_group_expression(
        _SESSION_MARKER_PATTERN.sub("", normalize_text(text)),
        dimension=DIMENSION_PRACTICE_GROUP,
        letter_groups=True,
        max_letter_run=MAX_LETTER_RUN,
    )


def _audience_selectors(expression: GroupExpression) -> list[AudienceSelector] | None:
    """Turn a resolved group expression into audience selectors.

    ``A`` selects a whole practice group and ``A2`` one subgroup of it, so the
    two are reported under different dimensions. A value of any other shape
    names a cohort this profile does not model, and the caller refuses the cell
    rather than assigning it to the nearest dimension.
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
    table: _SlotTable,
    subject: _Subject,
    context: ParseSourceContext,
    local_date: date,
    start: time,
    end: time,
    confidence: float,
    selectors: Sequence[AudienceSelector],
    covers_all: bool,
    cell_row: int,
    cell_column: int,
    cell_evidence: SourceEvidence,
    slot_evidence: SourceEvidence,
) -> CanonicalScheduleCandidate:
    event_type = _classify(subject=subject.display_title, block=table.heading)
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
            ("startLocalTime", start.isoformat()),
            ("courseIdentity", course_identity(subject.display_title) or ""),
            ("audience", audience_key),
        )
    )

    return CanonicalScheduleCandidate(
        candidate_id=f"{grid.worksheet.sheet_id}!R{cell_row + 1}C{cell_column + 1}",
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
        start_local_time=start,
        end_local_time=end,
        time_zone_id=context.time_zone_id,
        instructor=None,
        location=subject.place,
        # The wide merged heading above the table states the curriculum block
        # (ADR-047). This source names no academic department: its rows are
        # practice subjects, and reading one as a department would be an
        # inference the cell does not support.
        curriculum_block=table.heading,
        departments=[],
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": subject.display_title,
                "eventType": event_type.value,
                "localDate": local_date.isoformat(),
                # A rotation slot is always timed: a cell states which groups
                # attend a dated slot, so a slot without a readable time range is
                # refused rather than published as an all-day item (ADR-046).
                "isAllDay": encode_all_day(False),
                "startLocalTime": start.isoformat(),
                "endLocalTime": end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "instructor": None,
                "location": subject.place,
                "curriculumBlock": table.heading,
                "departments": None,
                "audience": audience_key,
            }
        ),
        confidence=confidence,
        identity_components=identity_components,
        evidence=[
            grid.evidence(subject.row_index, SUBJECT_COLUMN, extraction_rule=RULE_SUBJECT_CELL),
            grid.evidence(subject.row_index, PLACE_COLUMN, extraction_rule=RULE_PLACE_CELL),
            slot_evidence,
            cell_evidence,
        ],
    )


def _classify(*, subject: str, block: str | None) -> ScheduleEventType:
    """Classify a practice, reporting an examination as one.

    The shared practice classifier knows anatomy and the vertical corridor. This
    source also schedules its practical examinations in the rotation, and the
    title is the only thing that says so.
    """
    if any(word.startswith(key) for word in _words(subject) for key in EXAM_KEYS):
        return ScheduleEventType.EXAM
    return classify_practice_type(subject=subject, block=block)


def _require_date(resolution: DateResolution) -> date:
    if resolution.value is None:  # pragma: no cover - guarded by the caller
        raise ValueError("A resolved date is required to build a candidate.")
    return resolution.value


def _row_evidence(grid: WorksheetGrid, row_index: int) -> SourceEvidence:
    return grid.range_evidence(
        row_index,
        SUBJECT_COLUMN,
        row_index + 1,
        max(grid.worksheet.column_count, FIRST_SLOT_COLUMN),
        extraction_rule=RULE_ROW,
    )


def _accept(
    *,
    candidate: CanonicalScheduleCandidate,
    evidence: SourceEvidence,
    expression: GroupExpression,
    date_rule: str,
    start: time,
    end: time,
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
    diagnostics.increment(f"{METRIC_DATE_RULE_PREFIX}{date_rule}")
    for selector in candidate.audience.selectors:
        diagnostics.increment(f"{METRIC_AUDIENCE_DIMENSION_PREFIX}{selector.dimension}")

    if expression.confidence < 1.0:
        # The audience decides whose calendar changes, so a value read under a
        # cohort-model rule rather than as written is always reported.
        diagnostics.confidence(
            field_name="audience",
            score=expression.confidence,
            reason=expression.rule,
            candidate_id=candidate.candidate_id,
        )

    if candidate.confidence < 1.0 and expression.confidence == 1.0:
        diagnostics.confidence(
            field_name="localDate",
            score=candidate.confidence,
            reason=date_rule,
            candidate_id=candidate.candidate_id,
        )

    duration = duration_minutes(start, end)
    if not MIN_PLAUSIBLE_DURATION_MINUTES <= duration <= MAX_PLAUSIBLE_DURATION_MINUTES:
        diagnostics.warning(
            WARNING_IMPLAUSIBLE_DURATION,
            f"Practice lasts {duration} minutes, outside the plausible range of "
            f"{MIN_PLAUSIBLE_DURATION_MINUTES} to {MAX_PLAUSIBLE_DURATION_MINUTES} minutes.",
            candidate_id=candidate.candidate_id,
            evidence=evidence,
        )


def _words(value: str) -> list[str]:
    return _WORD_PATTERN.findall(comparison_key(value))
