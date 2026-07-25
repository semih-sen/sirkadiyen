"""Anatomy dissection group-list parser.

This is the source ADR-073 defers the dissection rotation to. The Grade 2 annual
program writes one dissection session as three consecutive daily slots with the
same session number, and excludes them, because publishing all three would book
every student into two hours they must not attend. This document says which hour
each student attends.

It is the simplest schedule the faculty publishes and the most repetitive: three
columns — a date, one of the three dissection hours, and the anatomy group `1`,
`2` or `3` attending it — and three rows per teaching day, the groups rotating
between them. The anatomy group is independent of a student's practice group.

The one thing that makes it hard is that **the same document states a day two
different ways**. In the later rows a day is a vertical merge over its three
hours. In the earlier ones the date is simply typed into the middle row of the
three, with the cells above and below left empty — the same thing to a reader,
nothing at all to a grid. So a day is recognized by its own shape instead: a run
of rows whose hours advance, stating exactly one date between them. A run that
states no date, or more than one, publishes nothing.
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
from sirkadiyen_parser.normalization.courses import course_identity
from sirkadiyen_parser.normalization.dates import (
    DateResolution,
    NumericDateOrder,
    resolve_date_text,
)
from sirkadiyen_parser.normalization.grid import WorksheetGrid
from sirkadiyen_parser.normalization.groups import GroupExpression, parse_group_expression
from sirkadiyen_parser.normalization.times import duration_minutes, resolve_time_range_text
from sirkadiyen_parser.parsers.annual import (
    MAX_PLAUSIBLE_DURATION_MINUTES,
    METRIC_DATE_RULE_PREFIX,
    MIN_PLAUSIBLE_DURATION_MINUTES,
    WARNING_IMPLAUSIBLE_DURATION,
    encode_all_day,
)
from sirkadiyen_parser.parsers.cohort_rotation import date_refusal
from sirkadiyen_parser.profiles import ParserProfileDefinition

DATE_COLUMN = 0
TIME_COLUMN = 1
GROUP_COLUMN = 2

DIMENSION_ANATOMY_GROUP = "anatomyGroup"

#: The three anatomy groups both committed documents state, and the only values
#: this profile publishes. They are independent of a student's practice group,
#: so a value outside them is refused rather than read as one.
_GROUP_VALUE_PATTERN = re.compile(r"^[1-3]$")

#: A date read from another row of the same day rather than from the row being
#: published. It is the document's own date, but the association is a rule of
#: this profile, so it scores below a date the row states itself — the same way
#: a year supplied by profile configuration does.
CONFIDENCE_DATE_FROM_DAY_BLOCK = 0.8

REASON_NOT_A_DISSECTION_ROW = "notADissectionRow"
REASON_UNRESOLVED_TIME = "unresolvedDissectionTimeRange"
REASON_UNRESOLVED_GROUP = "unresolvedAnatomyGroup"
REASON_UNSUPPORTED_GROUP_VALUE = "unsupportedAnatomyGroupValue"
REASON_DAY_WITHOUT_DATE = "dayBlockWithoutDate"
REASON_DAY_WITH_SEVERAL_DATES = "dayBlockWithSeveralDates"
#: The remaining hours of a day already refused with its own address, counted so
#: every scanned row is still accounted for without raising the same alarm three
#: times.
REASON_ROW_IN_REFUSED_DAY = "rowInRefusedDay"
REASON_DUPLICATE_IDENTITY = "duplicateStableIdentity"

WARNING_NO_ROTATION = "worksheetWithoutDissectionRows"
WARNING_NO_LESSON_TITLE = "profileStatesNoLessonTitle"
WARNING_DAY_REFUSED = "dissectionDayRefused"
WARNING_CONFLICTING_DUPLICATE = "conflictingDuplicateLesson"

METRIC_WORKSHEETS_SCANNED = "worksheets.scanned"
METRIC_WORKSHEETS_SELECTED = "worksheets.selected"
METRIC_WORKSHEETS_IGNORED = "worksheets.ignored.noDissectionRows"
METRIC_ROWS_SCANNED = "rows.scanned"
METRIC_ROWS_DISSECTION = "rows.dissection"
#: Rows whose date came from another row of the same day, which is the earlier
#: of the two ways this document writes a day.
METRIC_ROWS_DATE_FROM_DAY_BLOCK = "rows.dateFromDayBlock"
METRIC_DAYS_DETECTED = "days.detected"
METRIC_DAYS_REFUSED_PREFIX = "days.ignored."
METRIC_CANDIDATES_EMITTED = "candidates.emitted"
METRIC_CANDIDATE_EVENT_TYPE_PREFIX = "candidates.eventType."
METRIC_AUDIENCE_DIMENSION_PREFIX = "audience.dimension."

RULE_DATE_CELL = "anatomy.dateCell"
RULE_TIME_CELL = "anatomy.timeRangeCell"
RULE_GROUP_CELL = "anatomy.groupCell"
RULE_ROW = "anatomy.row"

#: Reported per candidate whose date was attributed by the day-block rule.
CONFIDENCE_REASON_DAY_BLOCK = "dateFromNeighbouringRowInDayBlock"


@dataclass(frozen=True, slots=True)
class _Row:
    """One dissection hour: a time range, a group, and possibly a date."""

    row_index: int
    start: time
    end: time
    time_confidence: float
    expression: GroupExpression
    group_text: str
    #: The date this row states itself, if it states one. A row inside a
    #: vertical merge counts as stating it: the merge is the document saying so.
    resolved_date: DateResolution | None
    date_text: str
    date_row_index: int | None


@dataclass(slots=True)
class _Accumulator:
    candidates: list[CanonicalScheduleCandidate] = field(default_factory=list)
    by_identity: dict[str, CanonicalScheduleCandidate] = field(default_factory=dict)


def parse_anatomy_snapshot(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
) -> ParseSnapshotResponse:
    """Parse an anatomy group list into candidate dissection sessions."""
    diagnostics = ParseDiagnostics()
    accumulator = _Accumulator()

    diagnostics.set_metric(METRIC_WORKSHEETS_SCANNED, len(request.snapshot.worksheets))
    title = profile.annual_markers[0] if profile.annual_markers else ""
    if not title:
        # The rows of this document name no lesson: they are a date, an hour and
        # a group. The title is the one the annual program uses for the same
        # lesson, and the profile declares it, so a profile that declares none
        # cannot publish anything under a name of the parser's invention.
        diagnostics.error(
            WARNING_NO_LESSON_TITLE,
            f"Profile '{profile.name}' declares no annual marker, so the lessons in this "
            "document have no title and none may be invented for them.",
        )
        return _respond(request, profile, diagnostics, accumulator)

    selected = 0
    for worksheet in request.snapshot.worksheets:
        grid = WorksheetGrid(worksheet)
        if _parse_worksheet(
            grid=grid,
            context=request.source_context,
            profile=profile,
            title=title,
            diagnostics=diagnostics,
            accumulator=accumulator,
        ):
            selected += 1
            continue

        diagnostics.increment(METRIC_WORKSHEETS_IGNORED)
        diagnostics.information(
            WARNING_NO_ROTATION,
            f"Worksheet '{worksheet.title}' states no dissection hour and was not parsed.",
            evidence=_worksheet_evidence(worksheet),
        )

    diagnostics.set_metric(METRIC_WORKSHEETS_SELECTED, selected)
    if selected == 0:
        diagnostics.error(
            WARNING_NO_ROTATION,
            "No worksheet in the snapshot states a dissection hour, so the snapshot "
            "cannot be parsed by this profile.",
        )

    return _respond(request, profile, diagnostics, accumulator)


def _respond(
    request: ParseSnapshotRequest,
    profile: ParserProfileDefinition,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> ParseSnapshotResponse:
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
        extraction_rule=RULE_ROW,
    )


def _parse_worksheet(
    *,
    grid: WorksheetGrid,
    context: ParseSourceContext,
    profile: ParserProfileDefinition,
    title: str,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> bool:
    """Walk a worksheet once, gathering dissection hours into days.

    A day ends where the hours stop advancing. That boundary is read from the
    document rather than assumed: its three hours always run 13:30, 14:30 and
    15:30, so a row whose hour does not follow the previous one begins the next
    day. A row that is not a dissection hour at all — the heading — ends the day
    before it and is counted.
    """
    day: list[_Row] = []
    parsed_any = False

    for row_index in range(grid.worksheet.row_count):
        diagnostics.increment(METRIC_ROWS_SCANNED)
        row = _read_row(
            grid=grid,
            row_index=row_index,
            numeric_date_order=profile.numeric_date_order,
            diagnostics=diagnostics,
        )
        if row is None:
            _publish_day(
                grid=grid,
                day=day,
                context=context,
                title=title,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )
            day = []
            continue

        if day and row.start <= day[-1].start:
            _publish_day(
                grid=grid,
                day=day,
                context=context,
                title=title,
                diagnostics=diagnostics,
                accumulator=accumulator,
            )
            day = []

        day.append(row)
        parsed_any = True

    _publish_day(
        grid=grid,
        day=day,
        context=context,
        title=title,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )
    return parsed_any


def _read_row(
    *,
    grid: WorksheetGrid,
    row_index: int,
    numeric_date_order: NumericDateOrder,
    diagnostics: ParseDiagnostics,
) -> _Row | None:
    """Read one dissection hour, or account for a row that states none."""
    resolved_time = grid.resolve(row_index, TIME_COLUMN)
    # The document's heading is one cell merged across all three columns, so
    # every column of that row reads back as the heading text. A value that came
    # from another column is not this column's value.
    if resolved_time.value_column_index != TIME_COLUMN:
        diagnostics.record_ignored_row(REASON_NOT_A_DISSECTION_ROW, _row_evidence(grid, row_index))
        return None

    time_text = resolved_time.text
    group_text = grid.text(row_index, GROUP_COLUMN)
    if not time_text and not group_text:
        diagnostics.record_ignored_row(REASON_NOT_A_DISSECTION_ROW, _row_evidence(grid, row_index))
        return None

    time_range = resolve_time_range_text(time_text)
    if not time_range.resolved or time_range.start is None or time_range.end is None:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_TIME,
            grid.evidence(row_index, TIME_COLUMN, extraction_rule=RULE_TIME_CELL),
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Dissection hour '{time_text}' could not be read ({time_range.reason}), "
                "so the row was not published."
            ),
        )
        return None

    expression = parse_group_expression(group_text, dimension=DIMENSION_ANATOMY_GROUP)
    resolved = grid.resolve(row_index, DATE_COLUMN)
    date_text = resolved.text
    # A row inside a vertical merge states the date as much as the anchor does:
    # the merge is the document saying the three hours are one day. Only an
    # empty, unmerged cell leaves the date to the day-block rule.
    resolved_date = (
        resolve_date_text(date_text, numeric_order=numeric_date_order) if date_text else None
    )

    diagnostics.increment(METRIC_ROWS_DISSECTION)
    return _Row(
        row_index=row_index,
        start=time_range.start,
        end=time_range.end,
        time_confidence=time_range.confidence,
        expression=expression,
        group_text=group_text,
        resolved_date=resolved_date,
        date_text=date_text,
        date_row_index=resolved.value_row_index if date_text else None,
    )


def _publish_day(
    *,
    grid: WorksheetGrid,
    day: Sequence[_Row],
    context: ParseSourceContext,
    title: str,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    """Publish one teaching day, or refuse it whole.

    The day is the unit of refusal on purpose. Its three hours are one rotation:
    publishing the hour that happens to state the date and dropping the two
    beside it would give two of the three groups no session and the third a
    session it may not be theirs.
    """
    if not day:
        return

    diagnostics.increment(METRIC_DAYS_DETECTED)
    dated = [row for row in day if row.resolved_date is not None]
    distinct = {row.date_text for row in dated}

    if not distinct:
        _refuse_day(
            grid=grid,
            day=day,
            reason=REASON_DAY_WITHOUT_DATE,
            message=(
                "A run of dissection hours states no date in any of its rows, so none of "
                "them could be scheduled."
            ),
            diagnostics=diagnostics,
        )
        return

    if len(distinct) > 1:
        _refuse_day(
            grid=grid,
            day=day,
            reason=REASON_DAY_WITH_SEVERAL_DATES,
            message=(
                f"A run of dissection hours states {len(distinct)} different dates "
                f"({', '.join(sorted(distinct))}), so it could not be read as one day."
            ),
            diagnostics=diagnostics,
        )
        return

    anchor = dated[0]
    resolution = anchor.resolved_date
    if resolution is None:  # pragma: no cover - guarded by `dated`
        return

    refusal = date_refusal(resolution, anchor.date_text, subject="Dissection day")
    if refusal is not None or resolution.value is None:
        reason, message = refusal if refusal is not None else (REASON_DAY_WITHOUT_DATE, "")
        _refuse_day(
            grid=grid,
            day=day,
            reason=reason,
            message=message,
            diagnostics=diagnostics,
            evidence=grid.evidence(
                anchor.date_row_index if anchor.date_row_index is not None else anchor.row_index,
                DATE_COLUMN,
                extraction_rule=RULE_DATE_CELL,
            ),
        )
        return

    # Counted once per day rather than once per published hour, because it
    # reports how the source writes its dates, not how many groups attend.
    diagnostics.increment(f"{METRIC_DATE_RULE_PREFIX}{resolution.rule}")

    for row in day:
        _publish_row(
            grid=grid,
            row=row,
            local_date=resolution.value,
            date_confidence=resolution.confidence,
            date_rule=resolution.rule,
            context=context,
            title=title,
            diagnostics=diagnostics,
            accumulator=accumulator,
        )


def _refuse_day(
    *,
    grid: WorksheetGrid,
    day: Sequence[_Row],
    reason: str,
    message: str,
    diagnostics: ParseDiagnostics,
    evidence: SourceEvidence | None = None,
) -> None:
    diagnostics.increment(f"{METRIC_DAYS_REFUSED_PREFIX}{reason}")
    diagnostics.record_ignored_row(
        reason,
        evidence or _day_evidence(grid, day),
        severity=ParserWarningSeverity.WARNING,
        message=f"{message} {len(day)} dissection hour(s) were not published.",
    )
    # The refusal was reported once with its address; the remaining hours of the
    # same day are counted so every scanned row stays accounted for.
    for row in day[1:]:
        diagnostics.record_ignored_row(
            REASON_ROW_IN_REFUSED_DAY,
            grid.evidence(row.row_index, TIME_COLUMN, extraction_rule=RULE_TIME_CELL),
        )


def _publish_row(
    *,
    grid: WorksheetGrid,
    row: _Row,
    local_date: date,
    date_confidence: float,
    date_rule: str,
    context: ParseSourceContext,
    title: str,
    diagnostics: ParseDiagnostics,
    accumulator: _Accumulator,
) -> None:
    evidence = grid.evidence(row.row_index, GROUP_COLUMN, extraction_rule=RULE_GROUP_CELL)
    selectors = _audience_selectors(row.expression)
    if not row.expression.resolved or selectors is None:
        diagnostics.record_ignored_row(
            REASON_UNRESOLVED_GROUP
            if not row.expression.resolved
            else REASON_UNSUPPORTED_GROUP_VALUE,
            evidence,
            severity=ParserWarningSeverity.WARNING,
            message=(
                f"Cell '{row.group_text}' does not state an anatomy group this profile "
                f"models ({row.expression.reason or REASON_UNSUPPORTED_GROUP_VALUE}), so "
                "no lesson was published for it."
            ),
        )
        return

    attributed = row.resolved_date is None
    if attributed:
        diagnostics.increment(METRIC_ROWS_DATE_FROM_DAY_BLOCK)

    confidence = min(
        date_confidence,
        row.time_confidence,
        row.expression.confidence,
        CONFIDENCE_DATE_FROM_DAY_BLOCK if attributed else 1.0,
    )
    candidate = _build_candidate(
        grid=grid,
        row=row,
        local_date=local_date,
        confidence=confidence,
        selectors=selectors,
        context=context,
        title=title,
        cell_evidence=evidence,
    )
    _accept(
        candidate=candidate,
        evidence=evidence,
        row=row,
        attributed=attributed,
        date_rule=date_rule,
        diagnostics=diagnostics,
        accumulator=accumulator,
    )


def _audience_selectors(expression: GroupExpression) -> list[AudienceSelector] | None:
    """Turn a resolved group expression into anatomy-group selectors.

    The anatomy group is independent of a student's practice group, so it is a
    dimension of its own and a value outside the three this source states is
    refused rather than mapped onto the nearest thing.
    """
    if expression.covers_all or not expression.values:
        return None

    selectors: list[AudienceSelector] = []
    for value in expression.values:
        if _GROUP_VALUE_PATTERN.match(value) is None:
            return None
        selectors.append(AudienceSelector(dimension=DIMENSION_ANATOMY_GROUP, value=value))
    return selectors


def _build_candidate(
    *,
    grid: WorksheetGrid,
    row: _Row,
    local_date: date,
    confidence: float,
    selectors: Sequence[AudienceSelector],
    context: ParseSourceContext,
    title: str,
    cell_evidence: SourceEvidence,
) -> CanonicalScheduleCandidate:
    audience_key = "+".join(f"{selector.dimension}:{selector.value}" for selector in selectors)
    identity_components = build_identity_components(
        (
            ("academicYear", context.academic_year),
            ("classYear", str(context.class_year)),
            ("programLanguage", context.program_language.value),
            ("localDate", local_date.isoformat()),
            ("startLocalTime", row.start.isoformat()),
            ("courseIdentity", course_identity(title) or ""),
            ("audience", audience_key),
        )
    )

    return CanonicalScheduleCandidate(
        candidate_id=f"{grid.worksheet.sheet_id}!R{row.row_index + 1}C{GROUP_COLUMN + 1}",
        academic_year=context.academic_year,
        class_year=context.class_year,
        program_language=context.program_language,
        audience=ScheduleAudienceCandidate(
            scope=AudienceScope.SELECTED_GROUPS,
            selectors=list(selectors),
        ),
        event_type=ScheduleEventType.ANATOMY_PRACTICE,
        status=CandidateRecordStatus.SCHEDULED,
        normalized_course_identity=course_identity(title),
        display_title=title,
        local_date=local_date,
        start_local_time=row.start,
        end_local_time=row.end,
        time_zone_id=context.time_zone_id,
        instructor=None,
        # The document names no room. Its own title says "salon", but no row
        # states which one, and reading the title as a location would be an
        # inference the cell does not support.
        location=None,
        curriculum_block=None,
        departments=[],
        stable_identity=stable_identity(identity_components),
        content_hash=content_hash(
            {
                "academicYear": context.academic_year,
                "classYear": str(context.class_year),
                "programLanguage": context.program_language.value,
                "displayTitle": title,
                "eventType": ScheduleEventType.ANATOMY_PRACTICE.value,
                "localDate": local_date.isoformat(),
                # A dissection hour is always timed: a row without a readable
                # time range is refused rather than published as all-day
                # (ADR-046).
                "isAllDay": encode_all_day(False),
                "startLocalTime": row.start.isoformat(),
                "endLocalTime": row.end.isoformat(),
                "timeZoneId": context.time_zone_id,
                "instructor": None,
                "location": None,
                "curriculumBlock": None,
                "departments": None,
                "audience": audience_key,
            }
        ),
        confidence=confidence,
        identity_components=identity_components,
        evidence=[
            grid.evidence(
                row.date_row_index if row.date_row_index is not None else row.row_index,
                DATE_COLUMN,
                extraction_rule=RULE_DATE_CELL,
            ),
            grid.evidence(row.row_index, TIME_COLUMN, extraction_rule=RULE_TIME_CELL),
            cell_evidence,
        ],
    )


def _accept(
    *,
    candidate: CanonicalScheduleCandidate,
    evidence: SourceEvidence,
    row: _Row,
    attributed: bool,
    date_rule: str,
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
        diagnostics.record_ignored_row(
            REASON_DUPLICATE_IDENTITY,
            evidence,
            severity=severity,
            message=(
                f"Row repeats the session already published as candidate "
                f"'{existing.candidate_id}', so it was not published again."
            ),
        )
        if severity is ParserWarningSeverity.WARNING:
            diagnostics.warn(
                severity=ParserWarningSeverity.WARNING,
                code=WARNING_CONFLICTING_DUPLICATE,
                message=(
                    "Two rows share one session identity but disagree on content. "
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

    if attributed:
        # The audience decides whose calendar changes and the date decides when,
        # so a date this profile attributed rather than read from the row is
        # always reported.
        diagnostics.confidence(
            field_name="localDate",
            score=candidate.confidence,
            reason=CONFIDENCE_REASON_DAY_BLOCK,
            candidate_id=candidate.candidate_id,
        )
    elif candidate.confidence < 1.0:
        diagnostics.confidence(
            field_name="localDate",
            score=candidate.confidence,
            reason=date_rule,
            candidate_id=candidate.candidate_id,
        )

    duration = duration_minutes(row.start, row.end)
    if not MIN_PLAUSIBLE_DURATION_MINUTES <= duration <= MAX_PLAUSIBLE_DURATION_MINUTES:
        diagnostics.warning(
            WARNING_IMPLAUSIBLE_DURATION,
            f"Dissection hour lasts {duration} minutes, outside the plausible range of "
            f"{MIN_PLAUSIBLE_DURATION_MINUTES} to {MAX_PLAUSIBLE_DURATION_MINUTES} minutes.",
            candidate_id=candidate.candidate_id,
            evidence=evidence,
        )


def _row_evidence(grid: WorksheetGrid, row_index: int) -> SourceEvidence:
    return grid.range_evidence(
        row_index,
        DATE_COLUMN,
        row_index + 1,
        max(grid.worksheet.column_count, GROUP_COLUMN + 1),
        extraction_rule=RULE_ROW,
    )


def _day_evidence(grid: WorksheetGrid, day: Sequence[_Row]) -> SourceEvidence:
    return grid.range_evidence(
        day[0].row_index,
        DATE_COLUMN,
        day[-1].row_index + 1,
        max(grid.worksheet.column_count, GROUP_COLUMN + 1),
        extraction_rule=RULE_ROW,
    )
