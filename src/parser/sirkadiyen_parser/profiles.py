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

    #: Whether this profile takes event locations from the weekly amphitheatre
    #: program supplied alongside it (ADR-133). Declared per profile, and as its
    #: own flag rather than as a second `companion_source_family`, because the
    #: two companions answer different questions and a profile may read one, the
    #: other, or both: the Grade 3 annual profile reads bedside topics *and*
    #: rooms, while Grade 1 and Grade 2 read only rooms.
    #:
    #: A profile that declares it and is given no amphitheatre snapshot publishes
    #: exactly what it published before, for the reason ADR-102 gives: the annual
    #: program is the only source of these sessions and must never wait on a
    #: document it merely enriches from.
    amphitheatre_companion: bool = False


_PROFILE_VERSION = "1.1.0"

#: Almost every source whose fixture is committed writes dates as spreadsheet
#: serials or as text naming the month, so those sources have never shown which
#: numeric order they use. Declaring one from the Turkish writing convention
#: would be a guess about a document, so those profiles state that the order is
#: undeclared and refuse the cells that depend on it. The declaration is
#: corrected from the first refusal a real source produces (ADR-051), which is
#: what happened to `grade2_practice_v1` below.
_UNDECLARED = NumericDateOrder.UNDECLARED

#: Every profile below that reads a date is at a version that reads its date
#: column chronologically and repairs a mistyped year from the dates around it
#: (ADR-139, parser engine 0.4.0). The bump is required whether or not a
#: committed fixture moves, for the reason engine 0.2.0 gave: a stored snapshot
#: cannot be proved free of such a cell the way the fixtures can.

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
    # 1.6.0 reads the term column the 2026-2027 Turkish workbook leaves without a
    # header, exactly as `grade2_yearly_v1` 1.2.0 does (ADR-128): its A1 is empty
    # where the 2025-2026 capture wrote `Dönem`, while the column below still
    # states `Dönem 1` on every row. Without it the header row is unrecognizable
    # and the snapshot is rejected whole. The English workbook of the same year
    # still labels the column, and a labelled header is always preferred, so its
    # output does not move.
    # 1.7.0 takes the room from the weekly amphitheatre program when that
    # companion is supplied (ADR-133). The workbook writes `AMFİ PROGRAMINA
    # BAKINIZ` where a room would go, and until now that instruction was counted
    # and dropped, so the event reached a student with no place on it.
    ParserProfileDefinition(
        "grade1_yearly_v1",
        "1.8.0",
        "annual",
        _UNDECLARED,
        term_column_may_be_unlabelled=True,
        amphitheatre_companion=True,
    ),
    # 1.1.0 bounds the cohort alphabet by programme and reads the English
    # cohorts the workbook writes two ways (ADR-130). The Turkish table states
    # A-H, the English one states İ1-İ3, and one reader served both without a
    # bound: an `İ1` cell published nothing and an `i1` cell published `I1`,
    # which is a value no student's profile holds. Both now publish `İ1`.
    ParserProfileDefinition(
        "grade1_practice_v1",
        "1.2.0",
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
    #
    # 1.2.0 reads the term column the 2026-2027 English workbook leaves without a
    # header. Its A1 is empty where every earlier capture of the same source wrote
    # `Dönem`, while the column below it still states `Time Table 2` on every row,
    # so the layout is unchanged and only the label is gone. Without this the
    # header row is unrecognizable and the whole snapshot is rejected, which is
    # what happened. The Turkish workbook of the same year still writes `Dönem`,
    # and a labelled header is always preferred, so nothing changes there.
    ParserProfileDefinition(
        "grade2_yearly_v1",
        # 1.3.0 takes the room from the weekly amphitheatre program (ADR-133).
        "1.4.0",
        "annual",
        _UNDECLARED,
        group_rotation_subjects=("diseksiyon", "dissection"),
        group_rotation_fallback=True,
        term_column_may_be_unlabelled=True,
        amphitheatre_companion=True,
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
    # 1.3.0 carries the engine 0.3.0 cohort-letter fold (ADR-130). Its committed
    # workbook writes the English cohorts in ASCII, so no fixture output moved,
    # but a stored snapshot cannot be proved free of the dotted spelling.
    ParserProfileDefinition(
        "grade2_practice_v1",
        "1.4.0",
        "practice",
        NumericDateOrder.DAY_FIRST,
        ("practiceGroup",),
        group_rotation_subjects=("anatomi", "anatomy", "diseksiyon", "dissection"),
    ),
    # 1.1.0: engine 0.3.0, as grade2_practice_v1 (ADR-130).
    ParserProfileDefinition(
        "grade2_anatomy_autumn_v1",
        "1.2.0",
        "anatomy",
        _UNDECLARED,
        ("anatomyGroup",),
        ("Diseksiyon",),
    ),
    # 1.1.0: engine 0.3.0, as grade2_practice_v1 (ADR-130).
    ParserProfileDefinition(
        "grade2_anatomy_spring_v1",
        "1.2.0",
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
    # 1.1.0: engine 0.3.0, as grade2_practice_v1 (ADR-130). This document does
    # carry the English programme's cohorts, which stay counted and unpublished
    # under a Turkish source either way.
    # 1.3.0 reads the workbook layout Student Affairs moved to for 2026-2027: the
    # corner cell reads `Uygulama yeri`, the practices start in the next column
    # with no separate place column, and each practice states its own room inside
    # its header cell rather than once in a place-statement row (ADR-147).
    ParserProfileDefinition(
        "grade2_vertical_corridor_v1",
        "1.3.0",
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
    # 1.5.0 declares the microbiology/pathology afternoon a group rotation too. The
    # annual writes it as one whole-class placeholder — `Uygulama (Patoloji /
    # Mikrobiyoloji)` / `Practice (Pathology / Microbiology)` — but the dedicated
    # `grade3_micropathology_practice_v1` source names which of the four groups
    # attends microbiology and which attends pathology on each date, so the
    # placeholder is deferred to it rather than shown to a whole class that is in
    # fact split four ways (ADR-146).
    ParserProfileDefinition(
        "grade3_yearly_v1",
        # 1.3.0 takes the room from the weekly amphitheatre program (ADR-133),
        # which it reads alongside the bedside companion it already had.
        "1.5.0",
        "annual",
        _UNDECLARED,
        ("curriculumGroup",),
        group_rotation_subjects=(
            "ogretim uyesi uygulama",
            "patoloji mikrobiyoloji",
            "pathology microbiology",
        ),
        term_column_may_be_unlabelled=True,
        # The bedside document says what each `Hasta Başı` session is about, and
        # only this workbook says when it is, so the topic is read from there and
        # published here (ADR-100, ADR-102). It writes `01.10.2026`, and proves
        # the order itself with the days above twelve it also writes.
        companion_source_family="bedsidePractice",
        companion_numeric_date_order=NumericDateOrder.DAY_FIRST,
        amphitheatre_companion=True,
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
        # Pinned rather than sharing the version above. This lookup states no
        # date anywhere, so the chronological reading of a date column (ADR-139)
        # cannot change what it publishes, and bumping it would re-parse every
        # stored snapshot of it to produce the same rooms.
        "1.0.0",
        "facultyPracticeLocations",
        _UNDECLARED,
    ),
    # The Dönem-3 microbiology/pathology practice program: one Word document with
    # two side-by-side tracks (Mikrobiyoloji and Tıbbi Patoloji) rotating the four
    # microPathologyGroup cohorts A1/A2/B1/B2 through subject blocks. It writes
    # `06.10.2026` and proves the order itself with the days above twelve it also
    # writes (`13.10.2026`), so it declares day-first (ADR-075's pattern). It is
    # catalogued once per program, Turkish and English, from the same file: the
    # document states the group but not the language, so each program's source
    # stamps its own (ADR-145).
    ParserProfileDefinition(
        "grade3_micropathology_practice_v1",
        "1.0.0",
        "micropathologyPractice",
        NumericDateOrder.DAY_FIRST,
        ("microPathologyGroup",),
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
