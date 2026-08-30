"""The weekly amphitheatre program: which room a stated session is held in.

The faculty publishes one workbook per week that answers a question none of the
annual programs answer. An annual program states that Grade 2 Turkish has
physiology on a Thursday at 09.20, and writes ``AMFİ PROGRAMINA BAKINIZ`` where
the room would go; this document is that program. It is therefore a companion in
exactly the sense ADR-102 means: it publishes nothing of its own and only says
more about sessions another source already states (ADR-133).

Layout
------

A worksheet holds one block per day, stacked vertically. A block is a day title
row, a ``SAAT`` header row naming a room per column, and then one row per time
slot with the slot's range in the ``SAAT`` column::

    31  AĞUSTOS 2026 / Pazartesi
    SAAT           | AZİZ SANCAR | KEMAL ATAY AMFİSİ | ...
    08.30 - 09.10  |             | DÖNEM 2-TÜRKÇE -KAN LENFOİD DİLİMİ -FİZYOLOJİ

A block is recognized by its ``SAAT`` header row and dated from the row directly
above it. That pairing is the whole structure rule, and it is what keeps the
reader away from the debris these workbooks accumulate: the committed fixture
carries a lone ``16 Eylül 2025 / Salı`` title stranded in column AC from a
previous academic year, with no header row beneath it and no data under it. A
rule that recognized day titles on their own would read it as a real day.

The room columns are read from each block's own header row rather than once per
worksheet, because they genuinely differ between days: in the committed fixture
Friday's columns J and K are ``FİZİK TEDAVİ YÜKSEK OKULU A/B AMFİSİ`` where every
other day writes ``ESKİ FİZİK TEDAVİ ANABİLİM DALI A/B DERSLİĞİ``.

Neither the file name nor the worksheet title may be used to decide which week a
workbook covers. The committed fixture's first worksheet is titled
``31 AĞUSTOS-1 EYLÜL   2026-`` while its blocks run through 4 September, and the
Drive file naming it is ``31 AĞUSTOS -4 EYLÜL 2026``. Every date this reader
reports comes from a day title row.

What a cell says
----------------

A cell names its audience in a dashed list whose order the source does not keep
stable — ``DÖNEM 3-TÜRKÇE-A GRUBU`` and ``DÖNEM 3- B GRUBU- TÜRKÇE`` are the same
shape written two ways — so each fact is recognized by what it looks like rather
than by which segment it sits in. The last segment that is none of those facts is
the academic department, which is what makes a room assignment joinable to a
lesson.

A cell may also state its own time, as in ``DÖNEM 5-HALK SAĞLIĞI -E GRUBU
-08.40-15.20``. A stated time is authoritative over the row the cell sits in,
because the row is a grid the timetable is drawn on and the cell is what the
source actually asserts.

Nothing here decides which lesson an assignment belongs to. This module reports
what the document says; :mod:`sirkadiyen_parser.parsers.annual` decides whether a
published lesson matches one of these assignments closely enough to take its room.
"""

import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import date, time

from sirkadiyen_parser.contracts.parsing import (
    ParserProfileDescriptor,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ProgramLanguage,
    SourceEvidence,
)
from sirkadiyen_parser.contracts.snapshot import (
    NormalizedCell,
    NormalizedSpreadsheetSnapshot,
)
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.normalization.dates import (
    NumericDateOrder,
    resolve_cell_date,
    resolve_date_text,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid, cell_display_text
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text
from sirkadiyen_parser.normalization.times import resolve_time_range_text, resolve_time_text
from sirkadiyen_parser.profiles import ParserProfileDefinition

#: The header cell that marks the start of a day block's grid.
_SLOT_HEADER_KEYS = frozenset({"saat", "hour", "time"})

#: How far a room column may sit from the ``SAAT`` column. The widest committed
#: block names fifteen rooms; the bound stops a stray cell far to the right from
#: being adopted as a room.
MAX_ROOM_COLUMNS = 24

#: A block whose header names no room is not a timetable.
MIN_ROOM_COLUMNS = 1

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_DAY_BLOCKS = "amphitheatre.dayBlocks"
METRIC_SLOT_ROWS = "amphitheatre.slotRows"
METRIC_ASSIGNMENTS = "amphitheatre.assignments"
METRIC_ASSIGNMENTS_WITH_DEPARTMENT = "amphitheatre.assignments.withDepartment"
METRIC_ASSIGNMENTS_IN_SCOPE = "amphitheatre.assignments.inSupportedClassYears"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"

WARNING_NO_DAY_BLOCK = "noAmphitheatreDayBlock"
WARNING_UNDATED_DAY_BLOCK = "undatedAmphitheatreDayBlock"
WARNING_PUBLISHES_NO_SESSIONS = "amphitheatreDocumentPublishesNoSessions"

REASON_UNRESOLVED_SLOT_TIME = "unresolvedAmphitheatreSlotTime"
REASON_NO_CLASS_YEAR = "amphitheatreCellNamesNoClassYear"

RULE_DAY_TITLE = "amphitheatre.dayTitle"
RULE_ROOM_HEADER = "amphitheatre.roomHeader"
RULE_ASSIGNMENT = "amphitheatre.assignment"

#: ``DÖNEM 2``, and also ``DÖNEM  - 3`` which one whole column of the committed
#: fixture writes. Anchored on a word boundary so ``DÖNEMİ`` cannot match.
_CLASS_YEAR_PATTERN = re.compile(r"\bdonem\b\s*-?\s*(\d)")

#: ``A GRUBU`` / ``B GRUBU``, and the ``DÖNEM 3-A`` form that writes the letter
#: straight onto the class year. Only Grade 3 is split into lettered curriculum
#: groups, so the letter is only read for it.
_GROUP_WORD_PATTERN = re.compile(r"\b([ab])\s*gru(?:bu|p)\b")
_GROUP_ATTACHED_PATTERN = re.compile(r"\bdonem\b\s*-?\s*3\s*-\s*([ab])\b")

#: A start and end the cell states for itself, as in ``-13.00-15.20``. Both ends
#: must be written as a time, so ``A2-2`` and ``HALL 1`` cannot match.
_TIME_RANGE_PATTERN = re.compile(r"(\d{1,2}[.:]\d{2})\s*[-–—]\s*(\d{1,2}[.:]\d{2})")

#: A single time the cell states at its end, as in ``ORTOPEDİ VE TRAVMATOLOJİ-10.30``.
_TRAILING_TIME_PATTERN = re.compile(r"[-–—]\s*(\d{1,2}[.:]\d{2})\s*$")

_LANGUAGE_KEYS: Mapping[str, ProgramLanguage] = {
    "turkce": ProgramLanguage.TURKISH,
    "ingilizce": ProgramLanguage.ENGLISH,
    "english": ProgramLanguage.ENGLISH,
}

#: Segments that are audience facts rather than the department, so that the
#: department is never read off one of them.
_SEGMENT_SEPARATOR_PATTERN = re.compile(r"\s*[-–—]\s*")


@dataclass(frozen=True, slots=True)
class AmphitheatreAssignment:
    """One room the document assigns to one audience at one time.

    ``curriculum_group`` and ``program_language`` are ``None`` when the cell
    names none. That is not the same as naming all of them: a cell that says
    nothing about language cannot be claimed by a Turkish lesson in preference
    to an English one, and the join treats an unstated fact as a fact that
    cannot narrow anything.
    """

    local_date: date
    start_local_time: time
    end_local_time: time
    room: str
    class_year: int | None
    program_language: ProgramLanguage | None
    curriculum_group: str | None
    curriculum_block: str | None
    department: str | None
    raw_text: str
    #: Whether the cell stated its own time instead of inheriting the slot row's.
    time_is_stated: bool
    evidence: SourceEvidence

    @property
    def department_key(self) -> str | None:
        """The department folded for comparison, or ``None`` when unstated."""
        return comparison_key(self.department) if self.department else None


@dataclass(frozen=True, slots=True)
class AmphitheatreDocument:
    """Every room assignment one weekly workbook states."""

    assignments: tuple[AmphitheatreAssignment, ...] = ()

    def dates(self) -> tuple[date, ...]:
        """The distinct dates the document states a block for, ascending."""
        return tuple(sorted({assignment.local_date for assignment in self.assignments}))

    def by_date(self) -> Mapping[date, tuple[AmphitheatreAssignment, ...]]:
        """Assignments grouped by the date they fall on, in document order."""
        grouped: dict[date, list[AmphitheatreAssignment]] = {}
        for assignment in self.assignments:
            grouped.setdefault(assignment.local_date, []).append(assignment)
        return {day: tuple(items) for day, items in grouped.items()}


def read_amphitheatre_document(
    snapshot: NormalizedSpreadsheetSnapshot,
    *,
    numeric_date_order: NumericDateOrder = NumericDateOrder.UNDECLARED,
    diagnostics: ParseDiagnostics | None = None,
) -> AmphitheatreDocument:
    """Read every dated room assignment a weekly amphitheatre workbook states.

    Every worksheet is scanned. These workbooks keep the previous week as an
    extra worksheet — the committed fixture's third worksheet is
    ``24-28 AĞUSTOS 2026`` — and dropping it would be a guess about which
    worksheet is current. Reading it costs nothing instead: each assignment
    carries the date its own block states, and a stale week simply describes days
    no lesson of the current revision falls on.
    """
    diagnostics = diagnostics or ParseDiagnostics()
    assignments: list[AmphitheatreAssignment] = []
    day_blocks = 0
    slot_rows = 0

    for worksheet in snapshot.worksheets:
        if worksheet.hidden:
            continue

        grid = WorksheetGrid(worksheet)
        for block in _find_day_blocks(grid, numeric_date_order, diagnostics):
            day_blocks += 1
            block_assignments, block_slot_rows = _read_block(grid, block, diagnostics)
            assignments.extend(block_assignments)
            slot_rows += block_slot_rows

    diagnostics.set_metric(METRIC_DAY_BLOCKS, day_blocks)
    diagnostics.set_metric(METRIC_SLOT_ROWS, slot_rows)
    diagnostics.set_metric(METRIC_ASSIGNMENTS, len(assignments))
    diagnostics.set_metric(
        METRIC_ASSIGNMENTS_WITH_DEPARTMENT,
        sum(1 for assignment in assignments if assignment.department),
    )
    diagnostics.set_metric(
        METRIC_ASSIGNMENTS_IN_SCOPE,
        sum(1 for assignment in assignments if assignment.class_year in {1, 2, 3}),
    )

    return AmphitheatreDocument(assignments=tuple(assignments))


def parse_amphitheatre_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Read a weekly amphitheatre workbook and deliberately publish nothing.

    The document states no lesson: a cell says that some Grade 2 physiology hour
    is in Kemal Atay, not that the hour exists. Publishing from it would create a
    second event beside the one the annual program already publishes. Reading it
    here is what proves the reader the annual profile calls, and what accounts for
    the document in the metrics rather than leaving it silently unparsed.
    """
    diagnostics = ParseDiagnostics()
    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))

    document = read_amphitheatre_document(
        request.snapshot,
        numeric_date_order=profile.numeric_date_order,
        diagnostics=diagnostics,
    )
    diagnostics.set_metric(METRIC_CANDIDATES_EMITTED, 0)

    if not document.assignments:
        diagnostics.error(
            WARNING_NO_DAY_BLOCK,
            "No worksheet in the snapshot holds an amphitheatre day block, so the "
            "document assigns no room at all.",
        )
    else:
        days = document.dates()
        diagnostics.information(
            WARNING_PUBLISHES_NO_SESSIONS,
            f"Read {len(document.assignments)} room assignments across {len(days)} days "
            f"from {days[0].isoformat()} to {days[-1].isoformat()}. No session is "
            "published from this document: it states which room an already-scheduled "
            "session uses, never that the session exists.",
        )

    return ParseSnapshotResponse(
        contract_version=request.contract_version,
        correlation_id=request.correlation_id,
        source_id=request.snapshot.source_id,
        snapshot_id=request.snapshot.snapshot_id,
        parser_profile=ParserProfileDescriptor(name=profile.name, version=profile.version),
        status=diagnostics.status(),
        candidates=[],
        warnings=list(diagnostics.warnings),
        metrics=list(diagnostics.metrics),
        confidence_indicators=list(diagnostics.confidence_indicators),
    )


@dataclass(frozen=True, slots=True)
class _DayBlock:
    """One day's grid: where it starts, what date it is, and its room columns."""

    local_date: date
    title_row_index: int
    header_row_index: int
    slot_column_index: int
    end_row_index_exclusive: int
    #: Room name by column index, in ascending column order.
    rooms: Mapping[int, str]


def _find_day_blocks(
    grid: WorksheetGrid,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> Sequence[_DayBlock]:
    """Every day block on the worksheet, in row order.

    A block is anchored on its ``SAAT`` header row and dated from the row above
    it. Requiring the two together is what distinguishes a real day from a title
    left behind by an earlier edit.
    """
    headers: list[tuple[int, int, Mapping[int, str]]] = []
    for row_index in grid.occupied_rows():
        found = _read_header_row(grid, row_index)
        if found is not None:
            headers.append((row_index, *found))

    blocks: list[_DayBlock] = []
    for position, (header_row, slot_column, rooms) in enumerate(headers):
        title_row = header_row - 1
        if title_row < 0:
            continue

        resolution = _resolve_block_date(grid, title_row, numeric_date_order)
        if resolution is None:
            diagnostics.warning(
                WARNING_UNDATED_DAY_BLOCK,
                "A row naming rooms was found but the row above it states no readable "
                "date, so the block cannot be dated and no room it assigns is reported.",
                evidence=grid.evidence(title_row, slot_column, extraction_rule=RULE_DAY_TITLE),
            )
            continue

        block_date, date_evidence_column = resolution
        next_title_row = (
            headers[position + 1][0] - 1
            if position + 1 < len(headers)
            else grid.worksheet.row_count
        )
        blocks.append(
            _DayBlock(
                local_date=block_date,
                title_row_index=title_row,
                header_row_index=header_row,
                slot_column_index=slot_column,
                end_row_index_exclusive=max(next_title_row, header_row + 1),
                rooms=rooms,
            )
        )
        diagnostics.confidence(
            field_name="localDate",
            score=1.0,
            reason=(
                f"Day block dated {block_date.isoformat()} from the title row above its "
                f"room header, at column {date_evidence_column}."
            ),
        )

    return blocks


def _read_header_row(grid: WorksheetGrid, row_index: int) -> tuple[int, Mapping[int, str]] | None:
    """The slot column and room columns of a ``SAAT`` header row, if this is one."""
    slot_column: int | None = None
    for column_index in grid.occupied_columns():
        if comparison_key(grid.text(row_index, column_index)) in _SLOT_HEADER_KEYS:
            slot_column = column_index
            break

    if slot_column is None:
        return None

    rooms: dict[int, str] = {}
    for column_index in grid.occupied_columns():
        if column_index <= slot_column or column_index - slot_column > MAX_ROOM_COLUMNS:
            continue
        if grid.is_column_hidden(column_index):
            continue
        name = grid.text(row_index, column_index)
        if name:
            rooms[column_index] = name

    if len(rooms) < MIN_ROOM_COLUMNS:
        return None
    return slot_column, rooms


def _resolve_block_date(
    grid: WorksheetGrid,
    title_row_index: int,
    numeric_date_order: NumericDateOrder,
) -> tuple[date, int] | None:
    """The date a block's title row states, and the column that stated it.

    The title is normally a merged run starting in the first column, but the
    committed fixture also writes a Saturday title in column I and a Sunday one
    as a date serial in column I. The row is therefore searched rather than a
    single coordinate read, and the first cell of it that resolves to a date wins.
    """
    for column_index in grid.occupied_columns():
        resolved = grid.resolve(title_row_index, column_index)
        if resolved.is_merge_expanded or resolved.cell is None:
            continue

        resolution = _resolve_title_cell(resolved.cell, numeric_date_order)
        if resolution is not None:
            return resolution, column_index

    return None


def _resolve_title_cell(
    cell: NormalizedCell,
    numeric_date_order: NumericDateOrder,
) -> date | None:
    """Read a day title cell as a date, or ``None`` when it states none.

    These titles hang the weekday off the date with a slash — ``31 AĞUSTOS 2026 /
    Pazartesi`` — and the shared resolver trims only the comma and dash forms of
    that separator, so the slash would be left behind and the whole title
    refused. The slash is replaced with a space here rather than in the shared
    primitive: making the resolver accept it would change how a date is read for
    every profile, which under the determinism rule means bumping the engine and
    re-parsing every stored snapshot in the system for a separator only this
    document family writes.

    Only the separator is rewritten. The date itself, the weekday it is checked
    against and the year are all still read by the shared resolver.
    """
    if cell.effective_value is not None and cell.effective_value.text_value is None:
        # A serial or a real date value: one day title is written that way, and
        # rewriting text it does not have would discard it.
        return resolve_cell_date(cell, numeric_order=numeric_date_order).value

    text = cell_display_text(cell)
    if text is None:
        return None
    return resolve_date_text(
        text.replace("/", " "),
        numeric_order=numeric_date_order,
    ).value


def _read_block(
    grid: WorksheetGrid,
    block: _DayBlock,
    diagnostics: ParseDiagnostics,
) -> tuple[list[AmphitheatreAssignment], int]:
    """Every assignment one day block states, and how many slot rows it has."""
    slots = _read_slot_rows(grid, block, diagnostics)
    if not slots:
        return [], 0

    slot_end_by_row = {row_index: end for row_index, _, end in slots}
    assignments: list[AmphitheatreAssignment] = []

    for row_index, slot_start, slot_end in slots:
        for column_index, room in sorted(block.rooms.items()):
            resolved = grid.resolve(row_index, column_index)
            if resolved.is_merge_expanded or not resolved.text:
                # A merged session is read once, at the row that holds its value.
                continue

            end = slot_end
            if resolved.merged_range is not None:
                # The merge spans several slots, so the session runs to the end of
                # the last slot it covers rather than the first.
                last_row = resolved.merged_range.end_row_index_exclusive - 1
                end = max(
                    (
                        candidate_end
                        for candidate_row, candidate_end in slot_end_by_row.items()
                        if row_index <= candidate_row <= last_row
                    ),
                    default=slot_end,
                )

            assignment = _read_assignment(
                grid,
                block,
                row_index=row_index,
                column_index=column_index,
                room=room,
                slot_start=slot_start,
                slot_end=end,
                text=resolved.text,
                diagnostics=diagnostics,
            )
            if assignment is not None:
                assignments.append(assignment)

    return assignments, len(slots)


def _read_slot_rows(
    grid: WorksheetGrid,
    block: _DayBlock,
    diagnostics: ParseDiagnostics,
) -> list[tuple[int, time, time]]:
    """The block's time-slot rows as (row index, start, end)."""
    slots: list[tuple[int, time, time]] = []
    for row_index in range(block.header_row_index + 1, block.end_row_index_exclusive):
        if grid.is_row_hidden(row_index):
            continue

        text = grid.text(row_index, block.slot_column_index)
        if not text:
            continue

        resolution = resolve_time_range_text(text)
        if resolution.start is None or resolution.end is None:
            diagnostics.record_ignored_row(
                REASON_UNRESOLVED_SLOT_TIME,
                grid.evidence(
                    row_index,
                    block.slot_column_index,
                    extraction_rule=RULE_ASSIGNMENT,
                ),
                message=(
                    "The slot column states no readable time range, so no room on the "
                    "row can be given a time."
                ),
            )
            continue

        slots.append((row_index, resolution.start, resolution.end))

    return slots


def _read_assignment(
    grid: WorksheetGrid,
    block: _DayBlock,
    *,
    row_index: int,
    column_index: int,
    room: str,
    slot_start: time,
    slot_end: time,
    text: str,
    diagnostics: ParseDiagnostics,
) -> AmphitheatreAssignment | None:
    """Interpret one occupied room cell, or report why it states no audience.

    A cell that names no class year is not a student lesson this system tracks —
    the committed fixture holds departmental seminars, specialty examinations and
    an occupational-safety course among them — so it is recorded as ignored
    rather than published as an assignment nothing could ever match.
    """
    audience = _read_audience(text)
    if audience.class_year is None:
        diagnostics.record_ignored_cell(
            REASON_NO_CLASS_YEAR,
            grid.evidence(row_index, column_index, extraction_rule=RULE_ASSIGNMENT),
            message=(
                "The cell names no class year, so it states a booking rather than a "
                "lesson any student schedule contains."
            ),
        )
        return None

    start, end, time_is_stated = _resolve_assignment_time(text, slot_start, slot_end)

    return AmphitheatreAssignment(
        local_date=block.local_date,
        start_local_time=start,
        end_local_time=end,
        room=room,
        class_year=audience.class_year,
        program_language=audience.program_language,
        curriculum_group=audience.curriculum_group,
        curriculum_block=audience.curriculum_block,
        department=audience.department,
        raw_text=text,
        time_is_stated=time_is_stated,
        evidence=grid.evidence(row_index, column_index, extraction_rule=RULE_ASSIGNMENT),
    )


def _resolve_assignment_time(
    text: str,
    slot_start: time,
    slot_end: time,
) -> tuple[time, time, bool]:
    """The times the cell states, falling back to the slot row's.

    A stated range replaces both ends. A single trailing time replaces only the
    start, because that is all the source asserted: ``ORTOPEDİ VE
    TRAVMATOLOJİ-10.30`` moves the session inside its slot and says nothing about
    when it ends.
    """
    range_match = _TIME_RANGE_PATTERN.search(text)
    if range_match is not None:
        resolution = resolve_time_range_text(f"{range_match.group(1)}-{range_match.group(2)}")
        if resolution.start is not None and resolution.end is not None:
            return resolution.start, resolution.end, True

    trailing_match = _TRAILING_TIME_PATTERN.search(text)
    if trailing_match is not None:
        stated_start = resolve_time_text(trailing_match.group(1))
        if stated_start.value is not None and stated_start.value < slot_end:
            return stated_start.value, slot_end, True

    return slot_start, slot_end, False


@dataclass(frozen=True, slots=True)
class _Audience:
    class_year: int | None
    program_language: ProgramLanguage | None
    curriculum_group: str | None
    curriculum_block: str | None
    department: str | None


def _read_audience(text: str) -> _Audience:
    """Read the audience facts one room cell states.

    Each fact is recognized by its own shape rather than by segment position,
    because the source writes them in whichever order it likes. What remains
    after every recognized fact is removed is the department, which is the
    segment that makes an assignment joinable to a lesson.
    """
    key = comparison_key(text)

    year_match = _CLASS_YEAR_PATTERN.search(key)
    class_year = int(year_match.group(1)) if year_match else None

    languages = {
        language for token, language in _LANGUAGE_KEYS.items() if re.search(rf"\b{token}\b", key)
    }
    # A session announced for both programs narrows neither of them, so it states
    # no language rather than one of the two.
    program_language = languages.pop() if len(languages) == 1 else None

    curriculum_group = None
    if class_year == 3:
        group_match = _GROUP_ATTACHED_PATTERN.search(key) or _GROUP_WORD_PATTERN.search(key)
        if group_match:
            curriculum_group = f"3-{group_match.group(1).upper()}"

    block, department = _read_block_and_department(text)
    return _Audience(
        class_year=class_year,
        program_language=program_language,
        curriculum_group=curriculum_group,
        curriculum_block=block,
        department=department,
    )


def _read_block_and_department(text: str) -> tuple[str | None, str | None]:
    """The curriculum block and department a cell's dashed list ends with.

    The department is the last segment that states no audience fact and no time.
    The curriculum block, when the cell names one, is the segment before it.
    """
    stripped = _TIME_RANGE_PATTERN.sub(" ", text)
    stripped = _TRAILING_TIME_PATTERN.sub(" ", stripped)

    segments = [
        segment
        for segment in (normalize_text(part) for part in _SEGMENT_SEPARATOR_PATTERN.split(stripped))
        if segment and not _is_audience_segment(segment)
    ]
    if not segments:
        return None, None
    if len(segments) == 1:
        return None, segments[0]
    return segments[-2], segments[-1]


def _is_audience_segment(segment: str) -> bool:
    """Whether a segment states an audience fact rather than lesson content."""
    key = comparison_key(segment)
    if not key:
        return True
    if _CLASS_YEAR_PATTERN.search(key) or key.isdigit():
        return True
    if _GROUP_WORD_PATTERN.search(key):
        return True
    return all(part in _LANGUAGE_KEYS or part == "ve" for part in key.replace("+", " ").split())


#: Why a lesson was given no room, so that every unenriched lesson is explainable.
REASON_NO_ASSIGNMENT = "noAmphitheatreAssignmentForLesson"
REASON_AMBIGUOUS = "ambiguousAmphitheatreAssignment"
#: A room accepted because every booking the cohort had in that hour named it,
#: rather than because the departments agreed. Recorded distinctly so the weaker
#: reason is visible in the metrics instead of hiding inside the total.
REASON_UNANIMOUS_WITHOUT_DEPARTMENT = "unanimousRoomWithoutDepartmentMatch"


@dataclass(frozen=True, slots=True)
class RoomResolution:
    """The room a lesson takes from the amphitheatre program, or why it takes none."""

    room: str | None
    reason: str
    assignment: AmphitheatreAssignment | None = None


class AmphitheatreIndex:
    """Answers "which room is this lesson in?" from one weekly document.

    Every fact the document states must agree with the lesson before a room is
    given, and a fact the document leaves unstated narrows nothing. That
    asymmetry is the safety rule: a room is written onto a student's calendar
    event, so naming the wrong one is worse than naming none, and this document
    genuinely does not state a language or a curriculum group for every booking
    on it.

    Two assignments that survive every test and disagree about the room leave the
    lesson unenriched. That is ADR-035's treatment of an ambiguous match applied
    to a room: nothing here picks a winner.
    """

    def __init__(self, document: AmphitheatreDocument) -> None:
        self._by_day: dict[tuple[date, int], list[AmphitheatreAssignment]] = {}
        for assignment in document.assignments:
            if assignment.class_year is None:
                continue
            self._by_day.setdefault((assignment.local_date, assignment.class_year), []).append(
                assignment
            )

    def __len__(self) -> int:
        return sum(len(items) for items in self._by_day.values())

    def resolve(
        self,
        *,
        local_date: date,
        class_year: int,
        program_language: ProgramLanguage,
        curriculum_groups: Sequence[str],
        departments: Sequence[str],
        start_local_time: time | None,
        end_local_time: time | None,
    ) -> RoomResolution:
        """The room this lesson is held in, when the document leaves no choice.

        The audience and the hour select the bookings a lesson could possibly be,
        and the department narrows them when both sides state one. A room is
        returned only when everything still standing names the same room, so the
        answer never depends on picking between two of them. That is what lets a
        lesson with no stated department still be placed: under half of the
        published lessons name one, and where a cohort has a single booking in an
        hour there is nothing to choose between.
        """
        if start_local_time is None or end_local_time is None:
            # An all-day item occupies no hour the grid could place it in.
            return RoomResolution(None, REASON_NO_ASSIGNMENT)

        group_keys = {comparison_key(value) for value in curriculum_groups if value}
        candidates = [
            assignment
            for assignment in self._by_day.get((local_date, class_year), ())
            if self._matches(
                assignment,
                program_language=program_language,
                group_keys=group_keys,
                start_local_time=start_local_time,
                end_local_time=end_local_time,
            )
        ]
        if not candidates:
            return RoomResolution(None, REASON_NO_ASSIGNMENT)

        # The department is the sharper key, so it is tried first. Falling back to
        # the whole hour when it selects nothing is not a weaker guess: the answer
        # still has to be unanimous below, and an hour in which the cohort has one
        # booking has one room whatever the two documents call the department.
        department_keys = {comparison_key(value) for value in departments if value}
        narrowed = [
            assignment for assignment in candidates if assignment.department_key in department_keys
        ]
        survivors = narrowed or candidates

        if len({comparison_key(assignment.room) for assignment in survivors}) > 1:
            return RoomResolution(None, REASON_AMBIGUOUS)

        return RoomResolution(
            survivors[0].room,
            RULE_ASSIGNMENT if narrowed else REASON_UNANIMOUS_WITHOUT_DEPARTMENT,
            survivors[0],
        )

    @staticmethod
    def _matches(
        assignment: AmphitheatreAssignment,
        *,
        program_language: ProgramLanguage,
        group_keys: set[str],
        start_local_time: time,
        end_local_time: time,
    ) -> bool:
        if (
            assignment.program_language is not None
            and assignment.program_language != program_language
        ):
            return False
        if (
            assignment.curriculum_group is not None
            and group_keys
            and comparison_key(assignment.curriculum_group) not in group_keys
        ):
            return False
        # The lesson and the booking must be the same hour of the day, compared
        # by overlap rather than equality: the grid's slot rows and the annual
        # program's times are two independent statements of one hour and are not
        # written to the same minute — the grid's 09.20-10.00 row carries a
        # lesson the annual program publishes as 09.20-10.10.
        return (
            assignment.start_local_time < end_local_time
            and start_local_time < assignment.end_local_time
        )
