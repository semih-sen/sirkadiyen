"""Out-of-sequence date repair.

Every schedule document here writes its date column in chronological order. That
order is the only corroboration a hand-typed date has, and it is strong: a row
whose date contradicts both of its neighbours is not a schedule that jumped six
years and came back, it is a typing mistake, and the neighbours bound what the
mistake can have been.

Six committed fixtures carry one. ``G1-TR-ANNUAL`` dates a lunch break
2027-11-30 among 2026-11-30 rows; ``G1-TR-PRACTICE`` dates a session 2020-11-20
between 2026-11-19 and 2026-11-20 — the same day and month, six years out — and
another 2026-05-21 in a block that runs through May 2027; ``G2-TR-PRACTICE``,
``G2-VERTICAL-AUTUMN`` and ``G2-VERTICAL-SPRING`` each write a slot a year out;
and ``G2-ANATOMY-SPRING`` writes ``9 Nisan 2025`` where its own weekday says it
means 2026. Every one of them is the same mistake: the year was carried over
from the document this one was copied from.

This module repairs that mistake and nothing else, under rules narrow enough
that a repair is a reading of the source rather than an invention:

- **Only the year is substituted.** The day and the month are what the typist
  meant; the year is what they forgot to update. A repair that moved a day would
  be a guess about which day, and there is no evidence for that.
- **The repaired date must fall between its neighbours.** The nearest sound
  dates before and after it are anchors, and a repair that does not land between
  them is not a repair. The bracket must also be narrow: over a whole semester,
  exactly one year landing inside it says almost nothing.
- **Exactly one substitution may survive.** Two candidate years that both fit
  the anchors are two readings of the cell, and publishing either would be a
  coin toss.
- **A stated weekday decides either way.** It vetoes: ``21 Mayıs 2026 Perşembe``
  substitutes to 2027-05-21, which is a Friday, so the cell disagrees with its
  own repair and nothing is applied. And it corroborates: a substituted year the
  cell's own weekday agrees with landed on one day in seven, which is the
  evidence the anchors are otherwise being asked for, so such a repair does not
  also have to sit between two of them. Every other one does — a suspect at the
  start or end of its run is bounded on one side by the academic year, which is
  far too wide a bracket to call a reading.

A suspect that fails any of these is still reported, with every candidate the
rules produced and the anchors that bound it, so an operator can decide what the
parser would not (ADR-139). The parser publishes what the source says; the
suggestion is what it would say if asked.

Repairs are applied with reduced confidence and always carry a warning, so a
repaired lesson is visible as a repaired lesson everywhere downstream and can
never be mistaken for a date the source stated plainly.
"""

from bisect import bisect_right
from collections.abc import Hashable, Iterable, Sequence
from dataclasses import dataclass, field
from datetime import date, timedelta
from typing import Self

from sirkadiyen_parser.normalization.dates import DateResolution, weekday_index_of

#: The academic year runs from 1 August of the first year to 31 July of the
#: second, which covers a Turkish medical faculty year including resit periods.
#: It mirrors ``ScheduleRevisionValidator.TryReadAcademicYear`` on the .NET side
#: deliberately: the validator holds the revision that this repair is meant to
#: prevent, so a repaired date that the validator would still refuse would be
#: worse than no repair at all.
ACADEMIC_YEAR_START_MONTH = 8
ACADEMIC_YEAR_START_DAY = 1
ACADEMIC_YEAR_END_MONTH = 7
ACADEMIC_YEAR_END_DAY = 31

#: How far outside its academic year a date may fall before it is suspected of
#: being mistyped. It matches the .NET validator's default grace so that the two
#: sides agree on which dates are anomalous.
DEFAULT_GRACE_DAYS = 30

#: The widest anchor bracket a repair may be applied inside. Uniqueness within a
#: bracket is only evidence when the bracket is narrow: over a whole semester,
#: exactly one year landing inside it says almost nothing.
MAX_ANCHOR_SPAN_DAYS = 60

#: A run shorter than this states too little order to be read as ordered at all.
MIN_RUN_LENGTH = 4

#: The share of a run's dates that must already be in order before the run is
#: treated as chronological. A source that lists its dates in some other order —
#: none does today, and the analysis runs on every profile — produces a short
#: ordered subsequence, and every date outside it would otherwise be reported as
#: a suspect.
MIN_ORDERED_SHARE = 0.8

RULE_SEQUENCE_YEAR_SUBSTITUTION = "sequenceYearSubstitution"
RULE_SEQUENCE_WEEKDAY_ALTERNATIVE = "sequenceWeekdayAlternative"

#: The rule a repaired :class:`DateResolution` reports. It replaces the rule that
#: read the cell, because what produced the published value is the repair.
RULE_SEQUENCE_REPAIRED = "sequenceRepairedDate"

#: A repair is weaker evidence than any cell read plainly, and weaker still when
#: the cell states no weekday to corroborate it. Both stay above the revision
#: validator's low-confidence threshold, because a repair that quarantined its
#: own revision would leave the schedule exactly where it started.
CONFIDENCE_SEQUENCE_REPAIR = 0.7
CONFIDENCE_SEQUENCE_REPAIR_CORROBORATED = 0.8

#: Why a suspect was repaired.
OUTCOME_REPAIRED = "repaired"

#: Why a suspect was not repaired. Each is reported as it stands, so an operator
#: reading a suggestion knows which rule withheld the correction.
OUTCOME_NO_CANDIDATE = "noCandidateFitsTheAnchors"
OUTCOME_AMBIGUOUS = "severalCandidatesFitTheAnchors"
OUTCOME_UNBOUNDED = "suspectIsNotBoundedOnBothSides"
OUTCOME_ANCHORS_TOO_WIDE = "anchorBracketTooWideToRead"
OUTCOME_WEEKDAY_CONTRADICTS = "candidateContradictsTheStatedWeekday"


@dataclass(frozen=True, slots=True)
class AcademicYearWindow:
    """The dates a lesson of one academic year may plausibly fall on."""

    start: date
    end: date
    grace_days: int = DEFAULT_GRACE_DAYS

    @classmethod
    def from_label(cls, label: str, *, grace_days: int = DEFAULT_GRACE_DAYS) -> Self | None:
        """Read an academic year label such as ``2026-2027``.

        Returns ``None`` for a label this rule cannot read, which is a source
        configuration fault rather than a schedule fault. The caller then skips
        the analysis rather than inventing a window.
        """
        parts = label.split("-")
        if len(parts) != 2:
            return None
        try:
            first = int(parts[0])
            second = int(parts[1])
        except ValueError:
            return None
        if second != first + 1:
            return None
        return cls(
            start=date(first, ACADEMIC_YEAR_START_MONTH, ACADEMIC_YEAR_START_DAY),
            end=date(second, ACADEMIC_YEAR_END_MONTH, ACADEMIC_YEAR_END_DAY),
            grace_days=grace_days,
        )

    @property
    def years(self) -> tuple[int, ...]:
        """The calendar years the window spans, in order."""
        return tuple(range(self.start.year, self.end.year + 1))

    def contains(self, value: date) -> bool:
        """Whether a date falls inside the academic year proper.

        A repair must land here rather than merely inside the graced window: the
        grace exists so that a slightly early orientation day is not reported as
        an anomaly, not so that a correction may aim at one.
        """
        return self.start <= value <= self.end

    def is_plausible(self, value: date) -> bool:
        """Whether a date is close enough to the year not to be suspected."""
        return (
            self.start - timedelta(days=self.grace_days)
            <= value
            <= self.end + timedelta(days=self.grace_days)
        )


@dataclass(frozen=True, slots=True)
class DateRepairCandidate:
    """One date a suspect cell may have meant, and how that was derived."""

    value: date
    rule: str

    #: Whether the cell's own weekday text agrees with this candidate. ``None``
    #: when the cell states no weekday, which is neither agreement nor
    #: contradiction.
    weekday_matches: bool | None = None


@dataclass(frozen=True, slots=True)
class DateSequenceEntry:
    """One dated position in a run, as the calling profile read it."""

    #: Whatever the profile uses to look the outcome up again — a row index, a
    #: cell address, a slot object. It is opaque here and only has to be hashable
    #: and unique within the run.
    key: Hashable
    resolution: DateResolution


@dataclass(frozen=True, slots=True)
class DateSequenceOutcome:
    """What the analysis concluded about one out-of-sequence date."""

    key: Hashable
    original: date

    #: The nearest sound dates before and after the suspect. ``None`` on a side
    #: means the suspect is the first or the last dated position in its run.
    lower_anchor: date | None
    upper_anchor: date | None

    #: Every date the rules produced, best first. Empty when the suspect's day
    #: and month cannot be placed between its anchors under any year.
    candidates: tuple[DateRepairCandidate, ...]

    #: The date that was published, or ``None`` when the source's own value was
    #: kept and the candidates are only a suggestion.
    applied: date | None

    #: Which rule decided the outcome. One of the ``OUTCOME_*`` codes.
    reason: str

    @property
    def repaired(self) -> bool:
        """Whether the published date differs from the one the source states."""
        return self.applied is not None


@dataclass(frozen=True, slots=True)
class DateSequence:
    """The chronological reading of one run of date cells.

    Build it once per run — a rotation block, a schedule table, a worksheet's
    dated rows — then ask it for each position's date. A position the analysis
    did not touch answers with exactly what the profile read, so a profile that
    adopts this never changes what it publishes for a sound document.
    """

    _resolutions: dict[Hashable, DateResolution] = field(default_factory=dict)
    _outcomes: tuple[DateSequenceOutcome, ...] = ()

    @property
    def outcomes(self) -> tuple[DateSequenceOutcome, ...]:
        """Every suspect the analysis found, in run order."""
        return self._outcomes

    @property
    def repairs(self) -> tuple[DateSequenceOutcome, ...]:
        """The suspects whose date was corrected."""
        return tuple(outcome for outcome in self._outcomes if outcome.repaired)

    @property
    def suggestions(self) -> tuple[DateSequenceOutcome, ...]:
        """The suspects the rules refused to correct on their own."""
        return tuple(outcome for outcome in self._outcomes if not outcome.repaired)

    def with_resolutions(self, overrides: dict[Hashable, DateResolution]) -> "DateSequence":
        """Return this analysis with further positions answered from ``overrides``.

        Used to layer a decision the analysis did not make — today, the dates an
        operator has corrected — over the ones it did, without either of them
        having to know about the other. A repair the analysis applied wins,
        because a corrected position is passed to :func:`analyze_date_sequence`
        as ``decided`` and so is never a suspect and never the key of a repair.
        """
        return DateSequence(
            _resolutions={**overrides, **self._resolutions},
            _outcomes=self._outcomes,
        )

    def resolution(self, key: Hashable, fallback: DateResolution) -> DateResolution:
        """Return the repaired resolution for a position, or the one read there.

        ``fallback`` is what the profile resolved for that position. Passing it
        rather than storing every resolution keeps the profile's own reading
        authoritative for everything this analysis did not touch.
        """
        return self._resolutions.get(key, fallback)


def analyze_date_sequence(
    entries: Sequence[DateSequenceEntry],
    *,
    window: AcademicYearWindow | None,
    decided: frozenset[Hashable] = frozenset(),
) -> DateSequence:
    """Read one run of dates chronologically and repair what it can.

    ``entries`` are the dated positions in the order the source writes them,
    including the ones that resolved to nothing: an unreadable cell is not a
    suspect and must not become an anchor either, and passing it keeps the run's
    order intact.

    ``decided`` names the positions an operator has already ruled on (ADR-139),
    by the same key the entries carry. Their reading outranks the order the
    parser would read them under, so they are never repaired and never reported
    as a suspect — including when the operator confirmed the very date the
    document states, which stays out of sequence and would otherwise be flagged
    on every later parse. Such a position still counts toward the run's order and
    may still anchor its neighbours; it simply is not itself questioned.

    Returns an empty analysis — one that repairs nothing and reports nothing —
    when there is no window, when the run is too short to state an order, or when
    the run is not chronological. Each of those is a case where order is not
    evidence, and a repair without evidence is an invention.
    """
    if window is None:
        return DateSequence()

    dated = [entry for entry in entries if entry.resolution.value is not None]
    if len(dated) < MIN_RUN_LENGTH:
        return DateSequence()

    plausible = [index for index, entry in enumerate(dated) if window.is_plausible(_value(entry))]
    ordered = _longest_non_decreasing(tuple(_value(dated[index]) for index in plausible))
    anchor_positions = sorted(plausible[position] for position in ordered)

    if len(anchor_positions) < MIN_ORDERED_SHARE * len(dated):
        return DateSequence()

    anchors = frozenset(anchor_positions)
    anchor_values = [_value(dated[position]) for position in anchor_positions]

    resolutions: dict[Hashable, DateResolution] = {}
    outcomes: list[DateSequenceOutcome] = []

    for index, entry in enumerate(dated):
        if index in anchors:
            continue
        if entry.key in decided:
            # The operator has already answered this position. Questioning it
            # again would re-raise a suggestion they resolved, and for a date they
            # confirmed as written that suggestion would never stop coming back.
            continue

        outcome = _examine(
            entry=entry,
            index=index,
            anchor_positions=anchor_positions,
            anchor_values=anchor_values,
            window=window,
        )
        outcomes.append(outcome)
        if outcome.applied is not None:
            resolutions[entry.key] = _repaired(entry.resolution, outcome)

    return DateSequence(_resolutions=resolutions, _outcomes=tuple(outcomes))


def _value(entry: DateSequenceEntry) -> date:
    value = entry.resolution.value
    if value is None:  # pragma: no cover - guarded by the caller's filter
        raise ValueError("A dated entry must carry a resolved date.")
    return value


def _examine(
    *,
    entry: DateSequenceEntry,
    index: int,
    anchor_positions: Sequence[int],
    anchor_values: Sequence[date],
    window: AcademicYearWindow,
) -> DateSequenceOutcome:
    original = _value(entry)
    boundary = bisect_right(anchor_positions, index)
    lower = anchor_values[boundary - 1] if boundary > 0 else None
    upper = anchor_values[boundary] if boundary < len(anchor_values) else None

    stated_weekday = weekday_index_of(entry.resolution.weekday_text)
    candidates = _candidates(
        original=original,
        lower=lower,
        upper=upper,
        window=window,
        stated_weekday=stated_weekday,
    )

    def outcome(applied: date | None, reason: str) -> DateSequenceOutcome:
        return DateSequenceOutcome(
            key=entry.key,
            original=original,
            lower_anchor=lower,
            upper_anchor=upper,
            candidates=candidates,
            applied=applied,
            reason=reason,
        )

    substitutions = [
        candidate for candidate in candidates if candidate.rule == RULE_SEQUENCE_YEAR_SUBSTITUTION
    ]
    # Reported in order of how much the objection tells a reviewer. A cell that
    # contradicts its own weekday is a specific, checkable fact about that cell,
    # so it is named even when the anchors would also have withheld the repair.
    if not substitutions:
        return outcome(None, OUTCOME_NO_CANDIDATE)
    if len(substitutions) > 1:
        return outcome(None, OUTCOME_AMBIGUOUS)

    only = substitutions[0]
    if only.weekday_matches is False:
        return outcome(None, OUTCOME_WEEKDAY_CONTRADICTS)

    # A weekday the substituted year agrees with is evidence the anchors did not
    # supply: the cell names one day in seven and the substitution landed on it.
    # It is what the anchors are otherwise being asked for, so a corroborated
    # substitution is not also required to sit between two of them. Every other
    # one is, and inside a bracket narrow enough for uniqueness to mean anything.
    if only.weekday_matches is True:
        return outcome(only.value, OUTCOME_REPAIRED)

    if lower is None or upper is None:
        return outcome(None, OUTCOME_UNBOUNDED)
    if (upper - lower).days > MAX_ANCHOR_SPAN_DAYS:
        return outcome(None, OUTCOME_ANCHORS_TOO_WIDE)

    return outcome(only.value, OUTCOME_REPAIRED)


def _candidates(
    *,
    original: date,
    lower: date | None,
    upper: date | None,
    window: AcademicYearWindow,
    stated_weekday: int | None,
) -> tuple[DateRepairCandidate, ...]:
    """Build every date the suspect may have meant, best first.

    The bracket is inclusive on both sides. These documents repeat a date across
    consecutive rows — a morning slot and an afternoon slot of the same day — so
    a repair that equals its own anchor is the ordinary case rather than a
    boundary error.
    """
    low = lower if lower is not None else window.start
    high = upper if upper is not None else window.end

    found: list[DateRepairCandidate] = []
    for year in window.years:
        if year == original.year:
            continue
        try:
            value = date(year, original.month, original.day)
        except ValueError:
            # 29 February in a year that has no 29 February. The substitution
            # simply does not exist; it is not an error.
            continue
        if not window.contains(value) or not low <= value <= high:
            continue
        found.append(
            DateRepairCandidate(
                value=value,
                rule=RULE_SEQUENCE_YEAR_SUBSTITUTION,
                weekday_matches=_agrees(value, stated_weekday),
            )
        )

    found.extend(
        _weekday_alternatives(
            substitutions=found,
            low=low,
            high=high,
            window=window,
            stated_weekday=stated_weekday,
        )
    )
    return tuple(found)


def _weekday_alternatives(
    *,
    substitutions: Sequence[DateRepairCandidate],
    low: date,
    high: date,
    window: AcademicYearWindow,
    stated_weekday: int | None,
) -> Iterable[DateRepairCandidate]:
    """Offer the dates the cell's own weekday points at, when its year does not.

    This only ever runs when the single year substitution contradicts the stated
    weekday, which is a cell disagreeing with itself: ``21 Mayıs 2026 Perşembe``
    is a Thursday in 2026 and a Friday in 2027, so one half of it was copied from
    a previous year and there is no way to tell which. Both readings are offered
    to whoever reviews it, and neither is ever applied — that is the whole point
    of the contradiction.

    The alternatives are the two occurrences of the stated weekday on either side
    of the substituted date, and no more. Every such weekday in the bracket would
    be a list rather than a suggestion, and the reason this reading is worth
    offering at all is that the typist kept the weekday and moved the number by a
    day or two.
    """
    if stated_weekday is None or len(substitutions) != 1:
        return ()
    if substitutions[0].weekday_matches is not False:
        return ()

    substituted = substitutions[0].value
    before = substituted - timedelta(days=(substituted.weekday() - stated_weekday) % 7)
    after = substituted + timedelta(days=(stated_weekday - substituted.weekday()) % 7)

    return tuple(
        DateRepairCandidate(
            value=value,
            rule=RULE_SEQUENCE_WEEKDAY_ALTERNATIVE,
            weekday_matches=True,
        )
        for value in sorted({before, after})
        if window.contains(value) and low <= value <= high
    )


def _agrees(value: date, stated_weekday: int | None) -> bool | None:
    if stated_weekday is None:
        return None
    return value.weekday() == stated_weekday


def _repaired(original: DateResolution, outcome: DateSequenceOutcome) -> DateResolution:
    """Rebuild a resolution around the repaired date.

    The rule becomes the repair rather than the rule that read the cell, because
    the repair is what produced the published value, and the reason names the
    rule the cell was read under so the original reading is not lost.
    """
    applied = outcome.applied
    if applied is None:  # pragma: no cover - guarded by the caller
        raise ValueError("Only an applied outcome produces a resolution.")

    corroborated = any(
        candidate.value == applied and candidate.weekday_matches is True
        for candidate in outcome.candidates
    )
    return DateResolution(
        value=applied,
        rule=RULE_SEQUENCE_REPAIRED,
        confidence=min(
            original.confidence,
            CONFIDENCE_SEQUENCE_REPAIR_CORROBORATED if corroborated else CONFIDENCE_SEQUENCE_REPAIR,
        ),
        reason=f"repairedFrom:{original.rule}",
        weekday_text=original.weekday_text,
        weekday_matches=True if corroborated else original.weekday_matches,
    )


def _longest_non_decreasing(values: Sequence[date]) -> tuple[int, ...]:
    """Return the positions of a longest non-decreasing subsequence.

    Patience sorting, reconstructed through predecessor links. Ties are resolved
    by the earliest position, so one run always yields one answer: the analysis
    is committed to a golden file and a reconstruction that varied would move it
    without anything changing.
    """
    if not values:
        return ()

    tails: list[int] = []
    tail_values: list[date] = []
    previous: list[int] = [-1] * len(values)

    for index, value in enumerate(values):
        position = bisect_right(tail_values, value)
        previous[index] = tails[position - 1] if position > 0 else -1
        if position == len(tails):
            tails.append(index)
            tail_values.append(value)
        else:
            tails[position] = index
            tail_values[position] = value

    chain: list[int] = []
    cursor = tails[-1]
    while cursor != -1:
        chain.append(cursor)
        cursor = previous[cursor]
    return tuple(reversed(chain))
