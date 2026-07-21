"""Text normalization primitives.

Source spreadsheets are written for human readers. They contain non-breaking
spaces, soft hyphens, zero-width characters, inconsistent line breaks, and
inconsistent casing. These helpers make text comparable without destroying the
Turkish characters that carry meaning in course and instructor names.

Display text keeps its original letters. Only comparison keys are folded.

Invisible characters are written as escapes on purpose: a literal zero-width
character in this file could not be reviewed in a diff.
"""

import re
import unicodedata

_REMOVED_CHARACTERS = str.maketrans(
    dict.fromkeys(
        [
            "­",  # soft hyphen
            "​",  # zero width space
            "‌",  # zero width non-joiner
            "‍",  # zero width joiner
            "﻿",  # byte order mark
        ]
    )
)

_SPACE_CHARACTERS = str.maketrans(
    {
        "\t": " ",
        " ": " ",  # no-break space
        " ": " ",  # figure space
        " ": " ",  # thin space
        " ": " ",  # narrow no-break space
    }
)

# Turkish casing is not the invariant casing rule: dotted and dotless I differ.
_TURKISH_LOWER = str.maketrans({"I": "ı", "İ": "i"})

_ASCII_FOLD = str.maketrans(
    {
        "ç": "c",
        "ğ": "g",
        "ı": "i",
        "ö": "o",
        "ş": "s",
        "ü": "u",
        "â": "a",
        "î": "i",
        "û": "u",
    }
)

_LINE_BREAK_PATTERN = re.compile("\r\n|[\r\n\v\f  ]")
_SPACE_RUN_PATTERN = re.compile(r" {2,}")
_NON_IDENTITY_PATTERN = re.compile(r"[^a-z0-9]+")


def normalize_text(value: str) -> str:
    """Return single-line display text with collapsed whitespace.

    Letters are preserved. Line breaks become single spaces, so this is only
    appropriate where the caller does not need the original line structure.
    """
    composed = unicodedata.normalize("NFC", value)
    stripped = composed.translate(_REMOVED_CHARACTERS)
    flattened = _LINE_BREAK_PATTERN.sub(" ", stripped)
    return _SPACE_RUN_PATTERN.sub(" ", flattened.translate(_SPACE_CHARACTERS)).strip()


def text_lines(value: str) -> tuple[str, ...]:
    """Split multi-line cell content into normalized, non-empty lines."""
    composed = unicodedata.normalize("NFC", value)
    lines = (normalize_text(line) for line in _LINE_BREAK_PATTERN.split(composed))
    return tuple(line for line in lines if line)


def is_blank(value: str | None) -> bool:
    """Return whether the value carries no readable content."""
    return value is None or not normalize_text(value)


def turkish_lower(value: str) -> str:
    """Lowercase using Turkish dotted and dotless I rules."""
    return value.translate(_TURKISH_LOWER).lower()


def ascii_fold(value: str) -> str:
    """Fold Turkish letters to their ASCII counterparts.

    Only for comparison keys. Never use the result as display text.
    """
    return value.translate(_ASCII_FOLD)


def comparison_key(value: str) -> str:
    """Return a case- and diacritic-insensitive key with collapsed whitespace."""
    return ascii_fold(turkish_lower(normalize_text(value)))


def identity_key(value: str) -> str:
    """Return a slug-style key suitable for stable identity components.

    Every run of characters outside ``[a-z0-9]`` becomes a single hyphen, so
    punctuation and spacing variations in the source do not change identity.
    """
    return _NON_IDENTITY_PATTERN.sub("-", comparison_key(value)).strip("-")
