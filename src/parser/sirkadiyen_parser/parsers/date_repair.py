"""How a parser profile reads a run of dates chronologically (ADR-139).

:mod:`sirkadiyen_parser.normalization.date_sequence` decides what an
out-of-sequence date may have meant. This module is what a profile calls: it
applies the operator's accepted corrections first, runs the analysis over what
is left, and reports both the repairs it made and the suggestions it withheld in
the one shape every profile must report them in.

The order matters. An operator's decision outranks the parser's reading of the
order, because the operator has looked at the document and the parser has not, so
a corrected date is never a suspect and never becomes a suggestion again.

A profile adopts this in three steps:

1. resolve its date cells as it always did, in source order, and collect them as
   :class:`~sirkadiyen_parser.normalization.date_sequence.DateSequenceEntry`
2. call :func:`read_date_run` once per run, then :func:`report_date_run`
3. take each position's date from :meth:`DateSequence.resolution` rather than
   from its own resolution

A profile that does this publishes exactly what it published before for a
document with no mistyped date, because a run with no suspect produces no
repairs, no suggestions and no warnings.
"""

from collections.abc import Callable, Sequence
from datetime import date
from typing import Any

from sirkadiyen_parser.contracts.parsing import ParseSourceContext, SourceEvidence
from sirkadiyen_parser.diagnostics import ParseDiagnostics
from sirkadiyen_parser.normalization.date_sequence import (
    OUTCOME_WEEKDAY_CONTRADICTS,
    AcademicYearWindow,
    DateSequence,
    DateSequenceEntry,
    DateSequenceOutcome,
    analyze_date_sequence,
)
from sirkadiyen_parser.normalization.dates import DateResolution

#: A date an operator has corrected is as good as a date the document wrote
#: plainly, but not better: it is a human decision about a document rather than a
#: reading of one, and the difference should stay visible in the confidence.
RULE_OPERATOR_CORRECTION = "operatorCorrectedDate"
CONFIDENCE_OPERATOR_CORRECTION = 0.95

WARNING_DATE_REPAIRED = "outOfSequenceDateRepaired"
WARNING_DATE_SUGGESTED = "outOfSequenceDateNotRepaired"
WARNING_DATE_CORRECTED = "operatorCorrectedDateApplied"

METRIC_DATES_REPAIRED = "dates.outOfSequence.repaired"
METRIC_DATES_SUGGESTED = "dates.outOfSequence.suggested"
METRIC_DATES_SUGGESTED_PREFIX = "dates.outOfSequence.suggested."
METRIC_DATES_CORRECTED = "dates.correctedByOperator"

#: The extraction rule a date-sequence warning records, so its evidence is
#: recognizable as this analysis rather than as the cell reading it revised.
RULE_DATE_SEQUENCE = "dateSequence"


def academic_year_window(context: ParseSourceContext) -> AcademicYearWindow | None:
    """Read the window this source's dates must fall in, or ``None``."""
    return AcademicYearWindow.from_label(context.academic_year)


def read_date_run(
    entries: Sequence[DateSequenceEntry],
    *,
    context: ParseSourceContext,
) -> DateSequence:
    """Apply the operator's corrections to a run, then read what is left.

    ``entries`` are the run's dated positions in the order the source writes
    them. Passing a position whose cell resolved to nothing is expected and
    correct: an unreadable cell is not a suspect, cannot be an anchor, and its
    place in the run is still part of the order.
    """
    corrections = {correction.original: correction for correction in context.date_corrections}
    corrected: dict[Any, DateResolution] = {}
    adjusted: list[DateSequenceEntry] = []

    for entry in entries:
        correction = corrections.get(entry.resolution.value) if entry.resolution.value else None
        if correction is None:
            adjusted.append(entry)
            continue

        resolution = DateResolution(
            value=correction.corrected,
            rule=RULE_OPERATOR_CORRECTION,
            confidence=min(entry.resolution.confidence, CONFIDENCE_OPERATOR_CORRECTION),
            reason=f"correctedFrom:{correction.original.isoformat()}",
            weekday_text=entry.resolution.weekday_text,
            # The document's weekday belongs to the value the operator replaced,
            # so it can no longer corroborate or contradict anything.
            weekday_matches=None,
        )
        corrected[entry.key] = resolution
        adjusted.append(DateSequenceEntry(key=entry.key, resolution=resolution))

    sequence = analyze_date_sequence(adjusted, window=academic_year_window(context))
    return sequence.with_resolutions(corrected) if corrected else sequence


def report_date_corrections(
    *,
    diagnostics: ParseDiagnostics,
    context: ParseSourceContext,
) -> None:
    """Record the corrections an operator has accepted for this source.

    Called once per parse rather than once per run: a correction is a property of
    the source, and a document with several runs would otherwise report the same
    decision several times.

    A correction is recorded whether or not the document still states the date it
    corrects. A correction that no longer matches anything is exactly what a
    corrected document looks like, and an operator needs to see that it can be
    retired rather than wonder why the parse stopped mentioning it.
    """
    for correction in context.date_corrections:
        diagnostics.increment(METRIC_DATES_CORRECTED)
        diagnostics.information(
            WARNING_DATE_CORRECTED,
            f"An operator has decided this source writes {correction.original.isoformat()} "
            f"where it means {correction.corrected.isoformat()}, so every cell stating the "
            f"first was read as the second (accepted by {correction.decided_by} on "
            f"{correction.decided_at}).",
            detail={
                "original": correction.original.isoformat(),
                "corrected": correction.corrected.isoformat(),
                "decidedBy": correction.decided_by,
                "decidedAt": correction.decided_at,
            },
        )


def report_date_run(
    sequence: DateSequence,
    *,
    diagnostics: ParseDiagnostics,
    evidence_for: Callable[[Any], SourceEvidence],
) -> None:
    """Record every repair and every withheld suggestion one run produced.

    A repair is a warning rather than a note. It changes which day a lesson
    lands on, which is the most consequential thing a parse can do to a student's
    calendar, so it must reach revision validation and be visible on the review
    screen even when the parse is otherwise clean.
    """
    for outcome in sequence.repairs:
        diagnostics.increment(METRIC_DATES_REPAIRED)
        diagnostics.warning(
            WARNING_DATE_REPAIRED,
            _repair_message(outcome),
            evidence=evidence_for(outcome.key),
            detail=_detail(outcome),
        )

    for outcome in sequence.suggestions:
        diagnostics.increment(METRIC_DATES_SUGGESTED)
        diagnostics.increment(f"{METRIC_DATES_SUGGESTED_PREFIX}{outcome.reason}")
        diagnostics.warning(
            WARNING_DATE_SUGGESTED,
            _suggestion_message(outcome),
            evidence=evidence_for(outcome.key),
            detail=_detail(outcome),
        )


def _repair_message(outcome: DateSequenceOutcome) -> str:
    applied = outcome.applied
    return (
        f"The source dates this row {outcome.original.isoformat()}, which falls outside the "
        f"run it sits in ({_bracket(outcome)}). Substituting the year is the only reading that "
        f"fits, so the row was read as {applied.isoformat() if applied else ''} rather than as "
        "written; whatever it publishes carries that date. Reject the revision if the source "
        "means what it says."
    )


def _suggestion_message(outcome: DateSequenceOutcome) -> str:
    written = (
        f"The source dates this row {outcome.original.isoformat()}, which falls outside the "
        f"run it sits in ({_bracket(outcome)}), and it was read as written."
    )
    if outcome.reason == OUTCOME_WEEKDAY_CONTRADICTS:
        return (
            f"{written} The cell contradicts itself: the year that would fit its neighbours "
            "falls on a different weekday than the one the cell names, so correcting either "
            "half would be a guess. The readings are listed for an operator to choose from."
        )
    if not outcome.candidates:
        return (
            f"{written} No year puts this day and month between its neighbours, so the row "
            "states something this analysis cannot explain as a mistyped year."
        )
    return (
        f"{written} The correction was withheld ({outcome.reason}); the readings that fit are "
        "listed for an operator to choose from."
    )


def _bracket(outcome: DateSequenceOutcome) -> str:
    return f"{_side(outcome.lower_anchor)} to {_side(outcome.upper_anchor)}"


def _side(anchor: date | None) -> str:
    return anchor.isoformat() if anchor is not None else "the start of the academic year"


def _detail(outcome: DateSequenceOutcome) -> dict[str, Any]:
    """Render an outcome as the evidence an operator acts on.

    The keys are the review screen's columns (ADR-135), and ``candidates`` is
    what the "apply this date" action reads: accepting one writes a source date
    correction from ``original`` to that candidate, so the shape here is the
    shape of the decision.
    """
    return {
        "original": outcome.original.isoformat(),
        "lowerAnchor": outcome.lower_anchor.isoformat() if outcome.lower_anchor else None,
        "upperAnchor": outcome.upper_anchor.isoformat() if outcome.upper_anchor else None,
        "reason": outcome.reason,
        "applied": outcome.applied.isoformat() if outcome.applied else None,
        "candidates": [
            {
                "value": candidate.value.isoformat(),
                "rule": candidate.rule,
                "weekdayMatches": candidate.weekday_matches,
            }
            for candidate in outcome.candidates
        ],
    }


__all__ = [
    "CONFIDENCE_OPERATOR_CORRECTION",
    "METRIC_DATES_CORRECTED",
    "METRIC_DATES_REPAIRED",
    "METRIC_DATES_SUGGESTED",
    "METRIC_DATES_SUGGESTED_PREFIX",
    "RULE_DATE_SEQUENCE",
    "RULE_OPERATOR_CORRECTION",
    "WARNING_DATE_CORRECTED",
    "WARNING_DATE_REPAIRED",
    "WARNING_DATE_SUGGESTED",
    "academic_year_window",
    "read_date_run",
    "report_date_corrections",
    "report_date_run",
]
