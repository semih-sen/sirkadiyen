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

    #: Whether this profile publishes every slot of its declared rotation for the
    #: dates the companion group source has not published (ADR-126). The Grade 2
    #: annual program states all three dissection hours of a session, and the
    #: anatomy group list assigns each student one of them — but until that list
    #: is uploaded a student sees no dissection at all, which is what this
    #: fallback answers. Declared per profile because a rotation is only safe to
    #: publish whole when its slots are consecutive hours of one session a student
    #: can read their own hour out of. The Grade 3 faculty-practice rotation is
    #: eight parallel slots and declares nothing here, so it keeps excluding them.
    group_rotation_fallback: bool = False

    #: Whether this source family writes its term column without a header. The
    #: Grade 3 workbooks do, and the column is not optional there: it states the
    #: curriculum group, so a row read without it would reach the wrong half of
    #: the class. Declared rather than always attempted, because adopting an
    #: unlabelled column is a guess about layout, and every other source family
    #: labels the column it means.
    term_column_may_be_unlabelled: bool = False

    #: The source family of the companion documents this profile enriches from,
    #: when it has one. A companion is never published: it only says more about
    #: sessions this profile already states, so a profile that is given none
    #: produces exactly what it produced before companions existed (ADR-102).
    companion_source_family: str | None = None

    #: How the companion writes ``12/11/2026``, which is a property of that
    #: document rather than of this one and so is declared separately (ADR-051).
    companion_numeric_date_order: NumericDateOrder = NumericDateOrder.UNDECLARED


_PROFILE_VERSION = "1.0.0"

#: Almost every source whose fixture is committed writes dates as spreadsheet
#: serials or as text naming the month, so those sources have never shown which
#: numeric order they use. Declaring one from the Turkish writing convention
#: would be a guess about a document, so those profiles state that the order is
#: undeclared and refuse the cells that depend on it. The declaration is
#: corrected from the first refusal a real source produces (ADR-051), which is
#: what happened to `grade2_practice_v1` below.
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
    #
    # 1.1.0 adds the fallback ADR-126 decided: for a dissection date no anatomy
    # group list has published, all three hours are published to the whole class
    # with the hour named in the title, so a student attends the one their group
    # is assigned to instead of seeing nothing. A date the anatomy source does
    # publish keeps deferring to it, so uploading the autumn list takes autumn out
    # of the fallback while spring stays in it until its own list arrives.
    ParserProfileDefinition(
        "grade2_yearly_v1",
        "1.1.0",
        "annual",
        _UNDECLARED,
        group_rotation_subjects=("diseksiyon", "dissection"),
        group_rotation_fallback=True,
    ),
    # The Grade 2 practice table is the transpose of the Grade 1 one: a column is
    # a dated slot and a row is a practice subject. Anatomy appears in it as a
    # row of dissection dates rather than of groups, which is the same rotation
    # the anatomy sources own, so it is declared out of scope here too (ADR-074).
    #
    # 1.1.0 declares the numeric date order this source writes. It is the first
    # committed source to write a numeric date at all — one cell, `TÜM GRUPLAR
    # 8.10.2025 08:30-10:20` — and 1.0.0 refused it exactly as ADR-051 requires.
    # The Turkish annual workbook schedules that same session as a spreadsheet
    # serial: 2025-10-08, 08:30-10:20, "FİZYOLOJİ 1. UYGULAMASI (TÜM GRUPLAR
    # Amfide yapılacak)". The other reading, 10 August 2025, falls outside both
    # the academic year and the block's own 3-16 October range. The declaration
    # is therefore read off a second source rather than off a writing convention
    # (ADR-075).
    # 1.2.0 verifies the English workbook as current-year content despite its
    # misleading filename, admits its independent İ1/İ2 practice groups, and
    # reads the two compact slot-header spellings it actually contains
    # (ADR-084).
    ParserProfileDefinition(
        "grade2_practice_v1",
        "1.2.0",
        "practice",
        NumericDateOrder.DAY_FIRST,
        ("practiceGroup",),
        group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
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
    # The vertical-corridor calendar states the *same* lettered cohorts as the
    # practice table — its `*` cells are the ones this document answers — so it
    # selects students by the practice group they already have rather than by a
    # third group they would have to declare (ADR-020, ADR-077). The dissection
    # rotation is written into this grid too, and the anatomy sources own it.
    ParserProfileDefinition(
        "grade2_vertical_corridor_v1",
        _PROFILE_VERSION,
        "verticalCorridor",
        _UNDECLARED,
        ("practiceGroup", "practiceSubgroup"),
        ("Uygulama",),
        group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
    ),
    # The Grade 3 class is split into two curriculum groups that each get their own
    # workbook, and the column stating which one a row belongs to carries no header
    # (hence `term_column_may_be_unlabelled`). The faculty-practice rotation is
    # written into these workbooks as eight `Öğretim üyesi Uygulama N` slots per
    # block, all eight of which a student would otherwise be booked into; the
    # faculty source assigns each cohort exactly one (ADR-073). The bedside rows
    # are deliberately *not* declared here: this workbook is the only source that
    # proves a date and a time for them.
    # 1.1.0 narrows a row's curriculum groups to the ones the source owns, so the
    # sessions both halves of the class attend are published once by the workbook
    # written for each half instead of twice, in two wordings (ADR-110).
    # 1.2.0 publishes the department a bedside or patient-practice title states for
    # the half of the class the row is addressed to (ADR-113).
    ParserProfileDefinition(
        "grade3_yearly_v1",
        "1.2.0",
        "annual",
        _UNDECLARED,
        ("curriculumGroup",),
        group_rotation_subjects=("ogretim uyesi uygulama",),
        term_column_may_be_unlabelled=True,
        # The bedside document says what each `Hasta Başı` session is about, and
        # only this workbook says when it is, so the topic is read from there and
        # published here (ADR-100, ADR-102). It writes `01.10.2026`, and proves
        # the order itself with the days above twelve it also writes.
        companion_source_family="bedsidePractice",
        companion_numeric_date_order=NumericDateOrder.DAY_FIRST,
    ),
    # The bedside document writes its dates as `01.10.2026`, and proves the order
    # itself: several of them state a day above twelve (`22.10.2026`). It is the
    # second source after `grade2_practice_v1` to need the declaration (ADR-075).
    # It publishes nothing of its own — the annual program states these sessions
    # with the time each actually has — so it declares no audience dimension it
    # does not use: the document names one curriculum group per column and no
    # division below that (ADR-100).
    ParserProfileDefinition(
        "grade3_bedside_v1",
        _PROFILE_VERSION,
        "bedsidePractice",
        NumericDateOrder.DAY_FIRST,
        ("curriculumGroup",),
    ),
    ParserProfileDefinition(
        "grade3_faculty_practice_v1",
        _PROFILE_VERSION,
        "facultyPractice",
        _UNDECLARED,
        ("curriculumGroup", "facultyPracticeGroup"),
    ),
    # The practice-location workbook is a lookup, not a schedule: it states a room
    # per department under a curriculum-block heading and no date anywhere. It is
    # named here so the catalog stops dispatching it to the faculty matrix reader,
    # which would refuse every row of it. It selects no audience of its own, and
    # the rooms reach students only once the faculty practice can be joined to it.
    ParserProfileDefinition(
        "grade3_faculty_locations_v1",
        _PROFILE_VERSION,
        "facultyPracticeLocations",
        _UNDECLARED,
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
