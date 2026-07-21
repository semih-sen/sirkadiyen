import pytest

from sirkadiyen_parser.normalization.groups import (
    RULE_ALL_GROUPS,
    RULE_ENUMERATED,
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
