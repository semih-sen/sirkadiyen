from sirkadiyen_parser.normalization.text import (
    ascii_fold,
    comparison_key,
    identity_key,
    is_blank,
    normalize_text,
    text_lines,
    turkish_lower,
)

NO_BREAK_SPACE = chr(0x00A0)
SOFT_HYPHEN = chr(0x00AD)
ZERO_WIDTH_SPACE = chr(0x200B)
LINE_SEPARATOR = chr(0x2028)
NEWLINE = chr(0x000A)


def test_normalize_text_collapses_whitespace_and_removes_invisible_characters() -> None:
    raw = f"  Tıbbi{NO_BREAK_SPACE}{NO_BREAK_SPACE}Biyo{SOFT_HYPHEN}loji{ZERO_WIDTH_SPACE}  "

    assert normalize_text(raw) == "Tıbbi Biyoloji"


def test_normalize_text_preserves_turkish_letters() -> None:
    assert normalize_text("ÖĞRETİM ÜYESİ") == "ÖĞRETİM ÜYESİ"


def test_normalize_text_flattens_every_line_break_form() -> None:
    raw = f"A{NEWLINE}B{LINE_SEPARATOR}C"

    assert normalize_text(raw) == "A B C"


def test_normalize_text_does_not_split_on_ordinary_spaces() -> None:
    assert text_lines("Anatomi Diseksiyon") == ("Anatomi Diseksiyon",)


def test_text_lines_splits_and_drops_empty_lines() -> None:
    raw = f"Tıbbi Biyoloji{NEWLINE}{NEWLINE}Prof. Dr. Ayşe Demir{NEWLINE}  "

    assert text_lines(raw) == ("Tıbbi Biyoloji", "Prof. Dr. Ayşe Demir")


def test_is_blank_treats_invisible_only_content_as_blank() -> None:
    assert is_blank(None)
    assert is_blank(f" {ZERO_WIDTH_SPACE}{NO_BREAK_SPACE} ")
    assert not is_blank("0")


def test_turkish_lower_uses_dotted_and_dotless_i_rules() -> None:
    assert turkish_lower("IĞDIR") == "ığdır"
    assert turkish_lower("İSTANBUL") == "istanbul"


def test_ascii_fold_maps_turkish_letters() -> None:
    assert ascii_fold("çğıöşü") == "cgiosu"


def test_comparison_key_is_case_and_diacritic_insensitive() -> None:
    assert comparison_key("TIBBİ Biyoloji") == comparison_key("tıbbi biyoloji")


def test_identity_key_ignores_punctuation_and_spacing_variation() -> None:
    assert identity_key("Tıbbi Biyoloji (I)") == "tibbi-biyoloji-i"
    assert identity_key("Tıbbi   Biyoloji  -  I") == identity_key("Tıbbi Biyoloji I")


def test_identity_key_of_unusable_text_is_empty() -> None:
    assert identity_key("--- ///") == ""
