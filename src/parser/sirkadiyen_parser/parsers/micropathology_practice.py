"""Grade 3 Mikrobiyoloji / Tıbbi Patoloji practice program reader.

The faculty publishes one Word document holding the whole Dönem-3 microbiology
and pathology practical calendar for the year. It is a single table with two
parallel tracks — **Mikrobiyoloji** and **Tıbbi Patoloji** — running side by
side down the same dates. A row states a date, and each track's cell on that row
states which practice group attends and which subject block they cover::

    UYGULAMA TARİHLERİ | Mikrobiyoloji  | Tıbbi Patoloji
    06.10.2026         | A1- (H)        |
    13.10.2026         | B1- (KL 1)     | A1- (H) (BB-GÜ)

The two tracks are **crossed**: on one date Mikrobiyoloji teaches Kan-Lenfoid to
group B1 while Tıbbi Patoloji teaches Hareket to group A1. So each cell is
authoritative for its own group and its own subject, and the block headings above
it are never consulted to decide either — the cell is the whole statement.

A cell is ``<group>- (<subject> [session])``, and a pathology cell adds a second
parenthesis naming the two supervising instructors as an abbreviation pair,
``(BB-GÜ)``. The group is the audience (``microPathologyGroup`` A1/A2/B1/B2), so
a cell whose group is not one of the four is refused rather than published to a
guessed audience. The subject and the instructor abbreviations are only labels:
a subject the profile does not recognize is kept **verbatim** with a warning
rather than dropped, and an unrecognized instructor token likewise. Losing the
label loses nothing a calendar cannot show; guessing a group would put a lesson
on the wrong students' calendars.

Every practical runs 14.30-16.20 unless the date cell states an inline override
(two sessions on 25.05.2027 do), and that default is read from the document's own
header rather than invented.
"""

import re
from collections.abc import Mapping
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
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.identity import build_identity_components, content_hash, stable_identity
from sirkadiyen_parser.normalization.courses import course_identity
from sirkadiyen_parser.normalization.dates import (
    NumericDateOrder,
    resolve_date_text,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.text import comparison_key, normalize_text
from sirkadiyen_parser.normalization.times import (
    TimeRangeResolution,
    duration_minutes,
    resolve_time_range_text,
)
from sirkadiyen_parser.parsers.annual import (
    MAX_PLAUSIBLE_DURATION_MINUTES,
    MIN_PLAUSIBLE_DURATION_MINUTES,
    WARNING_IMPLAUSIBLE_DURATION,
    encode_all_day,
)
from sirkadiyen_parser.parsers.date_repair import report_date_corrections
from sirkadiyen_parser.profiles import ParserProfileDefinition

DIMENSION_MICRO_PATHOLOGY_GROUP = "microPathologyGroup"

#: The four practice groups the whole Dönem-3 class is split into for this
#: program. A cell naming any other value states an audience the supported
#: profile schema cannot resolve, so it is refused rather than guessed.
SUPPORTED_GROUPS = frozenset({"A1", "A2", "B1", "B2"})

#: The subject-block abbreviations the cells use, mapped to the block name the
#: document spells out in its own headings. A subject outside this map is kept
#: verbatim as the label rather than dropped (the group, not the subject, is what
#: decides whose calendar changes), so the map is a convenience, not a gate.
SUBJECT_NAMES: Mapping[str, str] = {
    "H": "Hareket",
    "KL": "Kan-Lenfoid",
    "D": "Dolaşım",
    "SL": "Solunum",
    "SND": "Sindirim",
    "END": "Endokrin-Metabolizma",
    "ÜRG": "Ürogenital",
}

#: The instructor abbreviation pairs the pathology cells write, split on the
#: hyphen into two people, each keyed by its own last-two-name initials to the
#: Tıbbi Patoloji öğretim üyesi it names. Resolved from the İstanbul Tıp
#: Fakültesi academic staff directory. An unrecognized token is kept verbatim
#: with a warning rather than dropped.
INSTRUCTOR_NAMES: Mapping[str, str] = {
    "BB": "Mebrure Bilge Bilgiç",
    "GÜ": "Gökçen Ünverengil",
    "GY": "Gülçin Yegen",
    "AYA": "Ali Yılmaz Altay",
    "AB": "Aysel Bayram",
    "ÖH": "Özge Hürdoğan",
    "YÖ": "Mesude Yasemin Özlük",
    "DVB": "Doğu Vuralli Bakkaloğlu",
    "SÖ": "Semen Önder",
    "MB": "Melek Büyük",
    "NB": "Neslihan Berker",
    "ŞÖS": "Şule Öztürk Sarı",
    "BYE": "Begüm Yeni Erdem",
}

#: The header wording that names each track's column, and the department the
#: track belongs to. The date column is found beside them.
TRACK_HEADERS: Mapping[str, str] = {
    "mikrobiyoloji": "Mikrobiyoloji",
    "tibbi patoloji": "Tıbbi Patoloji",
}
DATE_HEADER_KEY = "uygulama"

#: A whole cell: a group, a hyphen, a parenthesised subject (with an optional
#: session number), and — pathology only — a second parenthesis of instructors.
_CELL_PATTERN = re.compile(
    r"^(?P<group>[A-Za-zİÜÇ]\s*\d)\s*-\s*"
    r"\((?P<subject>[^)]*)\)"
    r"(?:\s*\((?P<instructors>[^)]*)\))?$"
)

#: A subject inside the parentheses: a token and an optional trailing session
#: number, e.g. ``KL 1`` or ``END``.
_SUBJECT_PATTERN = re.compile(r"^(?P<token>.+?)(?:\s+(?P<session>\d+))?$")

#: An inline time range a date cell appends after the date, e.g.
#: ``25.05.2027 (13.30-15.20)``.
_INLINE_TIME_PATTERN = re.compile(
    r"\(\s*(?P<range>\d{1,2}\s*[:.]\s*\d{2}\s*[-–—]\s*\d{1,2}\s*[:.]\s*\d{2})\s*\)"
)

#: A cell that is nothing but a time range, used to read the document's default
#: practice hour from its header rather than assuming one.
_PURE_TIME_RANGE_PATTERN = re.compile(r"^\d{1,2}\s*[:.]\s*\d{2}\s*[-–—]\s*\d{1,2}\s*[:.]\s*\d{2}$")

#: Whether a date cell holds any digit. A cell without one is a label or marker
#: row rather than a date the profile failed to read.
_DIGIT_PATTERN = re.compile(r"\d")

#: How far into the worksheet the header row is searched for.
HEADER_SEARCH_ROW_LIMIT = 6

REASON_UNRESOLVED_DATE = "unresolvedPracticeDate"
REASON_UNRESOLVED_CELL = "cellDoesNotStateAPractice"
REASON_UNSUPPORTED_GROUP = "unsupportedPracticeGroupValue"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_NO_TABLE = "noMicroPathologyTable"
WARNING_UNSUPPORTED_GROUP = "unsupportedMicroPathologyGroup"
WARNING_UNKNOWN_SUBJECT = "unrecognizedSubjectAbbreviation"
WARNING_UNKNOWN_INSTRUCTOR = "unrecognizedInstructorAbbreviation"
WARNING_NO_DEFAULT_TIME = "noDefaultPracticeHourInHeader"

RULE_DATE = "microPathology.date"
RULE_CELL = "microPathology.cell"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_DATED_ROWS = "rows.dated"
METRIC_CELLS_SCANNED = "cells.scanned"
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_TRACK_PREFIX = "candidates.track."
METRIC_AUDIENCE_DIMENSION_PREFIX = "audience.dimension."
METRIC_SUBJECTS_UNKNOWN = "subjects.unrecognized"
METRIC_INSTRUCTORS_UNKNOWN = "instructors.unrecognized"


@dataclass(frozen=True, slots=True)
class _Track:
    """One of the two side-by-side practice tracks."""

    column: int
    department: str
    #: Whether this track's cells name their supervising instructors.
    has_instructors: bool


@dataclass(frozen=True, slots=True)
class _Layout:
    """Where the date column and the two track columns sit."""

    date_column: int
    tracks: tuple[_Track, ...]
    default_time: TimeRangeResolution


@dataclass(frozen=True, slots=True)
class _Cell:
    """A practice cell reduced to what it states."""

    group: str
    subject_display: str
    #: The instructor line for the description, ``None`` for a track that names
    #: none. Each person is rendered as ``Full Name (ABBR)``.
    instructor: str | None


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)


def parse_micropathology_practice_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse the Grade 3 microbiology/pathology practice document."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()
    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    report_date_corrections(diagnostics=diagnostics, context=request.source_context)

    layout: _Layout | None = None
    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        layout = _find_layout(grid)
        if layout is None:
            continue
        _parse_worksheet(
            grid=grid,
            layout=layout,
            context=request.source_context,
            numeric_date_order=profile.numeric_date_order,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )
        break

    if layout is None:
        diagnostics.error(
            WARNING_NO_TABLE,
            "No worksheet in the snapshot exposes the microbiology/pathology practice "
            "table, so the snapshot cannot be parsed by this profile.",
        )
    elif not layout.default_time.resolved:
        # The header states the hour every practical runs; refusing to invent one
        # is the same rule ADR-046 states for missing times.
        diagnostics.error(
            WARNING_NO_DEFAULT_TIME,
            "The header states no default practice hour, so no lesson could be given a "
            "time without inventing one.",
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


def _find_layout(grid: WorksheetGrid) -> _Layout | None:
    """Locate the date column and the two track columns from the header rows.

    The columns are found by their header wording rather than by position, so a
    layout change that keeps the wording keeps parsing, and the default practice
    hour is read from the header rather than assumed.
    """
    worksheet = grid.worksheet
    row_limit = min(worksheet.row_count, HEADER_SEARCH_ROW_LIMIT)

    date_column: int | None = None
    tracks: list[_Track] = []
    for row_index in range(row_limit):
        for column in range(worksheet.column_count):
            key = comparison_key(grid.text(row_index, column))
            if not key:
                continue
            if date_column is None and key.startswith(DATE_HEADER_KEY):
                date_column = column
            department = TRACK_HEADERS.get(key)
            if department is not None and all(track.column != column for track in tracks):
                tracks.append(
                    _Track(
                        column=column,
                        department=department,
                        has_instructors=department == TRACK_HEADERS["tibbi patoloji"],
                    )
                )

    if date_column is None or not tracks:
        return None

    return _Layout(
        date_column=date_column,
        tracks=tuple(sorted(tracks, key=lambda track: track.column)),
        default_time=_read_default_time(grid, row_limit),
    )


def _read_default_time(grid: WorksheetGrid, row_limit: int) -> TimeRangeResolution:
    """Read the practice hour the header states, so none is invented."""
    for row_index in range(row_limit):
        for column in range(grid.worksheet.column_count):
            text = grid.text(row_index, column)
            if _PURE_TIME_RANGE_PATTERN.match(text):
                return resolve_time_range_text(text)
    return resolve_time_range_text("")


def _parse_worksheet(
    *,
    grid: WorksheetGrid,
    layout: _Layout,
    context: ParseSourceContext,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    for row_index in range(grid.worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)
        date_text = grid.text(row_index, layout.date_column)
        if not date_text or not _DIGIT_PATTERN.search(date_text):
            # A row whose date cell holds no digits states no session: a header
            # (`UYGULAMA` / `TARİHLERİ`), a block heading, a term marker, an exam
            # divider or a spacer. It carries no candidate and is not an anomaly,
            # so it is counted only in rows.scanned. A cell that does hold digits
            # but resolves to no date is a different case, reported below.
            continue

        local_date, time_range = _resolve_date_and_time(
            date_text,
            numeric_date_order=numeric_date_order,
            default_time=layout.default_time,
        )
        if local_date is None or time_range.start is None or time_range.end is None:
            diagnostics.record_ignored_row(
                REASON_UNRESOLVED_DATE,
                grid.evidence(row_index, layout.date_column, extraction_rule=RULE_DATE),
                severity=ParserWarningSeverity.WARNING,
                message=(
                    f"Date cell '{date_text}' could not be read, so no practical on that "
                    "row was published."
                ),
            )
            continue

        diagnostics.increment(METRIC_DATED_ROWS)
        for track in layout.tracks:
            cell_text = grid.text(row_index, track.column)
            if not cell_text:
                continue
            diagnostics.increment(METRIC_CELLS_SCANNED)
            _parse_track_cell(
                grid=grid,
                row_index=row_index,
                track=track,
                cell_text=cell_text,
                local_date=local_date,
                start=time_range.start,
                end=time_range.end,
                confidence=time_range.confidence,
                context=context,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )


def _resolve_date_and_time(
    date_text: str,
    *,
    numeric_date_order: NumericDateOrder,
    default_time: TimeRangeResolution,
) -> tuple[date | None, TimeRangeResolution]:
    """Read a date cell, honoring an inline ``(HH.MM-HH.MM)`` time override."""
    time_range = default_time
    inline = _INLINE_TIME_PATTERN.search(date_text)
    remainder = date_text
    if inline is not None:
        override = resolve_time_range_text(inline.group("range"))
        if override.resolved:
            time_range = override
        remainder = date_text[: inline.start()].strip()

    resolution = resolve_date_text(remainder, numeric_order=numeric_date_order)
    return resolution.value, time_range


def _parse_track_cell(
    *,
    grid: WorksheetGrid,
    row_index: int,
    track: _Track,
    cell_text: str,
    local_date: date,
    start: time,
    end: time,
    confidence: float,
    context: ParseSourceContext,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    evidence = grid.evidence(row_index, track.column, extraction_rule=RULE_CELL)
    parsed = _read_cell(cell_text, track=track, diagnostics=diagnostics, evidence=evidence)
    if parsed is None:
        return

    display_title = f"{track.department} Uygulama - {parsed.subject_display}"
    selectors = [AudienceSelector(dimension=DIMENSION_MICRO_PATHOLOGY_GROUP, value=parsed.group)]
    audience_key = f"{DIMENSION_MICRO_PATHOLOGY_GROUP}:{parsed.group}"
    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", local_date.isoformat()),
            ("startLocalTime", start.isoformat()),
            ("courseIdentity", course_identity(display_title) or ""),
            ("audience", audience_key),
        )
    )
    candidate = CanonicalScheduleCandidate(
        candidate_id=f"{grid.worksheet.sheet_id}!R{row_index + 1}C{track.column + 1}",
        academic_year=context.academic_year,
        class_year=context.class_year,
        program_language=context.program_language,
        audience=ScheduleAudienceCandidate(
            scope=AudienceScope.SELECTED_GROUPS,
            selectors=selectors,
        ),
        event_type=ScheduleEventType.PRACTICE,
        status=CandidateRecordStatus.SCHEDULED,
        normalized_course_identity=course_identity(display_title),
        display_title=display_title,
        local_date=local_date,
        start_local_time=start,
        end_local_time=end,
        time_zone_id=context.time_zone_id,
        instructor=parsed.instructor,
        location=None,
        curriculum_block=None,
        departments=[track.department],
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": display_title,
                "eventType": ScheduleEventType.PRACTICE.value,
                "localDate": local_date.isoformat(),
                "isAllDay": encode_all_day(False),
                "startLocalTime": start.isoformat(),
                "endLocalTime": end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "instructor": parsed.instructor,
                "location": None,
                "curriculumBlock": None,
                "departments": track.department,
                "audience": audience_key,
            }
        ),
        confidence=confidence,
        identity_components=identity_components,
        evidence=[evidence],
    )
    _accept(
        candidate=candidate,
        track=track,
        start=start,
        end=end,
        evidence=evidence,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


def _read_cell(
    cell_text: str,
    *,
    track: _Track,
    diagnostics: ParseDiagnostics,
    evidence: SourceEvidence,
) -> _Cell | None:
    """Read one practice cell, or refuse it with its address.

    A cell whose group is not one of the four supported ones is refused: the
    group is the audience, and publishing to a guessed one is the failure this
    source exists to avoid. A subject or instructor abbreviation the profile does
    not recognize is kept verbatim with a warning instead, because losing a label
    loses nothing a group value protects.
    """
    match = _CELL_PATTERN.match(normalize_text(cell_text))
    if match is None:
        diagnostics.record_ignored_cell(
            REASON_UNRESOLVED_CELL,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{cell_text}' does not state a group and a subject, so no "
                "practical was published for it."
            ),
        )
        return None

    group = re.sub(r"\s+", "", match.group("group")).upper()
    if group not in SUPPORTED_GROUPS:
        diagnostics.record_ignored_cell(
            REASON_UNSUPPORTED_GROUP,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell states practice group '{group}', which is not one of "
                f"{sorted(SUPPORTED_GROUPS)}, so no lesson was published for it — its "
                "audience cannot be resolved."
            ),
        )
        diagnostics.warning(
            WARNING_UNSUPPORTED_GROUP,
            f"Practice group '{group}' is not a supported microPathologyGroup value.",
            evidence=evidence,
        )
        return None

    subject_display = _read_subject(
        match.group("subject"), diagnostics=diagnostics, evidence=evidence
    )
    instructor = (
        _read_instructors(match.group("instructors"), diagnostics=diagnostics, evidence=evidence)
        if track.has_instructors
        else None
    )
    return _Cell(group=group, subject_display=subject_display, instructor=instructor)


def _read_subject(
    subject_text: str,
    *,
    diagnostics: ParseDiagnostics,
    evidence: SourceEvidence,
) -> str:
    """Expand a subject abbreviation, keeping an unknown one verbatim."""
    match = _SUBJECT_PATTERN.match(normalize_text(subject_text))
    if match is None:  # pragma: no cover - the pattern matches any non-empty text
        return normalize_text(subject_text)

    token = match.group("token").strip()
    session = match.group("session")
    name = SUBJECT_NAMES.get(token.upper())
    if name is None:
        diagnostics.increment(METRIC_SUBJECTS_UNKNOWN)
        diagnostics.warning(
            WARNING_UNKNOWN_SUBJECT,
            f"Subject abbreviation '{token}' is not recognized, so it is kept verbatim "
            "in the lesson title.",
            evidence=evidence,
        )
        name = token
    return f"{name} {session}" if session else name


def _read_instructors(
    instructors_text: str | None,
    *,
    diagnostics: ParseDiagnostics,
    evidence: SourceEvidence,
) -> str | None:
    """Resolve an ``BB-GÜ`` instructor pair to ``Full Name (ABBR), ...``.

    An unrecognized token is kept verbatim so the calendar still shows the
    abbreviation the printed program uses, with a warning so the gap is visible.
    """
    if not instructors_text:
        return None

    rendered: list[str] = []
    for token in (part.strip() for part in instructors_text.split("-")):
        if not token:
            continue
        name = INSTRUCTOR_NAMES.get(token.upper())
        if name is None:
            diagnostics.increment(METRIC_INSTRUCTORS_UNKNOWN)
            diagnostics.warning(
                WARNING_UNKNOWN_INSTRUCTOR,
                f"Instructor abbreviation '{token}' is not recognized, so it is kept "
                "verbatim in the lesson description.",
                evidence=evidence,
            )
            rendered.append(token)
        else:
            rendered.append(f"{name} ({token})")

    return ", ".join(rendered) if rendered else None


def _accept(
    *,
    candidate: CanonicalScheduleCandidate,
    track: _Track,
    start: time,
    end: time,
    evidence: SourceEvidence,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    existing = accumulator.by_identity.get(candidate.stable_identity)
    if existing is not None:
        diagnostics.record_ignored_cell(
            REASON_DUPLICATE_IDENTITY,
            evidence,
            severity=(
                ParserWarningSeverity.INFORMATION
                if existing.content_hash == candidate.content_hash
                else ParserWarningSeverity.WARNING
            ),
            message=(
                f"Cell repeats the lesson already published as candidate "
                f"'{existing.candidate_id}', so it was not published again."
            ),
        )
        return

    accumulator.by_identity[candidate.stable_identity] = candidate
    accumulator.candidates.append(candidate)
    diagnostics.increment(f"{METRIC_TRACK_PREFIX}{track.department}")
    for selector in candidate.audience.selectors:
        diagnostics.increment(f"{METRIC_AUDIENCE_DIMENSION_PREFIX}{selector.dimension}")

    duration = duration_minutes(start, end)
    if not MIN_PLAUSIBLE_DURATION_MINUTES <= duration <= MAX_PLAUSIBLE_DURATION_MINUTES:
        diagnostics.warning(
            WARNING_IMPLAUSIBLE_DURATION,
            f"Practice lasts {duration} minutes, outside the plausible range of "
            f"{MIN_PLAUSIBLE_DURATION_MINUTES} to {MAX_PLAUSIBLE_DURATION_MINUTES} minutes.",
            candidate_id=candidate.candidate_id,
            evidence=evidence,
        )
