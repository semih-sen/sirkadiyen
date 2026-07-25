from dataclasses import dataclass

from sirkadiyen_parser.normalization.dates import NumericDateOrder


@dataclass(frozen=True, slots=True)
class ParserProfileDefinition:
    name: str
    version: str
    source_family: str
    #: How this source family writes ``12/11/2026``. It has no default on
    #: purpose: a profile added without thinking about its date order would
    #: otherwise inherit one, and a wrong order misparses silently whenever both
    #: components are twelve or lower (ADR-051).
    numeric_date_order: NumericDateOrder
    audience_dimensions: tuple[str, ...] = ()
    annual_markers: tuple[str, ...] = ()
    #: Title words whose annual rows this source writes for the whole class but
    #: that a companion source splits into a group rotation. The annual program
    #: states every slot of the rotation, so publishing them to the cohort would
    #: book each student into sessions they must not attend. Declared per profile
    #: rather than shared, because it depends on which companion sources exist for
    #: that grade (ADR-073).
    group_rotation_subjects: tuple[str, ...] = ()


_PROFILE_VERSION = "1.0.0"

#: Every source whose fixture is committed writes dates as spreadsheet serials or
#: as text naming the month, so no source has yet shown which numeric order it
#: uses. Declaring one from the Turkish writing convention would be a guess about
#: a document, so the profiles state that the order is undeclared and refuse the
#: cells that depend on it. The declaration is corrected from the first refusal a
#: real source produces (ADR-051).
_UNDECLARED = NumericDateOrder.UNDECLARED

_PROFILES = (
    # 1.1.0 excludes PDÖ/PBL and lunch; 1.2.0 models free study explicitly.
    # 1.3.0 excludes one-token UYGULAMA/PRACTICE slot placeholders because the
    # companion practice source publishes the authoritative group-specific lesson;
    # 1.4.0 omits "consult the amphitheatre program" instructions from locations;
    # 1.5.0 carries the shared-primitive change that refuses a numeric time cell
    # which is not a day fraction (parser engine 0.2.0, ADR-073). No committed
    # fixture's output moved, but a stored snapshot cannot be proved free of such
    # a cell, and the bump forces the worker to re-parse the stored annual
    # snapshots, since a parse run is keyed by (snapshot, profile, version).
    ParserProfileDefinition("grade1_yearly_v1", "1.5.0", "annual", _UNDECLARED),
    ParserProfileDefinition(
        "grade1_practice_v1",
        _PROFILE_VERSION,
        "practice",
        _UNDECLARED,
        ("practiceGroup", "practiceSubgroup"),
    ),
    ParserProfileDefinition(
        "grade1_anatomy_v1",
        _PROFILE_VERSION,
        "anatomy",
        _UNDECLARED,
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    # The Grade 2 annual workbooks share the Grade 1 row layout, so one profile
    # serves both the Turkish and the English source. What differs is the
    # dissection rotation: the annual program states all three daily slots, and
    # the anatomy group list assigns each student exactly one of them, so those
    # rows belong to the anatomy profiles rather than to the whole class (ADR-073).
    ParserProfileDefinition(
        "grade2_yearly_v1",
        _PROFILE_VERSION,
        "annual",
        _UNDECLARED,
        group_rotation_subjects=("diseksiyon", "dissection"),
    ),
    ParserProfileDefinition(
        "grade2_practice_v1",
        _PROFILE_VERSION,
        "practice",
        _UNDECLARED,
        ("practiceGroup",),
    ),
    ParserProfileDefinition(
        "grade2_anatomy_autumn_v1",
        _PROFILE_VERSION,
        "anatomy",
        _UNDECLARED,
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    ParserProfileDefinition(
        "grade2_anatomy_spring_v1",
        _PROFILE_VERSION,
        "anatomy",
        _UNDECLARED,
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    ParserProfileDefinition(
        "grade2_vertical_corridor_v1",
        _PROFILE_VERSION,
        "verticalCorridor",
        _UNDECLARED,
        ("verticalCorridorGroup",),
        ("Uygulama",),
    ),
    ParserProfileDefinition(
        "grade3_yearly_v1",
        _PROFILE_VERSION,
        "annual",
        _UNDECLARED,
        ("curriculumGroup",),
    ),
    ParserProfileDefinition(
        "grade3_bedside_v1",
        _PROFILE_VERSION,
        "bedsidePractice",
        _UNDECLARED,
        ("curriculumGroup", "bedsideGroup"),
    ),
    ParserProfileDefinition(
        "grade3_faculty_practice_v1",
        _PROFILE_VERSION,
        "facultyPractice",
        _UNDECLARED,
        ("curriculumGroup", "facultyPracticeGroup"),
    ),
    ParserProfileDefinition(
        "weekly_amphitheatre_v1", _PROFILE_VERSION, "amphitheatre", _UNDECLARED
    ),
)

PROFILES = {(profile.name, profile.version): profile for profile in _PROFILES}


def get_profile(name: str, version: str) -> ParserProfileDefinition | None:
    return PROFILES.get((name, version))


def list_profiles() -> tuple[ParserProfileDefinition, ...]:
    return _PROFILES
