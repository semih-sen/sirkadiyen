import pytest

from sirkadiyen_parser.normalization.instructors import (
    RULE_ACADEMIC_TITLE,
    RULE_NO_INSTRUCTOR,
    extract_instructors,
)

NEWLINE = chr(0x000A)


@pytest.mark.parametrize(
    "text",
    [
        "Prof. Dr. Ayşe Demir",
        "Doç. Dr. Mehmet Öz",
        "Öğr. Gör. Elif Yıldız",
        "Uzm. Dr. Can Arslan",
        "Yrd. Doç. Dr. Deniz Ak",
    ],
)
def test_a_leading_academic_title_marks_an_instructor(text: str) -> None:
    extraction = extract_instructors(text)

    assert extraction.instructors == (text,)
    assert extraction.remainder == ""
    assert extraction.rule == RULE_ACADEMIC_TITLE


def test_a_course_and_an_instructor_on_separate_lines_are_split() -> None:
    extraction = extract_instructors(f"Tıbbi Biyoloji{NEWLINE}Prof. Dr. Ayşe Demir")

    assert extraction.instructors == ("Prof. Dr. Ayşe Demir",)
    assert extraction.remainder == "Tıbbi Biyoloji"


def test_a_course_and_an_instructor_in_one_line_are_split_on_the_comma() -> None:
    extraction = extract_instructors("Fizyoloji, Prof. Dr. Zeynep Kaya")

    assert extraction.instructors == ("Prof. Dr. Zeynep Kaya",)
    assert extraction.remainder == "Fizyoloji"


def test_several_instructors_keep_their_source_order() -> None:
    extraction = extract_instructors("Prof. Dr. B Yılmaz, Doç. Dr. A Kaya")

    assert extraction.instructors == ("Prof. Dr. B Yılmaz", "Doç. Dr. A Kaya")


def test_text_without_a_title_is_returned_whole_rather_than_dropped() -> None:
    extraction = extract_instructors("Anatomi Diseksiyon")

    assert extraction.instructors == ()
    assert extraction.remainder == "Anatomi Diseksiyon"
    assert extraction.rule == RULE_NO_INSTRUCTOR


def test_empty_text_yields_nothing() -> None:
    extraction = extract_instructors("   ")

    assert extraction.instructors == ()
    assert extraction.remainder == ""
