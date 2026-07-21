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
