"""The chronological reading of a date column, and what it will and will not fix.

Every case here is written against the rule it exercises rather than against a
document, because the rules are what decide whether a lesson moves on a real
calendar. The cases that come from real documents are named as such.
"""

from datetime import date

import pytest

from sirkadiyen_parser.normalization.date_sequence import (
    CONFIDENCE_SEQUENCE_REPAIR,
    CONFIDENCE_SEQUENCE_REPAIR_CORROBORATED,
    MAX_ANCHOR_SPAN_DAYS,
    MIN_RUN_LENGTH,
    OUTCOME_AMBIGUOUS,
    OUTCOME_ANCHORS_TOO_WIDE,
    OUTCOME_NO_CANDIDATE,
    OUTCOME_REPAIRED,
    OUTCOME_UNBOUNDED,
    OUTCOME_WEEKDAY_CONTRADICTS,
    RULE_SEQUENCE_REPAIRED,
    RULE_SEQUENCE_WEEKDAY_ALTERNATIVE,
    RULE_SEQUENCE_YEAR_SUBSTITUTION,
    AcademicYearWindow,
    DateSequence,
    DateSequenceEntry,
    analyze_date_sequence,
)
from sirkadiyen_parser.normalization.dates import (
    RULE_SERIAL,
    DateResolution,
    resolve_date_text,
    unresolved_date,
)


def window(label: str) -> AcademicYearWindow:
    """The window a label names, refusing a label these tests meant to be readable.

    `AcademicYearWindow.from_label` answers `None` for a label it cannot read,
    which is the right answer for a misconfigured source and never the answer a
    test wants: a test whose window silently became `None` would assert that
    nothing was analysed and pass for the wrong reason.
    """
    built = AcademicYearWindow.from_label(label)
    assert built is not None, f"'{label}' should be a readable academic year."
    return built


WINDOW = window("2026-2027")


def serial(value: str) -> DateResolution:
    """A date the source wrote as a spreadsheet serial: no weekday, full confidence."""
    return DateResolution(value=date.fromisoformat(value), rule=RULE_SERIAL, confidence=1.0)


def run(*resolutions: DateResolution) -> tuple[DateSequenceEntry, ...]:
    return tuple(
        DateSequenceEntry(key=index, resolution=resolution)
        for index, resolution in enumerate(resolutions)
    )


def analyze(
    *resolutions: DateResolution,
    academic_year: AcademicYearWindow | None = WINDOW,
) -> DateSequence:
    return analyze_date_sequence(run(*resolutions), window=academic_year)


def test_a_sound_run_is_left_entirely_alone() -> None:
    """The guarantee every adopting profile depends on."""
    sequence = analyze(
        serial("2026-11-18"),
        serial("2026-11-19"),
        serial("2026-11-19"),
        serial("2026-11-20"),
        serial("2026-11-23"),
    )

    assert sequence.outcomes == ()
    fallback = serial("2026-11-19")
    assert sequence.resolution(1, fallback) is fallback


def test_a_year_typo_between_its_neighbours_is_repaired() -> None:
    """`G1-TR-PRACTICE` dates a session 2020-11-20 between two November 2026 rows."""
    sequence = analyze(
        serial("2026-11-17"),
        serial("2026-11-18"),
        serial("2026-11-19"),
        serial("2020-11-20"),
        serial("2026-11-20"),
        serial("2026-11-23"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.key == 3
    assert outcome.reason == OUTCOME_REPAIRED
    assert outcome.applied == date(2026, 11, 20)
    assert outcome.lower_anchor == date(2026, 11, 19)
    assert outcome.upper_anchor == date(2026, 11, 20)
    assert [candidate.value for candidate in outcome.candidates] == [date(2026, 11, 20)]


def test_a_repair_replaces_the_resolution_and_lowers_its_confidence() -> None:
    sequence = analyze(
        serial("2026-11-17"),
        serial("2026-11-19"),
        serial("2020-11-20"),
        serial("2026-11-20"),
        serial("2026-11-23"),
    )

    repaired = sequence.resolution(2, serial("2020-11-20"))

    assert repaired.value == date(2026, 11, 20)
    assert repaired.rule == RULE_SEQUENCE_REPAIRED
    assert repaired.confidence == CONFIDENCE_SEQUENCE_REPAIR
    assert repaired.reason == f"repairedFrom:{RULE_SERIAL}"


def test_a_weekday_the_repair_agrees_with_raises_its_confidence() -> None:
    """`G2-ANATOMY-SPRING` writes `9 Nisan 2025` and its own weekday says 2026."""
    spring = window("2025-2026")
    sequence = analyze(
        serial("2026-03-31"),
        serial("2026-04-07"),
        resolve_date_text("9 Nisan 2025 Perşembe"),
        serial("2026-04-14"),
        serial("2026-04-21"),
        academic_year=spring,
    )

    (outcome,) = sequence.outcomes
    assert outcome.applied == date(2026, 4, 9)
    assert outcome.candidates[0].weekday_matches is True

    repaired = sequence.resolution(2, resolve_date_text("9 Nisan 2025 Perşembe"))
    assert repaired.confidence == CONFIDENCE_SEQUENCE_REPAIR_CORROBORATED
    assert repaired.weekday_matches is True


def test_a_corroborating_weekday_stands_in_for_a_missing_anchor() -> None:
    """The suspect is the last slot of its run, so nothing bounds it from above.

    `G2-TR-PRACTICE` writes exactly this: `24 Aralık 2024 Çarşamba` ends a table
    whose other slots run through December 2025, and 2025-12-24 is a Wednesday.
    """
    sequence = analyze(
        serial("2026-12-18"),
        serial("2026-12-21"),
        serial("2026-12-22"),
        serial("2026-12-23"),
        resolve_date_text("24 Aralık 2025 Perşembe"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.upper_anchor is None
    assert outcome.reason == OUTCOME_REPAIRED
    assert outcome.applied == date(2026, 12, 24)


def test_an_unbounded_suspect_with_no_weekday_is_only_suggested() -> None:
    """The last position of a run has no anchor above it, and a serial names no weekday."""
    sequence = analyze(
        serial("2026-11-19"),
        serial("2026-11-20"),
        serial("2026-11-23"),
        serial("2026-11-23"),
        serial("2020-11-24"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.upper_anchor is None
    assert outcome.reason == OUTCOME_UNBOUNDED
    assert outcome.applied is None
    assert [candidate.value for candidate in outcome.candidates] == [date(2026, 11, 24)]


def test_a_cell_that_contradicts_its_own_weekday_is_never_repaired() -> None:
    """`G1-TR-PRACTICE` writes `21 Mayıs 2026 Perşembe` in a block running to May 2027.

    2027-05-21 fits the neighbours and is a Friday; 2027-05-20 is the Thursday the
    cell names. One half of the cell was copied from a previous year and there is
    no way to tell which, so both readings are offered and neither is applied.
    """
    sequence = analyze(
        serial("2027-05-10"),
        resolve_date_text("21 Mayıs 2026 Perşembe"),
        serial("2027-05-24"),
        serial("2027-05-25"),
        serial("2027-06-01"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.reason == OUTCOME_WEEKDAY_CONTRADICTS
    assert outcome.applied is None
    assert [(candidate.value, candidate.rule) for candidate in outcome.candidates] == [
        (date(2027, 5, 21), RULE_SEQUENCE_YEAR_SUBSTITUTION),
        (date(2027, 5, 20), RULE_SEQUENCE_WEEKDAY_ALTERNATIVE),
    ]


def test_a_weekday_alternative_outside_the_anchors_is_not_offered() -> None:
    """Only the occurrences either side of the substitution, and only inside the bracket."""
    sequence = analyze(
        serial("2027-05-19"),
        resolve_date_text("21 Mayıs 2026 Perşembe"),
        serial("2027-05-22"),
        serial("2027-05-25"),
        serial("2027-06-01"),
    )

    (outcome,) = sequence.outcomes
    alternatives = [
        candidate.value
        for candidate in outcome.candidates
        if candidate.rule == RULE_SEQUENCE_WEEKDAY_ALTERNATIVE
    ]
    assert alternatives == [date(2027, 5, 20)]


def test_a_day_and_month_that_no_year_can_place_is_only_reported() -> None:
    """`G2-EN-ANNUAL` dates three lunch breaks one day before the rows around them."""
    sequence = analyze(
        serial("2026-11-11"),
        serial("2026-11-11"),
        serial("2026-11-10"),
        serial("2026-11-11"),
        serial("2026-11-11"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.reason == OUTCOME_NO_CANDIDATE
    assert outcome.candidates == ()
    assert outcome.applied is None


def test_two_years_that_both_fit_the_anchors_are_never_chosen_between() -> None:
    sequence = analyze(
        serial("2026-08-02"),
        serial("2026-08-03"),
        serial("2020-06-15"),
        serial("2027-07-30"),
        serial("2027-07-31"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.reason in {OUTCOME_AMBIGUOUS, OUTCOME_ANCHORS_TOO_WIDE}
    assert outcome.applied is None


def test_a_bracket_wider_than_the_limit_withholds_the_repair() -> None:
    sequence = analyze(
        serial("2026-09-01"),
        serial("2026-09-02"),
        serial("2020-11-20"),
        serial("2027-01-15"),
        serial("2027-01-16"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.lower_anchor is not None
    assert outcome.upper_anchor is not None
    assert (outcome.upper_anchor - outcome.lower_anchor).days > MAX_ANCHOR_SPAN_DAYS
    assert outcome.reason == OUTCOME_ANCHORS_TOO_WIDE
    assert outcome.applied is None


def test_a_repair_must_land_inside_the_academic_year() -> None:
    """The grace that keeps a date from being suspected is not a target to aim at."""
    sequence = analyze(
        serial("2027-07-28"),
        serial("2027-07-29"),
        serial("2020-09-15"),
        serial("2027-07-30"),
        serial("2027-07-31"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.applied is None
    assert all(WINDOW.contains(candidate.value) for candidate in outcome.candidates)


def test_a_run_shorter_than_the_minimum_states_no_order() -> None:
    entries = run(*([serial("2026-11-20")] * (MIN_RUN_LENGTH - 2) + [serial("2020-11-20")]))

    assert analyze_date_sequence(entries, window=WINDOW).outcomes == ()


def test_a_run_that_is_not_chronological_is_not_read_as_one() -> None:
    """The safety valve for a document nobody has surveyed yet."""
    sequence = analyze(
        serial("2027-05-01"),
        serial("2026-09-01"),
        serial("2027-03-01"),
        serial("2026-10-01"),
        serial("2027-01-01"),
        serial("2026-11-01"),
    )

    assert sequence.outcomes == ()


def test_no_academic_year_means_no_analysis() -> None:
    sequence = analyze(
        serial("2026-11-19"),
        serial("2020-11-20"),
        serial("2026-11-20"),
        serial("2026-11-23"),
        academic_year=None,
    )

    assert sequence.outcomes == ()


def test_an_unreadable_cell_is_neither_a_suspect_nor_an_anchor() -> None:
    sequence = analyze(
        serial("2026-11-17"),
        serial("2026-11-19"),
        unresolved_date("unrecognizedDateFormat"),
        serial("2020-11-20"),
        serial("2026-11-20"),
        serial("2026-11-23"),
    )

    (outcome,) = sequence.outcomes
    assert outcome.key == 3
    assert outcome.applied == date(2026, 11, 20)


def test_a_february_29_that_the_other_year_does_not_have_yields_no_candidate() -> None:
    leap = window("2027-2028")
    sequence = analyze(
        serial("2028-02-27"),
        serial("2028-02-28"),
        serial("2024-02-29"),
        serial("2028-03-01"),
        serial("2028-03-02"),
        academic_year=leap,
    )

    (outcome,) = sequence.outcomes
    assert [candidate.value for candidate in outcome.candidates] == [date(2028, 2, 29)]


@pytest.mark.parametrize(
    "label",
    ["2026", "2026-2028", "not-a-year", "2026-2027-2028", ""],
)
def test_an_unreadable_academic_year_label_builds_no_window(label: str) -> None:
    assert AcademicYearWindow.from_label(label) is None


def test_the_window_runs_august_to_july() -> None:
    assert WINDOW.start == date(2026, 8, 1)
    assert WINDOW.end == date(2027, 7, 31)
    assert WINDOW.years == (2026, 2027)


def test_the_grace_widens_suspicion_but_not_the_year() -> None:
    early = date(2026, 7, 20)

    assert WINDOW.is_plausible(early)
    assert not WINDOW.contains(early)


def test_overrides_layer_under_the_analysis_rather_than_over_it() -> None:
    sequence = analyze(
        serial("2026-11-19"),
        serial("2020-11-20"),
        serial("2026-11-20"),
        serial("2026-11-23"),
        serial("2026-11-24"),
    )
    override = serial("2030-01-01")

    layered = sequence.with_resolutions({1: override, 9: override})

    assert layered.resolution(1, serial("2020-11-20")).value == date(2026, 11, 20)
    assert layered.resolution(9, serial("2020-11-20")) is override
