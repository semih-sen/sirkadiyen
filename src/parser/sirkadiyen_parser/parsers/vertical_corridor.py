"""Vertical-corridor skill-practice calendar parser.

This is the source the other two Grade 2 profiles defer to. The annual program
writes these sessions as a bare ``UYGULAMA`` placeholder (ADR-071) and the
practice table marks them with a bare ``*`` whose groups its own note says are
"ayrı bir tablo ile duyurulacaktır" (ADR-074). This is that other table.

Its layout is the transpose of the practice table and the same orientation as
:mod:`sirkadiyen_parser.parsers.practice`: a **row is a dated slot** and a
**column is one of the five skill practices**. What differs from both is that the
whole slot — a label, a date, a weekday and a time range — is written as separate
lines of a single cell in the first column, the way the practice table writes its
column headers. So the two Grade 2 rotation sources share their cell-level rules
(:mod:`sirkadiyen_parser.parsers.cohort_rotation`) and differ in the axis they
walk.

Three things about the document decide how it is read:

- **It is filled in over time.** Student Affairs edits it during the year, so most
  dated rows state no groups yet. A row with no group cells publishes nothing and
  is not an error; a row with group cells that cannot be dated is.
- **It carries a second programme's cohorts.** The English cohorts ``İ1``-``İ3``
  and the separately published ``EK-1``-``EK-3`` lists appear in the same grid as
  the Turkish ``A``-``H``. They are counted and named, never published under a
  source whose context states another programme.
- **Its hand-typed dates go wrong the same way the practice table's do.** Every
  date whose year is a year out also contradicts the weekday typed beside it, and
  that contradiction is what refuses it.
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
from sirkadiyen_parser.normalization.date_sequence import DateSequence, DateSequenceEntry
from sirkadiyen_parser.normalization.dates import NumericDateOrder, resolve_date_text
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.groups import GroupExpression
from sirkadiyen_parser.normalization.instructors import starts_with_academic_title
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text, text_lines
from sirkadiyen_parser.normalization.times import duration_minutes, resolve_time_range_text
from sirkadiyen_parser.parsers.annual import (
    MAX_PLAUSIBLE_DURATION_MINUTES,
    METRIC_DATE_RULE_PREFIX,
    MIN_PLAUSIBLE_DURATION_MINUTES,
    WARNING_IMPLAUSIBLE_DURATION,
    encode_all_day,
)
from sirkadiyen_parser.parsers.cohort_rotation import (
    COHORT_LETTERS,
    cohort_audience_selectors,
    date_refusal,
    read_cohort_groups,
)
from sirkadiyen_parser.parsers.date_repair import (
    RULE_DATE_SEQUENCE,
    read_date_run,
    report_date_corrections,
    report_date_run,
)
from sirkadiyen_parser.parsers.practice import split_trailing_note
from sirkadiyen_parser.profiles import ParserProfileDefinition

SLOT_COLUMN = 0
PLACE_COLUMN = 1
FIRST_SUBJECT_COLUMN = 2

#: The first two cells of the row that names the skill practices across the
#: table. The place header is abbreviated in the autumn document.
SLOT_HEADER_ALIASES = frozenset({"uygulama adi", "practice name"})
PLACE_HEADER_ALIASES = frozenset({"uyg yeri", "uygulama yeri", "practice place", "place"})

#: The row below the header, which states where the practices are held. It reuses
#: the place wording in the first column, so it is recognized by that.
PLACE_STATEMENT_ALIASES = frozenset({"uygulama yeri", "uyg yeri", "practice place"})

#: A place that names a future announcement rather than a room.
DEFERRED_PLACE_KEYS = frozenset({"yayinlanacak", "announced"})

EXAM_KEYS = frozenset({"sinav", "exam"})

#: The slot label opening the first cell of a dated row. It is a session number
#: within its own series, or the same ``*`` the practice table uses to mark a
#: skill-practice date.
_SLOT_LABEL_PATTERN = re.compile(r"^(?:\d+\s*/\s*\d+|\*+)$")

#: The English programme's cohorts, written as ``İ1 grubu``, ``i1+i2`` or inside
#: an examination cell. Matched on the folded comparison key, so the dotted and
#: dotless I are one thing here.
_ENGLISH_COHORT_PATTERN = re.compile(r"(?<![a-z0-9])i\s*[1-3](?![0-9])")

#: The separately published lists the document's own note points at: "Simüle
#: Hasta Öykü Alma uygulaması için oluşturulan gruplar farklıdır."
_ANNEX_COHORT_PATTERN = re.compile(r"(?<![a-z0-9])ek\s*-?\s*[1-9](?![0-9])")

#: Cohorts written with a hyphen between them, which this source does only in its
#: examination cells (``A-B-C-D SINAV``). Every part must be one of the eight
#: letters the source states, so a hyphen that separates anything else — ``EK-1``,
#: a numeric range — is left alone and the cell is refused instead.
_HYPHENATED_COHORTS_PATTERN = re.compile(
    rf"^[{COHORT_LETTERS}](?:\s*-\s*[{COHORT_LETTERS}])+$",
    re.IGNORECASE,
)

_WORD_PATTERN = re.compile(r"[^\W_]+", re.UNICODE)

REASON_NOT_A_SLOT_ROW = "notADatedSlotRow"
REASON_ROW_WITHOUT_TABLE = "slotRowOutsideAnyTable"
REASON_UNREADABLE_SLOT_WITH_GROUPS = "groupsStatedWithoutReadableSlot"
REASON_UNRESOLVED_SLOT_TIME = "unresolvedSlotTimeRange"
REASON_OUT_OF_SCOPE_GROUP_ROTATION = "outOfScopeGroupRotation"
REASON_ENGLISH_PROGRAM_COHORT = "cohortOfAnotherProgram"
REASON_SEPARATE_COHORT_LIST = "separatelyPublishedCohortList"
REASON_UNRESOLVED_GROUP = "unresolvedGroupExpression"
REASON_UNSUPPORTED_GROUP_VALUE = "unsupportedGroupValueShape"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_NO_TABLE = "worksheetWithoutSlotTable"
WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED_NO_TABLE = "worksheets.ignored.noSlotTable"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_ROWS_HEADER = "rows.headerRow"
METRIC_ROWS_PLACE_STATEMENT = "rows.placeStatement"
METRIC_ROWS_SLOT = "rows.slot"
METRIC_SLOTS_REFUSED_PREFIX = "slots.ignored."
METRIC_SUBJECTS_DETECTED = "subjects.detected"
METRIC_CELLS_SCANNED = "cells.scanned"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
METRIC_AUDIENCE_DIMENSION_PREFIX = "audience.dimension."
METRIC_PLACE_DEFERRED = "place.deferredToAnnouncement"

RULE_HEADER = "verticalCorridor.headerRow"
RULE_SUBJECT_HEADER = "verticalCorridor.subjectHeader"
RULE_SLOT_CELL = "verticalCorridor.slotCell"
RULE_PLACE_CELL = "verticalCorridor.placeCell"
RULE_GROUP_CELL = "verticalCorridor.groupCell"
RULE_ROW = "verticalCorridor.row"


@dataclass(frozen=True, slots=True)
class _Subject:
    """One skill practice, read from a column of the header row."""

    column: int
    display_title: str
    instructor: str | None


@dataclass(frozen=True, slots=True)
class _Table:
    """A header row and the skill-practice columns it names."""

    header_row: int
    subjects: tuple[_Subject, ...]
    place: str | None = None
    deferred_place: bool = False


@dataclass(frozen=True, slots=True)
class _Slot:
    """One dated row of a table."""

    row_index: int
    label: str | None
    local_date: date
    start: time
    end: time
    confidence: float
    date_rule: str


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)


def parse_vertical_corridor_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse a vertical-corridor calendar into candidate lessons."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    report_date_corrections(diagnostics=diagnostics, context=request.source_context)
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
            f"Worksheet '{worksheet.title}' has no recognizable header row and was not parsed.",
            evidence=_worksheet_evidence(worksheet),
        )

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    if selected == 0:
        diagnostics.error(
            WARNING_NO_TABLE,
            "No worksheet in the snapshot exposes a skill-practice header row, so the "
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
        extraction_rule=RULE_HEADER,
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

    The document is split across several Word tables, each of which repeats the
    header row, so a header does not end the worksheet: it replaces the table in
    force. Every row is classified exactly once, so ``rows.scanned`` equals the
    worksheet's row count.
    """
    table: _Table | None = None
    parsed_any = False

    # The dated rows run down the document in order, so they form one
    # chronological run and a date that contradicts it is read as a mistyped
    # year (ADR-139). A document whose dates are sound is unaffected.
    date_sequence = read_date_run(
        tuple(_worksheet_dates(grid=grid, numeric_date_order=profile.numeric_date_order)),
        context=context,
    )
    report_date_run(
        date_sequence,
        diagnostics=diagnostics,
        evidence_for=lambda row_index: grid.evidence(
            row_index,
            SLOT_COLUMN,
            extraction_rule=RULE_DATE_SEQUENCE,
        ),
    )

    for row_index in range(grid.worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)

        if _is_header_row(grid, row_index):
            diagnostics.increment(METRIC_ROWS_HEADER)
            table = _read_table(grid, row_index, diagnostics)
            parsed_any = True
            continue

        if table is not None and _is_place_statement_row(grid, row_index):
            diagnostics.increment(METRIC_ROWS_PLACE_STATEMENT)
            table = _read_place_statement(grid, row_index, table, diagnostics)
            continue

        slot = _read_slot_row(
            grid=grid,
            row_index=row_index,
            table=table,
            numeric_date_order=profile.numeric_date_order,
            date_sequence=date_sequence,
            group_rotation_subjects=frozenset(profile.group_rotation_subjects),
            diagnostics=diagnostics,
        )
        if slot is None or table is None:
            continue

        diagnostics.increment(METRIC_ROWS_SLOT)
        diagnostics.increment(f"{METRIC_DATE_RULE_PREFIX}{slot.date_rule}")
        for subject in table.subjects:
            _parse_cell(
                grid=grid,
                table=table,
                slot=slot,
                subject=subject,
                context=context,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )

    return parsed_any


def _is_header_row(grid: WorksheetGrid, row_index: int) -> bool:
    """Whether a row names the skill practices across the table.

    Recognized by the first column and by naming at least one practice. The
    place header beside it is *not* required: one of the seven tables of the
    spring document leaves that cell empty, and requiring it dropped that whole
    table — eleven dated rows — into "no table in force".
    """
    if comparison_key(grid.text(row_index, SLOT_COLUMN)) not in SLOT_HEADER_ALIASES:
        return False

    return any(
        grid.text(row_index, column)
        for column in range(FIRST_SUBJECT_COLUMN, grid.worksheet.column_count)
    )


def _is_place_statement_row(grid: WorksheetGrid, row_index: int) -> bool:
    return comparison_key(grid.text(row_index, SLOT_COLUMN)) in PLACE_STATEMENT_ALIASES and bool(
        grid.text(row_index, PLACE_COLUMN)
    )


def _read_table(grid: WorksheetGrid, row_index: int, diagnostics: ParseDiagnostics) -> _Table:
    subjects = tuple(_read_subjects(grid, row_index))
    diagnostics.increment(METRIC_SUBJECTS_DETECTED, len(subjects))
    return _Table(header_row=row_index, subjects=subjects)


def _read_subjects(grid: WorksheetGrid, header_row: int) -> Iterator[_Subject]:
    for column in range(FIRST_SUBJECT_COLUMN, grid.worksheet.column_count):
        resolved = grid.resolve(header_row, column)
        lines = text_lines(resolved.display_text or "")
        if not lines:
            continue

        # A header cell wraps its practice over several lines and usually ends
        # with a parenthesised instructor on its own line, so the lines are
        # rejoined before the trailing note is separated. A note that does not
        # name an instructor stays in the title rather than being discarded —
        # which is what happens to the columns whose closing bracket is missing.
        joined = " ".join(lines)
        without_note, note = split_trailing_note(joined)
        if note is None:
            without_note, note = _split_unclosed_note(joined)
        if note is not None and starts_with_academic_title(note):
            title_text, instructor = without_note, note
        else:
            title_text, instructor = joined, None

        title = normalize_course_title(title_text)
        if not title.display_title:
            continue

        yield _Subject(
            column=column,
            display_title=title.display_title,
            instructor=instructor,
        )


def _split_unclosed_note(text: str) -> tuple[str, str | None]:
    """Separate a trailing parenthetical whose closing bracket is missing.

    Four of the seven spring tables write ``OKSİJEN (Doç. Dr. Bengüsu MİRASOĞLU``
    and never close the bracket, while the first table writes the same column
    with the bracket closed. Without this the same practice reaches students'
    calendars under two titles, one of them ending mid-bracket.

    Only the caller's academic-title test makes it safe, and that test is applied
    to the result: text after a stray bracket that does not name an instructor is
    left in the title rather than thrown away.
    """
    opened = text.count("(")
    if opened != 1 or ")" in text:
        return text, None

    head, _, tail = text.partition("(")
    return head.strip(), tail.strip() or None


def _read_place_statement(
    grid: WorksheetGrid,
    row_index: int,
    table: _Table,
    diagnostics: ParseDiagnostics,
) -> _Table:
    """Read the room the table states once for all of its practices."""
    place = normalize_text(grid.text(row_index, PLACE_COLUMN))
    deferred = any(word.startswith(key) for word in _words(place) for key in DEFERRED_PLACE_KEYS)
    if deferred:
        diagnostics.increment(METRIC_PLACE_DEFERRED)

    return _Table(
        header_row=table.header_row,
        subjects=table.subjects,
        place=None if deferred else place or None,
        deferred_place=deferred,
    )


def _worksheet_dates(
    *,
    grid: WorksheetGrid,
    numeric_date_order: NumericDateOrder,
) -> Iterator[DateSequenceEntry]:
    """Resolve every slot cell that states a date, in row order.

    Most rows of this document are not slots — notes, banners, spacers — so
    membership of the run is decided by whether the cell's date lines resolve
    rather than by where the row sits.
    """
    for row_index in range(grid.worksheet.row_count):
        date_text = _slot_date_text(grid, row_index)
        if date_text is None:
            continue
        resolution = resolve_date_text(date_text, numeric_order=numeric_date_order)
        if resolution.resolved:
            yield DateSequenceEntry(key=row_index, resolution=resolution)


def _slot_date_text(grid: WorksheetGrid, row_index: int) -> str | None:
    """Return the date lines of a slot cell, or ``None`` when it states none.

    A slot cell wraps an optional label, a date that may itself wrap, and a time
    range on the last line. Shared with :func:`_read_slot_row` so the run and the
    row can never disagree about which text is the date.
    """
    lines = list(text_lines(grid.resolve(row_index, SLOT_COLUMN).display_text or ""))
    body = lines[1:] if lines and _SLOT_LABEL_PATTERN.match(lines[0]) else lines
    return " ".join(body[:-1]) if len(body) >= 2 else None


def _read_slot_row(
    *,
    grid: WorksheetGrid,
    row_index: int,
    table: _Table | None,
    numeric_date_order: NumericDateOrder,
    date_sequence: DateSequence,
    group_rotation_subjects: frozenset[str],
    diagnostics: ParseDiagnostics,
) -> _Slot | None:
    """Read one dated row, or account for why it states no schedulable slot.

    Most rows of this document are not slots: a note, a banner, a blank spacer,
    and — because Student Affairs fills the grid in over the year — a dated row
    whose groups have not been decided yet. What separates an expected non-slot
    from an anomaly is whether the row states groups: a row nobody can date but
    that names cohorts is a session that will silently not happen.
    """
    slot_text = grid.text(row_index, SLOT_COLUMN)
    stated_groups = table is not None and any(
        grid.text(row_index, subject.column) for subject in table.subjects
    )

    if not slot_text and not stated_groups:
        diagnostics.record_ignored_row(REASON_NOT_A_SLOT_ROW, _row_evidence(grid, row_index))
        return None

    if table is None:
        diagnostics.record_ignored_row(
            REASON_ROW_WITHOUT_TABLE,
            _row_evidence(grid, row_index),
            severity=ParserWarningSeverity.INFORMATION,
            message=(
                "Row precedes the first header row, so no skill-practice column could "
                "be named for its cells."
            ),
        )
        return None

    if group_rotation_subjects and any(
        word.startswith(key) for word in _words(slot_text) for key in group_rotation_subjects
    ):
        # The dissection rotation is written into this grid too, and the anatomy
        # sources own it (ADR-073).
        diagnostics.record_ignored_row(
            REASON_OUT_OF_SCOPE_GROUP_ROTATION,
            grid.evidence(row_index, SLOT_COLUMN, extraction_rule=RULE_SLOT_CELL),
            severity=ParserWarningSeverity.INFORMATION,
            message=(
                f"Row states '{slot_text}', a group rotation this profile defers to its "
                "own source (ADR-073), so nothing was published from it."
            ),
        )
        return None

    lines = list(text_lines(grid.resolve(row_index, SLOT_COLUMN).display_text or ""))
    body = lines[1:] if lines and _SLOT_LABEL_PATTERN.match(lines[0]) else lines
    label = lines[0] if len(body) < len(lines) else None

    # A slot needs a date and a time range on separate lines. One line cannot be
    # split into both without guessing where the date ends.
    if len(body) < 2:
        _refuse_row(
            grid=grid,
            row_index=row_index,
            stated_groups=stated_groups,
            reason=REASON_UNREADABLE_SLOT_WITH_GROUPS if stated_groups else REASON_NOT_A_SLOT_ROW,
            message=(
                f"Row states groups but its first cell '{normalize_text(slot_text)}' does "
                "not state a date and a time range on separate lines, so nothing was "
                "published from it."
            ),
            diagnostics=diagnostics,
        )
        return None

    time_range = resolve_time_range_text(body[-1])
    if not time_range.resolved or time_range.start is None or time_range.end is None:
        _refuse_row(
            grid=grid,
            row_index=row_index,
            stated_groups=stated_groups,
            reason=REASON_UNRESOLVED_SLOT_TIME,
            message=(
                f"Slot time range '{body[-1]}' could not be read ({time_range.reason}), "
                "so nothing was published from the row."
            ),
            diagnostics=diagnostics,
        )
        return None

    date_text = " ".join(body[:-1])
    # The document's chronological reading wins where it made one (ADR-139), and
    # a repaired date faces the refusal rules exactly as a written one does.
    resolved_date = date_sequence.resolution(
        row_index,
        resolve_date_text(date_text, numeric_order=numeric_date_order),
    )
    refusal = date_refusal(resolved_date, date_text, subject="Slot")
    if refusal is not None or resolved_date.value is None:
        reason, message = refusal if refusal is not None else (REASON_NOT_A_SLOT_ROW, "")
        _refuse_row(
            grid=grid,
            row_index=row_index,
            # A wrong date is an anomaly whether or not anyone is booked into it
            # yet, because the groups are filled in later and the date is not.
            stated_groups=True,
            reason=reason,
            message=message,
            diagnostics=diagnostics,
        )
        return None

    return _Slot(
        row_index=row_index,
        label=label,
        local_date=resolved_date.value,
        start=time_range.start,
        end=time_range.end,
        confidence=min(resolved_date.confidence, time_range.confidence),
        date_rule=resolved_date.rule,
    )


def _refuse_row(
    *,
    grid: WorksheetGrid,
    row_index: int,
    stated_groups: bool,
    reason: str,
    message: str,
    diagnostics: ParseDiagnostics,
) -> None:
    """Account for a row that publishes nothing.

    A row that states no groups is expected structure and is reported once per
    reason; a row that states groups nobody can date is reported with its own
    address, because a session is being lost.
    """
    diagnostics.increment(f"{METRIC_SLOTS_REFUSED_PREFIX}{reason}")
    if not stated_groups:
        diagnostics.record_ignored_row(reason, _row_evidence(grid, row_index))
        return

    # One warning, not two: recording the ignored row at WARNING already carries
    # the message and the cell's address.
    diagnostics.record_ignored_row(
        reason,
        grid.evidence(row_index, SLOT_COLUMN, extraction_rule=RULE_SLOT_CELL),
        severity=ParserWarningSeverity.WARNING,
        message=message,
    )


def _row_evidence(grid: WorksheetGrid, row_index: int) -> SourceEvidence:
    return grid.range_evidence(
        row_index,
        SLOT_COLUMN,
        row_index + 1,
        max(grid.worksheet.column_count, FIRST_SUBJECT_COLUMN),
        extraction_rule=RULE_ROW,
    )


def _parse_cell(
    *,
    grid: WorksheetGrid,
    table: _Table,
    slot: _Slot,
    subject: _Subject,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    text = normalize_text(grid.text(slot.row_index, subject.column))
    if not text:
        return

    diagnostics.increment(METRIC_CELLS_SCANNED)
    evidence = grid.evidence(slot.row_index, subject.column, extraction_rule=RULE_GROUP_CELL)
    key = comparison_key(text)

    if _ANNEX_COHORT_PATTERN.search(key):
        # The document's own note says the simulated-patient practice uses a
        # different grouping, published as a separate list. The cell states a
        # session, not an audience this source can resolve.
        diagnostics.record_ignored_cell(REASON_SEPARATE_COHORT_LIST, evidence)
        return

    if _ENGLISH_COHORT_PATTERN.search(key):
        # The English programme's cohorts share this grid. Publishing one under a
        # source whose context states another programme would put the session in
        # the wrong students' calendars, and the English source is the one that
        # may declare them (ADR-048).
        diagnostics.record_ignored_cell(REASON_ENGLISH_PROGRAM_COHORT, evidence)
        return

    is_exam = any(word in EXAM_KEYS for word in _words(text))
    expression = read_cohort_groups(_audience_text(text))
    selectors = cohort_audience_selectors(expression)
    if not expression.resolved or selectors is None:
        diagnostics.record_ignored_cell(
            REASON_UNRESOLVED_GROUP if not expression.resolved else REASON_UNSUPPORTED_GROUP_VALUE,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{text}' does not state which groups attend "
                f"({expression.reason or REASON_UNSUPPORTED_GROUP_VALUE}), so no lesson "
                "was published for it."
            ),
        )
        return

    candidate = _build_candidate(
        grid=grid,
        table=table,
        slot=slot,
        subject=subject,
        context=context,
        selectors=selectors,
        covers_all=expression.covers_all,
        is_exam=is_exam,
        cell_evidence=evidence,
    )
    _accept(
        candidate=candidate,
        evidence=evidence,
        expression=expression,
        slot=slot,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


def _audience_text(text: str) -> str:
    """Strip what a cell says about the session from what it says about who attends.

    An examination cell names its cohorts with hyphens between them
    (``A-B-C-D SINAV``). The hyphens are rewritten only when every part is one of
    the eight letters this source states, so ``EK-1`` and any numeric range keep
    their hyphen and are refused rather than split into cohorts.
    """
    without_exam = " ".join(
        word for word in re.split(r"(\s+)", text) if comparison_key(word) not in EXAM_KEYS
    ).strip()
    return (
        without_exam.replace("-", "+")
        if _HYPHENATED_COHORTS_PATTERN.match(without_exam)
        else without_exam
    )


def _build_candidate(
    *,
    grid: WorksheetGrid,
    table: _Table,
    slot: _Slot,
    subject: _Subject,
    context: ParseSourceContext,
    selectors: Sequence[AudienceSelector],
    covers_all: bool,
    is_exam: bool,
    cell_evidence: SourceEvidence,
) -> CanonicalScheduleCandidate:
    # The source family decides the type: every row of this document is a
    # vertical-corridor skill practice, and the profile is registered against
    # that source. Only an examination is written differently, and the cell says
    # so in words.
    event_type = ScheduleEventType.EXAM if is_exam else ScheduleEventType.VERTICAL_CORRIDOR
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
            ("localDate", slot.local_date.isoformat()),
            ("startLocalTime", slot.start.isoformat()),
            ("courseIdentity", course_identity(subject.display_title) or ""),
            ("audience", audience_key),
        )
    )

    return CanonicalScheduleCandidate(
        candidate_id=f"{grid.worksheet.sheet_id}!R{slot.row_index + 1}C{subject.column + 1}",
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
        local_date=slot.local_date,
        start_local_time=slot.start,
        end_local_time=slot.end,
        time_zone_id=context.time_zone_id,
        instructor=subject.instructor,
        location=table.place,
        # This document states no curriculum block and no academic department.
        # It is one programme from end to end, and reading a practice name as a
        # department would be an inference the cell does not support (ADR-047).
        curriculum_block=None,
        departments=[],
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": subject.display_title,
                "eventType": event_type.value,
                "localDate": slot.local_date.isoformat(),
                # A rotation slot is always timed: a row without a readable time
                # range is refused rather than published as all-day (ADR-046).
                "isAllDay": encode_all_day(False),
                "startLocalTime": slot.start.isoformat(),
                "endLocalTime": slot.end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "instructor": subject.instructor,
                "location": table.place,
                "curriculumBlock": None,
                "departments": None,
                "audience": audience_key,
            }
        ),
        confidence=slot.confidence,
        identity_components=identity_components,
        evidence=[
            grid.evidence(table.header_row, subject.column, extraction_rule=RULE_SUBJECT_HEADER),
            grid.evidence(slot.row_index, SLOT_COLUMN, extraction_rule=RULE_SLOT_CELL),
            cell_evidence,
        ],
    )


def _accept(
    *,
    candidate: CanonicalScheduleCandidate,
    evidence: SourceEvidence,
    expression: GroupExpression,
    slot: _Slot,
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

    if expression.confidence < 1.0:
        # The audience decides whose calendar changes, so a value read under a
        # cohort-model rule rather than as written is always reported.
        diagnostics.confidence(
            field_name="audience",
            score=expression.confidence,
            reason=expression.rule,
            candidate_id=candidate.candidate_id,
        )

    if candidate.confidence < 1.0:
        diagnostics.confidence(
            field_name="localDate",
            score=candidate.confidence,
            reason=slot.date_rule,
            candidate_id=candidate.candidate_id,
        )

    duration = duration_minutes(slot.start, slot.end)
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
