"""Grade 3 bedside-practice document reader.

This profile publishes **no schedule candidates**, and that is the whole design.

The annual workbook already states every one of the 92 bedside sessions with a
date, a start and an end time, and the department each curriculum group sits
with. This document states a date and a topic code, and the only time it writes
anywhere is a heading over its topic catalogue::

    HASTA BAŞI UYGULAMA KONULARI (13.30-14.20)

That heading is wrong for twenty-two of the sessions — the annual puts the
Friday ones at 14:00-14:50 — so publishing from here would either move real
lessons or duplicate the ones the annual already publishes correctly. The annual
owns the slot, and this document owns the *topic* (ADR-100).

What it is, then, is a reader: it turns the document into the two tables the
annual profile needs to put a topic on an event it already publishes.

    schedule:  (curriculum group, date) -> topic code
    catalogue: (department section, ordinal) -> topic text

The catalogue is keyed by **section and ordinal, never by the code's prefix**.
The prefixes are not consistent: the same document writes ``İçH`` and ``IçH``
with two different capital I's, ``ÇSH`` and ``ÇSvH`` for one department, and one
``İÇSH`` that belongs to the child-health section despite starting like the
internal-medicine one. The section headings it is written under are unambiguous,
so those decide, and the prefix is only used to tell which section a code in the
schedule table refers to.

The two documents are not the same shape. The A group's schedule table puts a
blank spacer column between its autumn and spring pairs and the B group's does
not, so the columns are paired by header rather than by position.
"""

import re
from collections.abc import Iterator, Mapping
from dataclasses import dataclass, field
from datetime import date
from functools import partial

from sirkadiyen_parser.contracts.parsing import (
    ParserProfileDescriptor,
    ParseSnapshotRequest,
    ParseSnapshotResponse,
    ParseSourceContext,
    SourceEvidence,
)
from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.normalization.date_sequence import DateSequence, DateSequenceEntry
from sirkadiyen_parser.normalization.dates import NumericDateOrder, resolve_date_text
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text
from sirkadiyen_parser.parsers.date_repair import (
    RULE_DATE_SEQUENCE,
    read_date_run,
    report_date_corrections,
    report_date_run,
)
from sirkadiyen_parser.profiles import ParserProfileDefinition

#: The department sections the catalogue is written in. A section is identified
#: by its own heading, and a code in the schedule table is matched to one by its
#: prefix — the two prefixes the schedule uses are unambiguous even though the
#: catalogue's are not.
SECTION_INTERNAL_MEDICINE = "internalMedicine"
SECTION_CHILD_HEALTH = "childHealth"

_SECTION_HEADING_TOKENS: Mapping[str, tuple[str, ...]] = {
    SECTION_CHILD_HEALTH: ("cocuk sagligi",),
    SECTION_INTERNAL_MEDICINE: ("ic hastaliklari",),
}

#: How a schedule-table code names its section. `csvh` and `csh` are the same
#: department written two ways; `ich` covers both capital I's the source uses,
#: because the comparison key folds them together.
_CODE_PREFIX_SECTIONS: Mapping[str, str] = {
    "csvh": SECTION_CHILD_HEALTH,
    "csh": SECTION_CHILD_HEALTH,
    "icsh": SECTION_CHILD_HEALTH,
    "ich": SECTION_INTERNAL_MEDICINE,
}

#: A topic code: a department prefix, the letter U, and an ordinal. The source
#: spaces and hyphenates it a dozen ways (`İçH - U 1`, `İçH U1`, `ÇSH -U11`),
#: and writes one entry for a run of ordinals (`IçH - U 39-43`).
_CODE_PATTERN = re.compile(
    r"^(?P<prefix>[^\W\d_]+)\s*-?\s*U\s*\.?\s*(?P<first>\d{1,2})(?:\s*-\s*(?P<last>\d{1,2}))?"
    r"(?P<rest>\s.*)?$",
    re.IGNORECASE,
)

DATE_HEADER_KEYS = frozenset({"tarih", "date"})

#: A schedule-table group header, which names the curriculum group the column
#: belongs to: `A Grubu` in one document and `B GRUBU` in the other.
_GROUP_HEADER_PATTERN = re.compile(r"^(?P<letter>[ab])\s*gru(bu|p)$", re.IGNORECASE)

#: How far into a worksheet the schedule header row is searched for.
HEADER_SEARCH_ROW_LIMIT = 10

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_SCHEDULE_ROWS = "schedule.rows"
METRIC_SCHEDULE_ENTRIES = "schedule.entries"
METRIC_CATALOGUE_TOPICS = "catalogue.topics"
METRIC_TOPICS_RESOLVED = "schedule.entries.withTopic"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"

REASON_UNRESOLVED_SLOT_DATE = "unresolvedSlotDate"
REASON_UNRESOLVED_TOPIC_CODE = "unresolvedTopicCode"

WARNING_NO_SCHEDULE_TABLE = "noBedsideScheduleTable"
WARNING_TOPIC_NOT_IN_CATALOGUE = "topicCodeNotInCatalogue"
WARNING_PUBLISHES_NO_SESSIONS = "bedsideDocumentPublishesNoSessions"

RULE_SLOT_DATE = "bedside.slotDate"
RULE_TOPIC_CODE = "bedside.topicCode"


@dataclass(frozen=True, slots=True)
class TopicCode:
    """A topic code as the document writes it, reduced to what identifies it."""

    section: str
    ordinal: int


@dataclass(frozen=True, slots=True)
class BedsideSlot:
    """One dated bedside session, as the schedule table states it."""

    curriculum_group: str
    local_date: date
    code: TopicCode
    raw_code: str


@dataclass(slots=True)
class BedsideDocument:
    """Everything this document says, in the two shapes a caller needs."""

    slots: list[BedsideSlot] = field(default_factory=list)
    topics: dict[TopicCode, str] = field(default_factory=dict)

    def topic_for(self, slot: BedsideSlot) -> str | None:
        return self.topics.get(slot.code)

    def topics_by_date(self) -> dict[tuple[str, date], str]:
        """The topic text for each group and date, for callers joining on both.

        A slot whose code is not in the catalogue is absent rather than present
        and empty: an event without a topic keeps the description it already
        has, and a guessed topic is worse than none.
        """
        return {
            (slot.curriculum_group, slot.local_date): topic
            for slot in self.slots
            if (topic := self.topic_for(slot)) is not None
        }


def read_bedside_document(
    snapshot: NormalizedSpreadsheetSnapshot,
    *,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics | None = None,
) -> BedsideDocument:
    """Read a bedside snapshot into its schedule and its topic catalogue.

    Shared by this profile and by the annual profile that puts these topics on
    the events it publishes, so both read the document exactly one way.
    """
    diagnostics = diagnostics or ParseDiagnostics()
    document = BedsideDocument()

    # The catalogue runs across worksheets, because a worksheet is only where
    # Word happened to end a table (ADR-076). The A document wraps four of its
    # topics in one-cell tables, which puts the code at the end of one worksheet
    # and its description at the start of the next; reading each worksheet on its
    # own would lose exactly those topics.
    catalogue = _CatalogueReader(document)

    for worksheet in snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        header = _find_schedule_header(grid)
        if header is None:
            catalogue.read(grid)
            continue

        catalogue.flush()
        header_row, pairs = header
        _read_schedule_into(
            document,
            grid=grid,
            header_row=header_row,
            pairs=pairs,
            context=context,
            numeric_date_order=numeric_date_order,
            diagnostics=diagnostics,
        )

    catalogue.flush()
    return document


def parse_bedside_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Read a bedside document and deliberately publish nothing from it."""
    diagnostics = ParseDiagnostics()
    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))

    report_date_corrections(diagnostics=diagnostics, context=request.source_context)
    document = read_bedside_document(
        request.snapshot,
        context=request.source_context,
        numeric_date_order=profile.numeric_date_order,
        diagnostics=diagnostics,
    )

    resolved = sum(1 for slot in document.slots if document.topic_for(slot) is not None)
    diagnostics.set_metric(METRIC_SCHEDULE_ENTRIES, len(document.slots))
    diagnostics.set_metric(METRIC_CATALOGUE_TOPICS, len(document.topics))
    diagnostics.set_metric(METRIC_TOPICS_RESOLVED, resolved)
    diagnostics.set_metric(METRIC_CANDIDATES_EMITTED, 0)

    if not document.slots:
        diagnostics.error(
            WARNING_NO_SCHEDULE_TABLE,
            "No worksheet in the snapshot holds a bedside schedule table, so the "
            "document states no dated session at all.",
        )
    else:
        diagnostics.information(
            WARNING_PUBLISHES_NO_SESSIONS,
            f"Read {len(document.slots)} dated bedside sessions and "
            f"{len(document.topics)} catalogue topics, of which {resolved} sessions could "
            "be given a topic. No session is published from this document: it states no "
            "per-session time, and the annual program publishes these events with the "
            "time each of them actually has.",
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


def _find_schedule_header(grid: WorksheetGrid) -> tuple[int, list[tuple[int, int, str]]] | None:
    """The header row and its (date column, code column, group) triples.

    The columns are paired left to right rather than by position: one document
    separates its autumn and spring pairs with a blank column and the other does
    not, so a fixed index reads the wrong column in one of them.
    """
    worksheet = grid.worksheet
    row_limit = min(worksheet.row_count, HEADER_SEARCH_ROW_LIMIT)

    for row_index in range(row_limit):
        pairs: list[tuple[int, int, str]] = []
        pending_date_column: int | None = None
        for column in range(worksheet.column_count):
            key = comparison_key(grid.text(row_index, column))
            if not key:
                continue
            if key in DATE_HEADER_KEYS:
                pending_date_column = column
                continue
            match = _GROUP_HEADER_PATTERN.match(key)
            if match is not None and pending_date_column is not None:
                pairs.append((pending_date_column, column, match.group("letter").upper()))
                pending_date_column = None

        if pairs:
            return row_index, pairs

    return None


def _read_schedule_into(
    document: BedsideDocument,
    *,
    grid: WorksheetGrid,
    header_row: int,
    pairs: list[tuple[int, int, str]],
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> None:
    # Each group's date column runs down the table in order — autumn on the left
    # and spring on the right — so every column is its own chronological run and
    # a date that contradicts its own is read as a mistyped year (ADR-139). The
    # two columns are deliberately not one run: they are two halves of a year
    # written side by side, and reading them as one would report every spring
    # date as an anomaly.
    sequences: dict[int, DateSequence] = {}
    for date_column, _, _ in pairs:
        sequence = read_date_run(
            tuple(
                _column_dates(
                    grid=grid,
                    header_row=header_row,
                    date_column=date_column,
                    numeric_date_order=numeric_date_order,
                )
            ),
            context=context,
        )
        report_date_run(
            sequence,
            diagnostics=diagnostics,
            evidence_for=partial(_column_evidence, grid, date_column),
        )
        sequences[date_column] = sequence

    for row_index in range(header_row + 1, grid.worksheet.row_count):
        diagnostics.increment(METRIC_SCHEDULE_ROWS)
        for date_column, code_column, letter in pairs:
            date_text = grid.text(row_index, date_column)
            code_text = grid.text(row_index, code_column)
            if not date_text and not code_text:
                continue

            resolution = sequences[date_column].resolution(
                row_index,
                resolve_date_text(date_text, numeric_order=numeric_date_order),
            )
            if not resolution.resolved or resolution.value is None:
                diagnostics.record_ignored_cell(
                    REASON_UNRESOLVED_SLOT_DATE,
                    grid.evidence(row_index, date_column, extraction_rule=RULE_SLOT_DATE),
                    message=(
                        f"Slot date '{date_text}' could not be read ({resolution.reason}), "
                        "so its topic cannot be attached to a session."
                    ),
                )
                continue

            code = _read_topic_code(code_text)
            if code is None:
                diagnostics.record_ignored_cell(
                    REASON_UNRESOLVED_TOPIC_CODE,
                    grid.evidence(row_index, code_column, extraction_rule=RULE_TOPIC_CODE),
                    message=(
                        f"Cell '{code_text}' does not name a topic code, so the session on "
                        f"{resolution.value.isoformat()} was read without one."
                    ),
                )
                continue

            document.slots.append(
                BedsideSlot(
                    curriculum_group=f"{context.class_year}-{letter}",
                    local_date=resolution.value,
                    code=code,
                    raw_code=code_text,
                )
            )


def _column_evidence(grid: WorksheetGrid, column: int, row_index: int) -> SourceEvidence:
    """Point at one cell of a group's date column.

    A named function rather than a closure over the loop variable: the loop builds
    one of these per column, and a lambda capturing ``date_column`` would either
    close over the last one or need a default argument, which is the shape mypy
    cannot infer a type for.
    """
    return grid.evidence(row_index, column, extraction_rule=RULE_DATE_SEQUENCE)


def _column_dates(
    *,
    grid: WorksheetGrid,
    header_row: int,
    date_column: int,
    numeric_date_order: NumericDateOrder,
) -> Iterator[DateSequenceEntry]:
    """Resolve one group's date column below the header, in row order."""
    for row_index in range(header_row + 1, grid.worksheet.row_count):
        date_text = grid.text(row_index, date_column)
        if not date_text:
            continue
        yield DateSequenceEntry(
            key=row_index,
            resolution=resolve_date_text(date_text, numeric_order=numeric_date_order),
        )


@dataclass(slots=True)
class _CatalogueReader:
    """Reads the prose catalogue, carrying its state across worksheets.

    A code line is followed by the lines describing it, up to the next code or
    section heading. The section in force decides which department the code
    belongs to, because the prefixes the source writes do not agree with
    themselves.
    """

    document: BedsideDocument
    section: str | None = None
    pending: list[TopicCode] = field(default_factory=list)
    lines: list[str] = field(default_factory=list)

    def read(self, grid: WorksheetGrid) -> None:
        for row_index in range(grid.worksheet.row_count):
            for column in range(grid.worksheet.column_count):
                resolved = grid.resolve(row_index, column)
                if resolved.is_merge_expanded:
                    continue
                for line in _text_lines(resolved.text):
                    self._read_line(line)

    def flush(self) -> None:
        """Attach the description read so far to the codes waiting for it."""
        if not self.pending:
            self.lines.clear()
            return

        text = normalize_text(" ".join(self.lines))
        if text:
            for code in self.pending:
                # The first description a code is given wins: a later repetition
                # is the document restating it, not correcting it.
                self.document.topics.setdefault(code, text)
        self.pending.clear()
        self.lines.clear()

    def _read_line(self, line: str) -> None:
        codes, description = _codes_of_line(line, self.section)
        if codes:
            self.flush()
            self.pending.extend(codes)
            if description:
                self.lines.append(description)
            return

        heading = _section_of_heading(line)
        if heading is not None:
            self.flush()
            self.section = heading
            return

        if self.pending:
            self.lines.append(line)


def _codes_of_line(line: str, section: str | None) -> tuple[tuple[TopicCode, ...], str]:
    """The topic codes a line opens with, and whatever follows them on it.

    A code usually sits on its own line, but not always: a Word cell holding a
    code and its description reaches the parser as one line, because a cell's
    internal line breaks are not preserved. So the description is returned too,
    and an empty one means the description is on the lines that follow.

    A range (``IçH - U 39-43``) names every ordinal it spans: the source wrote
    one description for the run, and each of those sessions has that topic.
    """
    match = _CODE_PATTERN.match(line.strip())
    if match is None:
        return (), ""

    resolved_section = _CODE_PREFIX_SECTIONS.get(comparison_key(match.group("prefix"))) or section
    if resolved_section is None:
        return (), ""

    first = int(match.group("first"))
    last = int(match.group("last") or first)
    # A run longer than the catalogue ever writes is not a range but a sentence
    # that happens to hold two numbers.
    if last < first or last - first > 12:
        return (), ""

    codes = tuple(
        TopicCode(section=resolved_section, ordinal=ordinal) for ordinal in range(first, last + 1)
    )
    return codes, (match.group("rest") or "").strip()


def _section_of_heading(line: str) -> str | None:
    key = comparison_key(line)
    if not key or len(key) > 90:
        return None
    # A code line can also read as upper case, so the code test runs first at
    # the call site; here only the department wording matters.
    for section, tokens in _SECTION_HEADING_TOKENS.items():
        if any(token in key for token in tokens):
            return section
    return None


def _read_topic_code(text: str) -> TopicCode | None:
    """The single code a schedule cell names, or ``None``.

    Unlike the catalogue, a schedule cell states nothing but a code, so anything
    trailing it means the cell is something else.
    """
    match = _CODE_PATTERN.match(text.strip())
    if match is None or (match.group("rest") or "").strip():
        return None
    section = _CODE_PREFIX_SECTIONS.get(comparison_key(match.group("prefix")))
    if section is None:
        return None
    return TopicCode(section=section, ordinal=int(match.group("first")))


def _text_lines(value: str | None) -> list[str]:
    if not value:
        return []
    return [stripped for line in value.splitlines() if (stripped := line.strip())]
