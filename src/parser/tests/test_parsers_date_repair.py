"""How a profile applies an operator's date decisions to a run (ADR-139).

:mod:`sirkadiyen_parser.parsers.date_repair` layers the corrections an operator
has accepted over the chronological analysis. The case that matters most here is
the confirmation: a correction whose original and corrected dates are the same,
which is how an operator says "the document is right, stop flagging it".
"""

from datetime import date

from sirkadiyen_parser.contracts.parsing import (
    ParseSourceContext,
    ProgramLanguage,
    SourceDateCorrection,
)
from sirkadiyen_parser.normalization.date_sequence import DateSequenceEntry
from sirkadiyen_parser.normalization.dates import RULE_SERIAL, DateResolution
from sirkadiyen_parser.parsers.date_repair import (
    RULE_OPERATOR_CORRECTION,
    read_date_run,
)


def serial(value: str) -> DateResolution:
    return DateResolution(value=date.fromisoformat(value), rule=RULE_SERIAL, confidence=1.0)


def run(*resolutions: DateResolution) -> tuple[DateSequenceEntry, ...]:
    return tuple(
        DateSequenceEntry(key=index, resolution=resolution)
        for index, resolution in enumerate(resolutions)
    )


def context(*corrections: SourceDateCorrection) -> ParseSourceContext:
    return ParseSourceContext(
        academicYear="2026-2027",
        classYear=1,
        programLanguage=ProgramLanguage.TURKISH,
        timeZoneId="Europe/Istanbul",
        dateCorrections=list(corrections),
    )


# 2026-11-10 drops back inside a rising November run, and no year substitution lands
# it between its neighbours: the analysis reports `noCandidateFitsTheAnchors` and reads
# the row (at index 2) as written.
OUT_OF_SEQUENCE = run(
    serial("2026-11-18"),
    serial("2026-11-19"),
    serial("2026-11-10"),
    serial("2026-11-20"),
    serial("2026-11-21"),
)


def test_without_a_decision_the_out_of_sequence_date_is_suggested() -> None:
    sequence = read_date_run(OUT_OF_SEQUENCE, context=context())

    assert len(sequence.suggestions) == 1
    assert sequence.suggestions[0].original == date(2026, 11, 10)


def test_confirming_the_written_date_stops_it_being_flagged() -> None:
    """A correction with the same original and corrected date is a confirmation.

    It changes no day on any calendar, but it settles the position so the revision
    is no longer held over it, and it carries operator provenance so the confirmed
    reading is traceable to a person.
    """
    confirmation = SourceDateCorrection(
        original=date(2026, 11, 10),
        corrected=date(2026, 11, 10),
        decidedBy="ops@sirkadiyen.example",
        decidedAt="2026-09-04T00:00:00Z",
    )

    sequence = read_date_run(OUT_OF_SEQUENCE, context=context(confirmation))

    assert sequence.suggestions == ()
    assert sequence.repairs == ()

    confirmed = sequence.resolution(2, serial("2026-11-10"))
    assert confirmed.value == date(2026, 11, 10)
    assert confirmed.rule == RULE_OPERATOR_CORRECTION


def test_a_substitution_still_reads_the_written_date_as_the_corrected_one() -> None:
    """The confirmation case must not regress the ordinary substitution it shares code with."""
    substitution = SourceDateCorrection(
        original=date(2026, 11, 10),
        corrected=date(2026, 11, 11),
        decidedBy="ops@sirkadiyen.example",
        decidedAt="2026-09-04T00:00:00Z",
    )

    sequence = read_date_run(OUT_OF_SEQUENCE, context=context(substitution))

    assert sequence.suggestions == ()
    corrected = sequence.resolution(2, serial("2026-11-10"))
    assert corrected.value == date(2026, 11, 11)
    assert corrected.rule == RULE_OPERATOR_CORRECTION
