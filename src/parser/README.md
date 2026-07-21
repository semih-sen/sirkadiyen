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

Parser profiles are registered but not implemented yet. `POST /v1/parse`
therefore returns `501 Not Implemented` for a registered profile and `422` for
an unknown profile. It never reports an empty candidate list as a successful
parse.

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

Until the first fixture-backed profile exists, the golden subject is the
normalization trace over the synthetic fixtures described in
`tests/fixtures/synthetic/README.md`.
