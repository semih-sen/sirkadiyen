import pytest

from sirkadiyen_parser.normalization.groups import (
    CONFIDENCE_LETTER_RUN,
    RULE_ALL_GROUPS,
    RULE_ENUMERATED,
    RULE_LETTER_RUN,
    RULE_UNRESOLVED,
    parse_group_expression,
)

DIMENSION = "practiceGroup"


@pytest.mark.parametrize("text", ["Tüm gruplar", "TÜM GRUPLAR", "Bütün gruplar", "All groups"])
def test_all_group_phrases_cover_every_group(text: str) -> None:
    expression = parse_group_expression(text, dimension=DIMENSION)

    assert expression.covers_all
    assert expression.values == ()
    assert expression.rule == RULE_ALL_GROUPS


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("Grup 1", ("1",)),
        ("GRUP 2", ("2",)),
        ("G3", ("3",)),
        ("1", ("1",)),
        ("A", ("A",)),
        ("A1", ("A1",)),
        ("Grup 1, 2", ("1", "2")),
        ("Grup 1 ve 2", ("1", "2")),
        ("1; 2; 3", ("1", "2", "3")),
        ("Grup 1-3", ("1", "2", "3")),
        ("A1-A3", ("A1", "A2", "A3")),
    ],
)
def test_enumerated_group_expressions(text: str, expected: tuple[str, ...]) -> None:
    expression = parse_group_expression(text, dimension=DIMENSION)

    assert expression.values == expected
    assert not expression.covers_all
    assert expression.rule == RULE_ENUMERATED


def test_duplicate_values_are_collapsed_in_source_order() -> None:
    assert parse_group_expression("2, 1, 2", dimension=DIMENSION).values == ("2", "1")


def test_leading_zeroes_are_normalized() -> None:
    assert parse_group_expression("Grup 01", dimension=DIMENSION).values == ("1",)


def test_the_dimension_is_carried_through() -> None:
    assert parse_group_expression("2", dimension="anatomyGroup").dimension == "anatomyGroup"


@pytest.mark.parametrize("text", ["Anatomi", "Diseksiyon", "Uygulama", ""])
def test_non_group_text_stays_unresolved(text: str) -> None:
    expression = parse_group_expression(text, dimension=DIMENSION)

    assert not expression.resolved
    assert expression.rule == RULE_UNRESOLVED


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("G", ("G",)),
        ("A", ("A",)),
        ("G2", ("G2",)),
        ("A1,A2", ("A1", "A2")),
        ("Grup A", ("A",)),
    ],
)
def test_lettered_cohorts_read_a_bare_letter_as_a_group(
    text: str,
    expected: tuple[str, ...],
) -> None:
    expression = parse_group_expression(text, dimension=DIMENSION, letter_groups=True)

    assert expression.values == expected
    assert expression.rule == RULE_ENUMERATED


def test_numbered_cohorts_still_read_a_leading_letter_as_a_label() -> None:
    assert parse_group_expression("G2", dimension=DIMENSION).values == ("2",)


def test_a_letter_run_names_several_lettered_groups_with_reduced_confidence() -> None:
    expression = parse_group_expression("AB", dimension=DIMENSION, letter_groups=True)

    assert expression.values == ("A", "B")
    assert expression.rule == RULE_LETTER_RUN
    assert expression.confidence == CONFIDENCE_LETTER_RUN


@pytest.mark.parametrize("text", ["ABC", "ABCD", "SINAV"])
def test_a_longer_run_needs_a_profile_that_has_read_its_source(text: str) -> None:
    # Two letters by default: a caller that has not looked at its source cannot
    # read an ordinary word as a list of cohorts.
    assert not parse_group_expression(text, dimension=DIMENSION, letter_groups=True).resolved


def test_a_profile_may_raise_the_run_length_its_source_writes() -> None:
    expression = parse_group_expression(
        "ABCD",
        dimension=DIMENSION,
        letter_groups=True,
        max_letter_run=8,
    )

    assert expression.values == ("A", "B", "C", "D")
    assert expression.rule == RULE_LETTER_RUN


def test_a_label_carrying_digits_keeps_its_two_letter_cap() -> None:
    # `ABC1` is not a longer cohort label whatever run length a profile allows.
    assert not parse_group_expression(
        "ABC1",
        dimension=DIMENSION,
        letter_groups=True,
        max_letter_run=8,
    ).resolved


@pytest.mark.parametrize("text", ["TELAFİ", "TELAFİ-a2", "H-A-B-i3-i2", "SINAV TELAFİ"])
def test_a_makeup_marker_never_becomes_an_audience(text: str) -> None:
    expression = parse_group_expression(text, dimension=DIMENSION, letter_groups=True)

    assert not expression.resolved


def test_a_partially_understood_expression_resolves_to_nothing() -> None:
    # Keeping only "1" would silently remove every lesson of the other cohort.
    expression = parse_group_expression("Grup 1 ve Anatomi", dimension=DIMENSION)

    assert not expression.resolved
    assert expression.values == ()
    assert expression.reason == "unrecognizedGroupToken"
    assert expression.unresolved_text == "Grup 1 ve Anatomi"


def test_a_mismatched_range_prefix_stays_unresolved() -> None:
    assert not parse_group_expression("A1-B3", dimension=DIMENSION).resolved


def test_a_backwards_range_stays_unresolved() -> None:
    assert not parse_group_expression("3-1", dimension=DIMENSION).resolved


def test_an_implausibly_long_range_stays_unresolved() -> None:
    assert not parse_group_expression("1-400", dimension=DIMENSION).resolved


@pytest.mark.parametrize("text", ["45975", "2025", "101"])
def test_a_long_digit_run_is_not_a_group(text: str) -> None:
    # Date serials, years and room numbers share a column with group labels in
    # some sources. Reading one as a group would target the wrong cohort.
    assert not parse_group_expression(text, dimension=DIMENSION).resolved


@pytest.mark.parametrize("text", ["İ1", "i1", "I1", "ı1"])
def test_the_turkish_spellings_of_one_letter_read_as_one_label(text: str) -> None:
    # Turkish writes this letter with four glyphs and does not case-fold the
    # dotted pair onto the dotless one. `comparison_key` has always treated all
    # four as `i`; the token patterns are ASCII and did not, so a source writing
    # `İ1` lost the cell entirely (ADR-130).
    expression = parse_group_expression(text, dimension=DIMENSION)

    assert expression.resolved
    assert expression.values == ("I1",)


@pytest.mark.parametrize("text", ["TELAFİ", "ANATOMİ", "BİYOFİZİK"])
def test_a_word_holding_that_letter_is_still_not_a_cohort_run(text: str) -> None:
    # The fold is deliberately confined to a one-letter label. Applied to a whole
    # word it would turn `TELAFİ` into six ASCII letters, which a profile reading
    # long runs would accept as six cohorts. A normalization primitive must not
    # rely on the caller's alphabet bound to stay safe.
    expression = parse_group_expression(
        text,
        dimension=DIMENSION,
        letter_groups=True,
        max_letter_run=8,
    )

    assert not expression.resolved


def test_an_ascii_letter_run_is_unaffected_by_the_fold() -> None:
    expression = parse_group_expression(
        "ABCD",
        dimension=DIMENSION,
        letter_groups=True,
        max_letter_run=4,
    )

    assert expression.values == ("A", "B", "C", "D")
