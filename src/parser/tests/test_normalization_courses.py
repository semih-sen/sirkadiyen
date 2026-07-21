from sirkadiyen_parser.normalization.courses import (
    RULE_JOINED_LINES,
    RULE_SINGLE_LINE,
    RULE_UNRESOLVED,
    course_identity,
    normalize_course_title,
)

NEWLINE = chr(0x000A)


def test_a_single_line_title_keeps_its_letters() -> None:
    title = normalize_course_title("  Tıbbi   Biyoloji  ")

    assert title.display_title == "Tıbbi Biyoloji"
    assert title.rule == RULE_SINGLE_LINE
    assert title.line_count == 1


def test_a_multi_line_title_joins_rather_than_truncates() -> None:
    title = normalize_course_title(f"Anatomi{NEWLINE}Diseksiyon")

    assert title.display_title == "Anatomi - Diseksiyon"
    assert title.rule == RULE_JOINED_LINES
    assert title.confidence < 1.0
    assert title.line_count == 2


def test_an_empty_title_is_unresolved() -> None:
    title = normalize_course_title("   ")

    assert not title.resolved
    assert title.rule == RULE_UNRESOLVED
    assert title.course_identity is None


def test_identity_is_stable_across_casing_spacing_and_punctuation() -> None:
    assert course_identity("TIBBİ BİYOLOJİ") == course_identity("Tıbbi  Biyoloji")
    assert course_identity("Tıbbi Biyoloji.") == course_identity("Tıbbi Biyoloji")


def test_identity_drops_a_leading_list_number() -> None:
    # A row's ordinal orders the source, it does not name the course.
    assert course_identity("1. Tıbbi Biyoloji") == course_identity("Tıbbi Biyoloji")
    assert course_identity("12) Tıbbi Biyoloji") == course_identity("Tıbbi Biyoloji")


def test_identity_distinguishes_different_courses() -> None:
    assert course_identity("Anatomi") != course_identity("Fizyoloji")


def test_identity_of_unusable_text_is_none() -> None:
    assert course_identity("---") is None
