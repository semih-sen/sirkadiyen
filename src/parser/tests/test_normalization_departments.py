"""Block and department resolution.

Every literal in this file is a value that appears in the committed Grade 1
workbooks, so the rule is pinned against the source rather than against an idea
of what the source might contain.
"""

import pytest

from sirkadiyen_parser.normalization.departments import (
    CONFIDENCE_DEPARTMENT_LIST_MEMBER,
    CONFIDENCE_MARKED_DEPARTMENT,
    RULE_BLOCK_ONLY,
    RULE_DEPARTMENT_LIST_MEMBER,
    RULE_EMPTY,
    RULE_MARKED_DEPARTMENT,
    resolve_block_and_departments,
    resolve_group_departments,
)


@pytest.mark.parametrize("value", [None, "", "   "])
def test_an_empty_cell_states_neither_a_block_nor_a_department(value: str | None) -> None:
    resolution = resolve_block_and_departments(value)

    assert resolution.curriculum_block is None
    assert resolution.departments == ()
    assert resolution.unmarked_segments == ()
    assert resolution.rule == RULE_EMPTY


@pytest.mark.parametrize(
    "value",
    [
        "DOKU DİLİMİ",
        "HÜCRE DİLİMİ",
        "TIBBA MERHABA DİLİMİ",
        "CELL",
        "MEDICAL SKILLS LABORATORY",
    ],
)
def test_a_cell_without_a_slash_is_only_a_curriculum_block(value: str) -> None:
    resolution = resolve_block_and_departments(value)

    assert resolution.curriculum_block == value
    assert resolution.departments == ()
    assert resolution.rule == RULE_BLOCK_ONLY
    assert not resolution.resolved


@pytest.mark.parametrize(
    ("value", "block", "department"),
    [
        ("HAREKET-1 DİLİMİ / ANATOMİ AD.", "HAREKET-1 DİLİMİ", "ANATOMİ AD."),
        (
            "TIBBA MERHABA DİLİMİ / TIP TARİHİ VE ETİK A.D.",
            "TIBBA MERHABA DİLİMİ",
            "TIP TARİHİ VE ETİK A.D.",
        ),
        ("DOKU DİLİMİ / FİZYOLOJİ AD.", "DOKU DİLİMİ", "FİZYOLOJİ AD."),
        ("CELL / TIBBİ BİYOLOJİ AD.", "CELL", "TIBBİ BİYOLOJİ AD."),
        (
            "MOVEMENT / ANATOMİ ANABİLİM DALI",
            "MOVEMENT",
            "ANATOMİ ANABİLİM DALI",
        ),
    ],
)
def test_a_marked_segment_after_the_slash_is_the_department(
    value: str,
    block: str,
    department: str,
) -> None:
    resolution = resolve_block_and_departments(value)

    assert resolution.curriculum_block == block
    assert resolution.departments == (department,)
    assert resolution.rule == RULE_MARKED_DEPARTMENT
    assert resolution.confidence == CONFIDENCE_MARKED_DEPARTMENT
    assert not resolution.names_several_departments


def test_a_stated_sub_department_is_kept_with_its_department() -> None:
    resolution = resolve_block_and_departments(
        "HÜCRE DİLİMİ / İÇ HASTALIKLARI AD. (ENDOKRİNOLOJİ BD.)"
    )

    assert resolution.departments == ("İÇ HASTALIKLARI AD. (ENDOKRİNOLOJİ BD.)",)
    assert resolution.rule == RULE_MARKED_DEPARTMENT


def test_a_sub_department_written_without_a_dot_is_still_recognized() -> None:
    resolution = resolve_block_and_departments("LIFE STAGES / İÇ HASTALIKLARI AD. (GERİATRİ BD)")

    assert resolution.departments == ("İÇ HASTALIKLARI AD. (GERİATRİ BD)",)


@pytest.mark.parametrize(
    ("value", "unmarked"),
    [
        (
            "YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ  / DİKEY KORİDOR",
            ("DİKEY KORİDOR",),
        ),
        (
            "HAYATIN EVRELERİ DİLİMİ / DİŞ HEKİMLİĞİ FAKÜLTESİ",
            ("DİŞ HEKİMLİĞİ FAKÜLTESİ",),
        ),
        (
            "HAYATIN EVRELERİ DİLİMİ / TIBBİ EKOLOJİ VE HİDROKLİMATOLOJİ",
            ("TIBBİ EKOLOJİ VE HİDROKLİMATOLOJİ",),
        ),
        ("MOLECULER BASIS OF LIFE/CELL", ("CELL",)),
        ("TISSUE/ DİKEY KORİDOR", ("DİKEY KORİDOR",)),
    ],
)
def test_an_unmarked_segment_never_becomes_a_department(
    value: str,
    unmarked: tuple[str, ...],
) -> None:
    """A second block, another faculty or an unmarked name is not a department.

    ``TIBBİ EKOLOJİ VE HİDROKLİMATOLOJİ`` really is a department, and it is still
    refused: the cell does not say so, and publishing it would make the rule
    depend on knowing Turkish faculty structure rather than on the source.
    """
    resolution = resolve_block_and_departments(value)

    assert resolution.departments == ()
    assert resolution.unmarked_segments == unmarked
    assert resolution.rule == RULE_BLOCK_ONLY


def test_a_dashed_list_of_marked_departments_is_an_integrated_session() -> None:
    resolution = resolve_block_and_departments(
        "DİKEY KORİDOR DİLİMİ / RUH SAĞLIĞI VE HASTALIKLARI AD. - HALK SAĞLIĞI AD."
    )

    assert resolution.departments == (
        "RUH SAĞLIĞI VE HASTALIKLARI AD.",
        "HALK SAĞLIĞI AD.",
    )
    assert resolution.names_several_departments
    assert resolution.rule == RULE_MARKED_DEPARTMENT


def test_an_unmarked_member_of_a_marked_list_is_kept_at_lower_confidence() -> None:
    """The source enumerates departments here, so the list decides, not the words.

    Dropping ``İÇ HASTALIKLARI HEMATOLOJİ`` would hide one of the departments
    teaching an integrated session from the student who attends it.
    """
    resolution = resolve_block_and_departments(
        "HÜCRE DİLİMİ / BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD. - İÇ HASTALIKLARI HEMATOLOJİ"
    )

    assert resolution.departments == (
        "BİYOFİZİK AD.",
        "TIBBİ BİYOLOJİ AD.",
        "İÇ HASTALIKLARI HEMATOLOJİ",
    )
    assert resolution.rule == RULE_DEPARTMENT_LIST_MEMBER
    assert resolution.confidence == CONFIDENCE_DEPARTMENT_LIST_MEMBER


def test_a_hyphen_inside_a_name_does_not_split_it() -> None:
    resolution = resolve_block_and_departments("HAREKET-1 DİLİMİ / ANATOMİ AD.")

    assert resolution.curriculum_block == "HAREKET-1 DİLİMİ"
    assert resolution.departments == ("ANATOMİ AD.",)


def test_several_slashes_are_read_segment_by_segment() -> None:
    resolution = resolve_block_and_departments("HAREKET-1 DİLİMİ / HİSTOLOJİ AD./DOKU DİLİMİ")

    assert resolution.curriculum_block == "HAREKET-1 DİLİMİ"
    assert resolution.departments == ("HİSTOLOJİ AD.",)
    assert resolution.unmarked_segments == ("DOKU DİLİMİ",)


def test_a_cell_that_starts_with_a_department_states_no_block() -> None:
    """Otherwise a cell naming only a department would invent a block from it."""
    resolution = resolve_block_and_departments("ANATOMİ AD.")

    assert resolution.curriculum_block is None
    assert resolution.departments == ("ANATOMİ AD.",)


def test_one_department_repeated_is_kept_once() -> None:
    resolution = resolve_block_and_departments(
        "HÜCRE DİLİMİ / TIBBİ BİYOLOJİ AD. - TIBBİ BİYOLOJİ AD."
    )

    assert resolution.departments == ("TIBBİ BİYOLOJİ AD.",)
    assert not resolution.names_several_departments


def test_resolution_is_deterministic() -> None:
    value = "HÜCRE DİLİMİ / BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD."

    assert resolve_block_and_departments(value) == resolve_block_and_departments(value)


def test_a_title_states_the_department_of_each_curriculum_group() -> None:
    """The Grade 3 bedside construction, verbatim from the A workbook."""
    assert resolve_group_departments("Hasta Başı Uygulama-1 A Grubu (İç H.) B Grubu (ÇSvH)") == (
        ("A", "İç H."),
        ("B", "ÇSvH"),
    )


def test_the_pairs_follow_the_order_the_title_writes_them_in() -> None:
    """The same session on another date swaps which half sits with whom."""
    assert resolve_group_departments("Hasta Başı Uygulama-1 A Grubu (ÇSvH) B Grubu (İç H.)") == (
        ("A", "ÇSvH"),
        ("B", "İç H."),
    )


def test_the_english_workbook_writes_the_same_construction() -> None:
    assert resolve_group_departments(
        "Practice with the patient-3 A Grubu (ÇSvH) B Grubu (İç H.)"
    ) == (("A", "ÇSvH"), ("B", "İç H."))


def test_a_joined_group_with_a_parenthetical_note_states_no_department() -> None:
    """A Grade 1 title, and the reason the group letter must stand alone.

    ``A-B GRUBU`` addresses both groups at once and the parenthesis holds a note
    about the lesson, so reading its ``B`` as a group would give five Grade 1
    lessons a department the source never stated.
    """
    assert (
        resolve_group_departments(
            "BİLGİ KURAMI ve BİLİMSEL DÜŞÜNMEYE GİRİŞ A-B GRUBU (İngilizce Tıp ile Ortak Ders)"
        )
        == ()
    )


def test_one_group_alone_states_no_department() -> None:
    assert resolve_group_departments("Uygulama A Grubu (İç H.)") == ()


def test_a_title_without_the_construction_states_nothing() -> None:
    assert resolve_group_departments("Hasta Başı Uygulama-1") == ()
    assert resolve_group_departments(None) == ()


def test_a_group_named_twice_keeps_its_first_department() -> None:
    assert resolve_group_departments("X A Grubu (İç H.) B Grubu (ÇSvH) A Grubu (ÇSvH)") == (
        ("A", "İç H."),
        ("B", "ÇSvH"),
    )
