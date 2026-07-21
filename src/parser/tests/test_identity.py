from sirkadiyen_parser.contracts.parsing import IdentityComponent
from sirkadiyen_parser.identity import (
    HASH_PREFIX,
    build_identity_components,
    content_hash,
    stable_identity,
)


def components(course: str = "hucre-zari") -> list[IdentityComponent]:
    return build_identity_components(
        (
            ("academicYear", "2025-2026"),
            ("classYear", "1"),
            ("courseIdentity", course),
        )
    )


def test_identity_components_keep_their_declared_order() -> None:
    assert [component.name for component in components()] == [
        "academicYear",
        "classYear",
        "courseIdentity",
    ]


def test_stable_identity_is_prefixed_and_repeatable() -> None:
    first = stable_identity(components())

    assert first.startswith(HASH_PREFIX)
    assert first == stable_identity(components())


def test_a_changed_component_changes_the_identity() -> None:
    assert stable_identity(components()) != stable_identity(components("hucre-cekirdegi"))


def test_component_order_is_part_of_the_identity() -> None:
    forward = build_identity_components((("a", "1"), ("b", "2")))
    reversed_order = build_identity_components((("b", "2"), ("a", "1")))

    assert stable_identity(forward) != stable_identity(reversed_order)


def test_content_hash_does_not_depend_on_key_order() -> None:
    assert content_hash({"title": "Ders", "room": "A"}) == content_hash(
        {"room": "A", "title": "Ders"}
    )


def test_an_absent_field_differs_from_an_empty_one() -> None:
    assert content_hash({"room": None}) != content_hash({"room": ""})


def test_a_changed_field_changes_the_content_hash() -> None:
    assert content_hash({"room": "A"}) != content_hash({"room": "B"})
