import pytest

from sirkadiyen_parser.normalization.instructors import (
    RULE_ACADEMIC_TITLE,
    RULE_NO_INSTRUCTOR,
    RULE_TRAILING_INSTRUCTORS,
    extract_instructors,
    split_trailing_instructors,
    starts_with_academic_title,
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


@pytest.mark.parametrize(
    ("segment", "expected"),
    [
        ("Prof. Dr. Ayşe Demir", True),
        ("Prof.Dr. Ayşe Demir", True),
        ("Dr.Öğr.Üyesi Hacer Yavru", True),
        ("Doç.Dr. Nilüfer Alçalar", True),
        ("Dram Atölyesi", False),
        ("21", False),
    ],
)
def test_an_academic_title_is_recognized_spaced_or_run_together(
    segment: str,
    expected: bool,
) -> None:
    assert starts_with_academic_title(segment) is expected


def test_a_trailing_instructor_is_separated_from_the_title() -> None:
    split = split_trailing_instructors("1-Hücre zarı / Prof.Dr. Fatma Oğuz (Prof. Dr. Selçuk D)")

    assert split.title == "1-Hücre zarı"
    assert split.instructors == ("Prof.Dr. Fatma Oğuz (Prof. Dr. Selçuk D)",)
    assert split.rule == RULE_TRAILING_INSTRUCTORS


def test_several_trailing_instructors_keep_their_order() -> None:
    split = split_trailing_instructors("Entegre oturum / Prof. Dr. A Yılmaz/ Doç. Dr. B Kaya")

    assert split.title == "Entegre oturum"
    assert split.instructors == ("Prof. Dr. A Yılmaz", "Doç. Dr. B Kaya")


def test_a_spaced_dash_may_separate_the_instructor() -> None:
    split = split_trailing_instructors("ENTEGRE OTURUM - Sigara ve sağlığım - Dr. Emine Ş")

    assert split.title == "ENTEGRE OTURUM - Sigara ve sağlığım"
    assert split.instructors == ("Dr. Emine Ş",)


def test_a_slash_that_does_not_introduce_an_instructor_stays_in_the_title() -> None:
    split = split_trailing_instructors("Anatomi Uygulama 14 / 21")

    assert split.title == "Anatomi Uygulama 14 / 21"
    assert split.instructors == ()
    assert split.rule == RULE_NO_INSTRUCTOR


def test_a_tail_that_is_only_partly_instructors_is_never_truncated() -> None:
    text = "2- İskelet kası / Prof. Dr. Tamer D (Prof. Dr. Aytül U) - (İngilizce Tıp ile ortak)"

    split = split_trailing_instructors(text)

    assert split.title == text
    assert split.instructors == ()
