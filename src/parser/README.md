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

## Implemented parser profiles

| Profile | Sources | Fixtures |
| --- | --- | --- |
| `grade1_yearly_v1` | `G1-TR-ANNUAL`, `G1-EN-ANNUAL` | `tests/fixtures/real/g1-{tr,en}-annual.snapshot.json` |
| `grade1_practice_v1` | `G1-TR-PRACTICE` | `tests/fixtures/real/g1-tr-practice.snapshot.json` |

`parsers/annual.py` reads the row-oriented annual layout: one lesson per row,
with columns selected by header alias rather than by position, so the Turkish
and English workbooks share one implementation.

`parsers/practice.py` reads the rotation matrix, where **a candidate is a cell**:
the group comes from the cell, the subject from its column header, and the date
and time from its row. It states the lettered cohort model explicitly (ADR-020),
so `G` is group G and `A2` is a subgroup of group A, and it refuses any cell
whose value it cannot fully read — a makeup marker naming no group publishes
nothing rather than reaching every student.

What it deliberately refuses:

- a numeric date column cell that does not declare a date format
- a time cell that the source spreadsheet converted into a date, which would
  otherwise publish a lesson at midnight
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
  no year, and reading `0900` as a time are all opt-in per profile.
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
