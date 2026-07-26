# Sirkadiyen

Sirkadiyen is a personal academic schedule synchronization platform for Istanbul Faculty of Medicine students.

It parses irregular schedules published through Google Sheets, Google Drive
files, and direct spreadsheet downloads; converts them into a canonical internal
schedule model; determines which lessons apply to each student; and synchronizes
only the changed events with the student's Google Calendar.

## Core goals

- Support first-, second-, and third-year students.
- Support Turkish and English programs.
- Parse irregular and frequently changing schedule documents.
- Detect source changes quickly.
- Update only affected Google Calendar events.
- Preserve a complete audit trail for every parsed lesson and synchronization action.
- Keep the parser isolated from authentication, user management, and calendar synchronization.

## Main components

- Frontend application
- ASP.NET Core backend API
- .NET background worker
- Python parsing service
- PostgreSQL database
- Redis for caching, locks, and short-lived coordination
- Google OAuth, Google Sheets API, and Google Calendar API

## Repository conventions

The AI agent must read the following files before making architectural or implementation changes:

1. `AI_GUIDELINE.md`
2. `memory_bank/projectbrief.md`
3. `memory_bank/productContext.md`
4. `memory_bank/systemPatterns.md`
5. `memory_bank/techContext.md`
6. `memory_bank/activeContext.md`
7. `memory_bank/progress.md`
8. `memory_bank/decisionLog.md`

Source spreadsheet fixtures must be placed under `sheets/`.

## Initial directory proposal

```text
/
├── AI_GUIDELINE.md
├── README.md
├── memory_bank/
├── sheets/
├── src/
│   ├── Sirkadiyen.Api/
│   ├── Sirkadiyen.Application/
│   ├── Sirkadiyen.Domain/
│   ├── Sirkadiyen.Infrastructure/
│   ├── Sirkadiyen.Worker/
│   ├── Sirkadiyen.Contracts/
│   └── parser/
├── tests/
│   ├── Sirkadiyen.UnitTests/
│   ├── Sirkadiyen.IntegrationTests/
│   ├── Sirkadiyen.ArchitectureTests/
│   └── parser/
└── docker/
```

This structure may evolve only through an explicit architectural decision recorded in `memory_bank/decisionLog.md`.

## Current implementation status

The .NET 10 solution foundation is initialized with the following projects:

- `Sirkadiyen.Domain`
- `Sirkadiyen.Application`
- `Sirkadiyen.Contracts`
- `Sirkadiyen.Infrastructure`
- `Sirkadiyen.Api`
- `Sirkadiyen.Worker`

The API exposes `GET /health`, Google-only sign-in, a backend-managed secure
cookie session, and SuperAdmin-protected revision/diff operations. Beyond that:

- The .NET ingestion layer acquires a Google Sheets v4 response and normalizes
  values, formulas, formatting, merges, and hidden dimensions into the versioned
  snapshot contract. A typed catalog records the 18 confirmed mixed-transport
  sources and the source context each one needs.
- The Python parser implements two profiles against real snapshots:
  `grade1_yearly_v1` for both Grade 1 annual sources and `grade1_practice_v1`
  for the Grade 1 Turkish rotation matrix, both with golden-file regression
  cover.
- PostgreSQL holds configured sources, immutable snapshots, parse runs,
  revisions and canonical records, including the unchanged-source short circuit.
  See `docs/database.md`.
- PostgreSQL also holds Google-authenticated local users. Google ID credentials
  are verified server-side and discarded; the browser receives only an
  HTTP-only secure application cookie. See `docs/authentication.md`.
- PostgreSQL-backed single-use licenses use keyed hashes, transactional
  redemption, append-only audits, concurrency constraints and revocation-driven
  suspension. New codes use the compact `SRK-XXXXX-XXXXX` format, while
  SuperAdmins can activate an existing user directly with a required audited
  reason. Onboarding state is derived from these records. See
  `docs/licensing-and-onboarding.md`.
- The worker seeds the source catalog, polls Google Sheets on an adaptive
  Istanbul-time schedule, calls the Python parser over its strict v1 HTTP
  contract, transactionally persists candidate revisions, validates them, and
  publishes healthy revisions while quarantining suspicious ones for review.
- A PostgreSQL-backed global operational freeze is read at runtime before every
  source acquisition, before a parse run starts or resumes, and immediately
  before publication. SuperAdmins can freeze or unfreeze it through the
  CSRF-protected API. Its transitions are append-only audit records; an
  unreadable control fails closed (ADR-034, ADR-043).
- The semantic diff is a pure deterministic engine — exact identity/content
  comparison, created/updated/deleted/unchanged classification, and
  ambiguity-safe secondary matching for time changes using normalized lesson
  title, instructor and explicitly sourced academic department. Every published
  revision is diffed against the one it superseded, in its own transaction, and
  the result is stored once as `Ready` or `Held` (ADR-039, ADR-040). A held diff
  is released only by a named operator stating a reason, and never when the hold
  is ambiguity (ADR-042).

Not implemented: Drive/HTTP acquisition, DOCX conversion, calendar
synchronization, student profiles, Calendar authorization, and the Next.js
frontend.

Published schedule mistakes use forward-fix rather than rollback: correct the
authoritative source and let polling publish a newer revision (ADR-033).

## Local development

Prerequisite: the .NET SDK version selected by `global.json`.

Restore and build the solution:

```powershell
dotnet restore Sirkadiyen.slnx
dotnet build Sirkadiyen.slnx --configuration Release --no-restore
```

### Configuration

Copy `.env.example` to `.env` in the repository root and fill it in. That file is
untracked; never commit real credentials or tokens.

The API, the worker, the EF Core design-time tools and the integration tests all
load it themselves: each searches upward from its output directory for the
nearest `.env` and applies every variable the process environment does not
already define (ADR-041). Running from a project directory therefore works
without exporting anything:

```powershell
dotnet run --project src/Sirkadiyen.Api
dotnet run --project src/Sirkadiyen.Worker
```

An exported or container-injected variable always wins over the file, so a
deployed host — which ships no `.env` at all — is unaffected.

### Dependencies and schema

```powershell
docker compose up -d postgres redis
dotnet tool restore
dotnet dotnet-ef database update --project src/Sirkadiyen.Infrastructure
```

The migration command reads `SIRKADIYEN_DATABASE__CONNECTION_STRING` from `.env`
and falls back to a local development host when it is absent.

Database integration tests skip themselves unless
`SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING` is set, in `.env` or in the
environment. That database is dropped and re-migrated on every run, so it must
never name a working one. `docs/database.md` covers the schema, migrations, and
test conventions.

Parser setup and commands are documented in `src/parser/README.md`.

Generate a deterministic local snapshot from a catalog fixture:

```powershell
dotnet run --project tools/Sirkadiyen.SnapshotTool -- `
  --repository-root . `
  --source-id G1-TR-ANNUAL `
  --output src/parser/tests/fixtures/real/g1-tr-annual.snapshot.json `
  --acquired-at-utc 2026-07-21T00:00:00Z
```

The fixture may be an XLSX workbook or a DOCX document: several programs are
published as Word files, and both are converted onto the same normalized
snapshot contract, so a parser profile never learns which one its source was
(ADR-076). A document the catalog does not describe yet is named directly with
`--document <repository-relative path>` instead of being looked up by source ID.

A source that is handed out rather than published — the Grade 2 anatomy group
lists arrive once a semester with no URL — is catalogued under the
`administrativeUpload` transport and names itself `urn:sirkadiyen:upload:{sourceId}`
rather than claiming a location it does not have (ADR-079). It is looked up by
source ID like any other.

This command is for fixture development only. Production ingestion uses
transport-specific adapters and persists immutable snapshots before parsing.
An administratively uploaded document is acquired over the API instead
(`POST /api/sources/{sourceId}/document`, ADR-080); a DOCX that must be
*downloaded* still has no transport, so the two vertical-corridor sources cannot
be acquired at runtime yet.
