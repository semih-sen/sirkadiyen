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

### Profiles declare how their source writes a numeric date

`12/11/2026` is 12 November read day-first and 11 December read month-first, and
nothing in the cell says which. Each profile therefore declares its
`numericDateOrder`, reported by `GET /v1/profiles` (ADR-051):

| Declaration | Effect |
| --- | --- |
| `dayFirst` / `monthFirst` | read as declared; a cell only the other order can explain is refused as `numericDateImpossibleUnderDeclaredOrder` |
| `undeclared` | published only when both orders agree, refused as `numericDateOrderNotDeclaredByProfile` otherwise |

Every profile currently declares `undeclared`, because no committed fixture
writes a date that way: the annual and practice sources use spreadsheet serials
and month names, which the `dates.rule.<rule>` metric reports per parse. The
first source that does write one refuses those rows and names the cells, and that
evidence — not the Turkish writing convention — is what a declaration is
corrected from.

## Implemented parser profiles

| Profile | Sources | Fixtures |
| --- | --- | --- |
| `grade1_yearly_v1` | `G1-TR-ANNUAL`, `G1-EN-ANNUAL` | `tests/fixtures/real/g1-{tr,en}-annual.snapshot.json` |
| `grade1_practice_v1` | `G1-TR-PRACTICE` | `tests/fixtures/real/g1-tr-practice.snapshot.json` |
| `grade2_yearly_v1` | `G2-TR-ANNUAL`, `G2-EN-ANNUAL` | `tests/fixtures/real/g2-{tr,en}-annual.snapshot.json` |
| `grade2_practice_v1` | `G2-TR-PRACTICE` | `tests/fixtures/real/g2-tr-practice.snapshot.json` |

`parsers/annual.py` reads the row-oriented annual layout: one lesson per row,
with columns selected by header alias rather than by position, so the Turkish
and English workbooks share one implementation. The Grade 1 and Grade 2 annual
profiles share it too; the class year comes from the request context, so a row
whose term cell names another year is excluded and counted rather than guessed
at.

### A group rotation stated in the annual program is not a whole-class lesson

An annual profile may declare `group_rotation_subjects`, reported by
`GET /v1/profiles`. `grade2_yearly_v1` declares `diseksiyon` and `dissection`
(ADR-073): the Grade 2 workbooks write one dissection session as three
consecutive daily slots, and the anatomy group list — the separate
`SALON GRUP SAATLERİ` source — assigns each student exactly one of them.
Publishing all three to the cohort would book every student into two sessions
they must not attend, so those rows are excluded and counted as
`rows.ignored.outOfScopeGroupRotation` (159 rows in each Grade 2 workbook) until
the anatomy profiles publish them with their real audience.

The declaration is per profile, not a shared word list: Grade 1 declares none, so
the same title stays published there.

`parsers/practice.py` reads the rotation matrix, where **a candidate is a cell**:
the group comes from the cell, the subject from its column header, and the date
and time from its row. It states the lettered cohort model explicitly (ADR-020),
so `G` is group G and `A2` is a subgroup of group A, and it refuses any cell
whose value it cannot fully read — a makeup marker naming no group publishes
nothing rather than reaching every student.

`parsers/practice_slots.py` reads the **transpose** of that matrix, which is how
the Grade 2 practice table is written (ADR-074): a column is a dated slot whose
header holds a slot label, a date and a time range on separate lines, and a row
is a practice subject naming its room beside it. A candidate is still a cell.
Every row of the worksheet is classified exactly once — block heading, slot
header, subject, or counted as neither — so `rows.scanned` equals the worksheet's
row count and the topic lists between the tables cannot swallow schedule data
unexplained.

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
