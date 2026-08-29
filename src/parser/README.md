# Sirkadiyen Parser

The parser is an isolated Python 3.13 service. It accepts normalized spreadsheet
snapshots from the .NET ingestion layer and will produce deterministic canonical
schedule candidates with evidence, warnings, metrics, and confidence indicators.

It must not authenticate users, access production profile data, publish schedule
revisions, or mutate Google Calendar.

## Local setup

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -e ".[dev]"
```

## Quality checks

```powershell
.venv\Scripts\python -m ruff check .
.venv\Scripts\python -m ruff format --check .
.venv\Scripts\python -m mypy
.venv\Scripts\python -m pytest
```

## Run

```powershell
.venv\Scripts\python -m uvicorn sirkadiyen_parser.api:app --reload
```

Available foundation endpoints:

- `GET /health`
- `GET /v1/profiles`
- `POST /v1/parse`

`POST /v1/parse` returns `422` for an unknown profile and `501 Not Implemented`
for a profile that is registered but has no implementation yet. It never reports
an empty candidate list as a successful parse. `GET /v1/profiles` states which
profiles are implemented.

### Parse requests carry source context

A workbook does not state its academic year, class year, program language or
interpretation timezone, and one profile serves several sources. The request
therefore carries them explicitly (ADR-017):

```json
"sourceContext": {
  "academicYear": "2025-2026",
  "classYear": 1,
  "programLanguage": "turkish",
  "timeZoneId": "Europe/Istanbul"
}
```

The profile validates rows against this context instead of inferring it. A row
whose term cell names another class year is excluded and counted.

The context also carries what only the caller can know about *other* documents:
`authoritativeAudienceSelectors` (ADR-110) and `groupRotationCoveredDates`
(ADR-126, below).

### Profiles declare how their source writes a numeric date

`12/11/2026` is 12 November read day-first and 11 December read month-first, and
nothing in the cell says which. Each profile therefore declares its
`numericDateOrder`, reported by `GET /v1/profiles` (ADR-051):

| Declaration | Effect |
| --- | --- |
| `dayFirst` / `monthFirst` | read as declared; a cell only the other order can explain is refused as `numericDateImpossibleUnderDeclaredOrder` |
| `undeclared` | published only when both orders agree, refused as `numericDateOrderNotDeclaredByProfile` otherwise |

`grade2_practice_v1` declares `dayFirst`; every other profile declares
`undeclared`, because their committed fixtures write dates only as spreadsheet
serials and month names, which the `dates.rule.<rule>` metric reports per parse.

The one declaration is what the mechanism is for. The Grade 2 practice workbook
writes exactly one numeric date, `TÜM GRUPLAR 8.10.2025 08:30-10:20`, and version
1.0.0 refused it and named the cell. The Grade 2 annual workbook schedules that
same session as an unambiguous serial — 2025-10-08, 08:30-10:20, `FİZYOLOJİ 1.
UYGULAMASI (TÜM GRUPLAR Amfide yapılacak)` — while the other reading, 10 August
2025, falls outside both the academic year and the block's own 3-16 October
range. The declaration was read off that second source (ADR-075). The Turkish
writing convention was never the argument, and it is not evidence for declaring
the remaining profiles.

## Implemented parser profiles

| Profile | Sources | Fixtures |
| --- | --- | --- |
| `grade1_yearly_v1` | `G1-TR-ANNUAL`, `G1-EN-ANNUAL` | `tests/fixtures/real/g1-{tr,en}-annual.snapshot.json` |
| `grade1_practice_v1` | `G1-TR-PRACTICE` | `tests/fixtures/real/g1-tr-practice.snapshot.json` |
| `grade2_yearly_v1` | `G2-TR-ANNUAL`, `G2-EN-ANNUAL` | `tests/fixtures/real/g2-{tr,en}-annual.snapshot.json` |
| `grade2_practice_v1` | `G2-TR-PRACTICE`, `G2-EN-PRACTICE` | `tests/fixtures/real/g2-{tr,en}-practice.snapshot.json` |
| `grade2_vertical_corridor_v1` | `G2-VERTICAL-AUTUMN`, `G2-VERTICAL-SPRING` | `tests/fixtures/real/g2-vertical-{autumn,spring}.snapshot.json` |
| `grade2_anatomy_autumn_v1`, `grade2_anatomy_spring_v1` | `G2-ANATOMY-{AUTUMN,SPRING}` and their `-EN` counterparts | `tests/fixtures/real/g2-anatomy-{autumn,spring}.snapshot.json` |

`parsers/annual.py` reads the row-oriented annual layout: one lesson per row,
with columns selected by header alias rather than by position, so the Turkish
and English workbooks share one implementation. The Grade 1 and Grade 2 annual
profiles share it too; the class year comes from the request context, so a row
whose term cell names another year is excluded and counted rather than guessed
at.

### A term column a workbook forgot to label

Selecting columns by alias needs the source to write the alias. The 2026-2027
Grade 2 English workbook writes no header over its term column — `A1` is empty
where the 2025-2026 capture wrote `Dönem` — while every row below it still
states `Time Table 2`, so only the label went and the layout is unchanged. With
no `term` alias anywhere in the header row the whole snapshot was rejected as
`noParsableWorksheet`.

A profile may therefore declare `term_column_may_be_unlabelled`, reported by
`GET /v1/profiles`. `grade2_yearly_v1` and `grade3_yearly_v1` do. It is a
fallback, never a preference: it is tried only when no column carries a term
alias, so the Turkish workbook of the same year, which still writes `Dönem`,
is read exactly as before and its candidates are unchanged. The probe reads
only columns left of the date column, only the first value each states, and
adopts a column only when exactly one of them reads as a class year — two
would be a guess about which one addresses the students (ADR-128).

Adopting a column is still a guess about layout, so what matters is how a wrong
one fails: every row then states a class year the request context contradicts
and is refused and counted, which surfaces as an empty result with a reason
rather than as lessons addressed to the wrong cohort.

### A group rotation stated in the annual program is not a whole-class lesson

An annual profile may declare `group_rotation_subjects`, reported by
`GET /v1/profiles`. `grade2_yearly_v1` declares `diseksiyon` and `dissection`
(ADR-073): the Grade 2 workbooks write one dissection session as three
consecutive daily slots, and the anatomy group list — the separate
`SALON GRUP SAATLERİ` source — assigns each student exactly one of them.
Publishing all three to the cohort would book every student into two sessions
they must not attend, so a row whose date the group list has published is
excluded and counted as `rows.ignored.groupRotationCoveredByCompanion`.

The declaration is per profile, not a shared word list: Grade 1 declares none, so
the same title stays published there.

### A rotation date nobody has published is published in full

A group list that has not been uploaded leaves the student with no dissection at
all, which is what the fallback answers (ADR-126). A profile may also declare
`group_rotation_fallback` — `grade2_yearly_v1` does, `grade3_yearly_v1`
deliberately does not — and the request states which dates the owning sources
have already published:

```json
"sourceContext": {
  "academicYear": "2026-2027",
  "classYear": 2,
  "programLanguage": "turkish",
  "timeZoneId": "Europe/Istanbul",
  "groupRotationCoveredDates": ["2026-10-06", "2026-10-08"]
}
```

For a rotation date **not** in that list, every hour of the session is published
to the whole class, each naming which of the day's hours it is
(`DİSEKSİYON (3/13) — 2. saat`, `… — Hour 2` in the English program) and carrying
a note saying the group list is not out yet and that only the student's own hour
will remain once it is. The hour is numbered by start time rather than by row
order, because the start time is what identifies the record.

The parser never decides this on its own: it cannot see whether another document
exists. The caller states the coverage it found, and an empty list means the
owners have published nothing — which is exactly the state that makes all three
hours appear. `rows.publishedGroupRotationFallback` and
`groupRotationFallback.days` count the result, and one
`groupRotationPublishedWithoutCompanion` warning per snapshot names the range.

`parsers/practice.py` reads the rotation matrix, where **a candidate is a cell**:
the group comes from the cell, the subject from its column header, and the date
and time from its row. It states the lettered cohort model explicitly (ADR-020),
so `G` is group G and `A2` is a subgroup of group A, and it refuses any cell
whose value it cannot fully read — a makeup marker naming no group publishes
nothing rather than reaching every student.

`parsers/practice_slots.py` reads the **transpose** of that matrix, which is how
both Grade 2 practice tables are written (ADR-074, ADR-084): a column is a dated
slot whose header holds a slot label, a date and a time range, and a row is a
practice subject naming its room beside it. The English source has one compact
day/month spelling and one header with the date and time on a single line; the
reader separates only those stated parts and does not correct either value. A
candidate is still a cell.
Every row of the worksheet is classified exactly once — block heading, slot
header, subject, or counted as neither — so `rows.scanned` equals the worksheet's
row count and the topic lists between the tables cannot swallow schedule data
unexplained.

The common profile chooses the cohort grammar from authoritative source context:
Turkish accepts the bounded `A`-`H` model and English accepts only the independent
practice groups `İ1` and `İ2`. A token from one programme is never admitted into
the other programme's candidates.

It is stricter than the annual profiles in two places, because this is the source
that decides *which* students receive an event:

- a slot header whose stated weekday contradicts its own date is refused, which
  is how the four hand-typed dates whose year is a year out are caught
- a cell that states a session but no audience — a bare `*`, whose groups the
  source says are announced in another table, or a make-up marker — publishes
  nothing
- a letter run such as `ABCD` names four cohorts, but only within the eight
  letters this source states; without that bound the same rule would read the
  word `SINAV` as five cohorts, one of which is a real group

The declared `dayFirst` order applies only to a cell that states a year. A slot
label such as `2/6` has the shape of a numeric date, and this profile supplies
no year rule, so it is refused rather than completed into 2 June.

### The source those refused `*` cells point at

`parsers/vertical_corridor.py` reads the skill-practice calendar the other two
Grade 2 profiles defer to (ADR-077). The annual program writes these sessions as
a bare `UYGULAMA` placeholder and the practice table marks them `*`, saying their
groups are announced in a separate table; this is that table, published as a Word
document and reaching the parser through the DOCX conversion (ADR-076).

Its axis is the practice table's, transposed back — a row is a dated slot, a
column is one of the five skill practices — but the whole slot is written as
lines of one cell, the way the practice table writes its column headers. The two
Grade 2 rotation profiles therefore share their cell-level rules in
`parsers/cohort_rotation.py`: the eight-letter cohort alphabet, and the refusal
of a slot that contradicts its own weekday. One definition, because a drifting
copy of the cohort bound would not be visible until a word reached real
calendars.

Three properties of the document shape the reading:

- **It is filled in over the year.** Student Affairs edits it, so most dated rows
  state no groups yet. A dated row with no group cells publishes nothing and
  raises nothing; a row with group cells that cannot be dated is a warning,
  because a session with an audience is being lost.
- **It carries a second programme.** The English cohorts `İ1`-`İ3` and the
  separately published `EK-1`-`EK-3` lists sit in the same grid as `A`-`H`. They
  are counted under their own reasons and never published under a source whose
  context states another programme (ADR-048).
- **Its examinations name cohorts with hyphens** (`A-B-C-D SINAV`). The hyphens
  are read as separators only when every part is one of the eight declared
  letters, so `EK-1` and any numeric range keep theirs and are refused.

It also repairs one thing rather than transcribing it: four of the seven spring
tables write `OKSİJEN (Doç. Dr. Bengüsu MİRASOĞLU` and never close the bracket.
An unclosed trailing parenthetical becomes the instructor **only** when it starts
with an academic title, which is what keeps the same practice from reaching
calendars under two titles, one ending mid-bracket.

### A day is a run of hours, not a row with a date

`parsers/anatomy.py` reads the dissection group lists that ADR-073 defers to: the
annual program states all three of a day's dissection hours and this document
says which of them each anatomy group attends. Three columns — a date, an hour,
a group — and three rows per teaching day (ADR-078).

The awkward part is that **one document states a day two different ways**. In its
later rows a day is a vertical merge over its three hours; in its earlier ones the
date is simply typed into the middle row of the three, with the cells above and
below left empty. To a reader those look the same. To a grid the second is two
undated rows.

So a day is recognized by its own shape: a run of consecutive rows whose hours
advance, stating exactly one date between them. A run that states none, or
several, publishes nothing.

- **The day is the unit of refusal.** Publishing the hour that happens to state
  the date and dropping the two beside it would give two of the three groups no
  session and the third one that may not be theirs.
- **A date attributed from a neighbouring row scores 0.8 and says so**, in a
  confidence indicator naming `dateFromNeighbouringRowInDayBlock`. A date reached
  through a merge does not: a merge is the document itself saying the three hours
  are one day.
- **The lesson title comes from the profile**, not the parser. These rows name no
  lesson, so the title is the profile's declared annual marker — `Diseksiyon`, the
  name the annual program gives the same lesson. A profile that declares none
  publishes nothing rather than inventing one.
- The anatomy group is a dimension of its own. It is independent of a student's
  practice group, and a value outside the three the source states is refused. Both
  sources now declare those three, so the same bound is enforced on the profile a
  student submits and on the revision this profile produces (ADR-079).

### A holiday is an all-day item, not a lesson at midnight

The annual sources write a holiday or a semester break as a dated row with an
empty time pair, one row per closed day. Those publish as all-day candidates:
`isAllDay` is true, both times are null, and the event type is `other` (ADR-046).

The shape decides, not the title. A row becomes all-day only when it states a
date, no times **at all**, and a title naming a closure — `tatil`, `bayram`,
`holiday`, or the phrase `labor day`. The same words appear on timed rows, and
those are published as the source states them: `CUMHURİYET BAYRAMI AREFESİ` is
three real hours of teaching, and the English workbook writes its own semester
break as eleven timed 08:30–16:20 rows.

A dated row with no times whose title names no closure is refused with a warning
citing the cell, counted as `rows.ignored.noScheduledTimeAndNoClosure`. A lesson
whose times the faculty forgot must not become an all-day block on every
student's calendar.

Consecutive closed days stay separate records. The source states one row per day
and skips weekends inside a break, so merging them into a span would cover days
the source excluded.

What it deliberately refuses:

- a numeric date column cell that does not declare a date format
- a numeric date text whose meaning depends on a component order the profile has
  not declared
- a dated row with no times that names no closure
- a time cell that the source spreadsheet converted into a date, which would
  otherwise publish a lesson at midnight
- a numeric time cell that is not a day fraction. The Grade 2 English workbook
  holds a bare `9` in an `hh:mm` cell, meaning nine in the morning to whoever
  typed it; as a spreadsheet value it is nine whole days and the workbook itself
  renders it `00:00`. Only a cell whose format declares a full timestamp may
  carry a whole-day part
- an end time that does not follow its start
- a second row claiming a lesson identity an earlier row already published

Each refusal leaves the row unpublished, increments `rows.ignored.<reason>` and,
when the cause is an anomaly rather than expected structure, records a warning
citing the offending cell.

Known limitation: the event type is classified from title and block keywords, so
a lecture whose title merely mentions a practice ("… Uygulama Alanları") is
labelled `practice`. The label is descriptive only; date, time and audience are
unaffected. The practice sources will be the authoritative signal once they are
parsed.

## Shared normalization primitives

`sirkadiyen_parser/normalization/` holds the profile-independent primitives that
every parser profile builds on:

| Module | Responsibility |
| --- | --- |
| `text` | whitespace, invisible characters, Turkish-aware folding, identity keys |
| `grid` | A1 addressing, merged-cell expansion, hidden dimensions, evidence |
| `dates` | serial and Turkish/English text dates, weekday cross-check |
| `times` | day fractions, time text, time ranges |
| `groups` | group expressions for one audience dimension |
| `courses` | display title and normalized course identity |
| `instructors` | academic-title-led instructor extraction |

Two rules run through all of them:

- **Nothing is guessed.** An unresolvable value is returned as unresolved with a
  reason code, so the profile records a warning rather than inventing schedule
  data. Reading a bare numeric cell as a date serial, completing a date that has
  no year, choosing the component order of `12/11/2026`, and reading `0900` as a
  time are all opt-in per profile.
- **Nothing is dropped.** `diagnostics.ParseDiagnostics` accounts for every
  ignored row by reason and derives the result status from what was recorded.

### Parser versions

`sirkadiyen_parser/version.py` holds `PARSER_ENGINE_VERSION`, covering the
shared primitives. Only the parser-profile version travels on the wire, so a
behavioural change to a primitive requires bumping the engine version **and**
every affected profile version.

## Golden files

`tests/golden/` records what a fixture produced. Regenerate deliberately, then
read the diff before committing it:

```powershell
$env:SIRKADIYEN_UPDATE_GOLDEN = "1"; .venv\Scripts\python -m pytest
$env:SIRKADIYEN_UPDATE_GOLDEN = $null
```

Never regenerate a golden file merely to make a failing test pass. A changed
golden file is a changed parser output, and it must be explained.

Two golden subjects exist:

- `tests/golden/normalization/` traces the shared primitives over the synthetic
  fixtures described in `tests/fixtures/synthetic/README.md`.
- `tests/golden/parse/` records whole parse responses for the real fixtures. A
  response holding hundreds of candidates is projected into one digest line per
  candidate plus the complete warnings, metrics and confidence indicators, and a
  digest of the entire serialized response (ADR-019). A digest line reads
  `candidateId|date|start-end|eventType|id:…|content:…|title`, so a moved or
  changed lesson is visible in the diff.
