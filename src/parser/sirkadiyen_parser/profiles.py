from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class ParserProfileDefinition:
    name: str
    version: str
    source_family: str
    audience_dimensions: tuple[str, ...] = ()
    annual_markers: tuple[str, ...] = ()


_PROFILE_VERSION = "1.0.0"

_PROFILES = (
    ParserProfileDefinition("grade1_yearly_v1", _PROFILE_VERSION, "annual"),
    ParserProfileDefinition(
        "grade1_practice_v1",
        _PROFILE_VERSION,
        "practice",
        ("practiceGroup", "practiceSubgroup"),
    ),
    ParserProfileDefinition(
        "grade1_anatomy_v1",
        _PROFILE_VERSION,
        "anatomy",
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    ParserProfileDefinition("grade2_yearly_v1", _PROFILE_VERSION, "annual"),
    ParserProfileDefinition("grade2_practice_v1", _PROFILE_VERSION, "practice", ("practiceGroup",)),
    ParserProfileDefinition(
        "grade2_anatomy_autumn_v1",
        _PROFILE_VERSION,
        "anatomy",
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    ParserProfileDefinition(
        "grade2_anatomy_spring_v1",
        _PROFILE_VERSION,
        "anatomy",
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    ParserProfileDefinition(
        "grade2_vertical_corridor_v1",
        _PROFILE_VERSION,
        "verticalCorridor",
        ("verticalCorridorGroup",),
        ("Uygulama",),
    ),
    ParserProfileDefinition("grade3_yearly_v1", _PROFILE_VERSION, "annual", ("curriculumGroup",)),
    ParserProfileDefinition(
        "grade3_bedside_v1",
        _PROFILE_VERSION,
        "bedsidePractice",
        ("curriculumGroup", "bedsideGroup"),
    ),
    ParserProfileDefinition(
        "grade3_faculty_practice_v1",
        _PROFILE_VERSION,
        "facultyPractice",
        ("curriculumGroup", "facultyPracticeGroup"),
    ),
    ParserProfileDefinition("weekly_amphitheatre_v1", _PROFILE_VERSION, "amphitheatre"),
)

PROFILES = {(profile.name, profile.version): profile for profile in _PROFILES}


def get_profile(name: str, version: str) -> ParserProfileDefinition | None:
    return PROFILES.get((name, version))


def list_profiles() -> tuple[ParserProfileDefinition, ...]:
    return _PROFILES
