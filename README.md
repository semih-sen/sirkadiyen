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

- The .NET ingestion layer acquires Google Sheets responses and administratively
  uploaded DOCX files, normalizing both onto the versioned snapshot contract. A
  typed catalog records the 22 confirmed mixed-transport sources and the source
  context each one needs.
- The Python parser has golden-file-backed profiles for the implemented Grade 1
  annual/Turkish-practice and Grade 2 annual/Turkish-and-English-practice,
  anatomy and vertical-corridor source families. The Grade 2 English practice
  workbook is verified as 2025-2026 content despite its misleading filename
  (ADR-084).
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

- Student profiles, Calendar authorization, initial synchronization, incremental
  diff dispatch and non-destructive reconciliation are implemented. Calendar work
  is admitted independently of the slower source-polling clock, so a newly queued
  initial sync is picked up within the configured idle-check interval (ADR-082).
- The Next.js frontend implements Google sign-in and the student onboarding path.
  Its SuperAdmin panel currently covers the operational freeze, revision review
  and administrative document upload.

- Google Drive acquisition is implemented (ADR-083): the vertical-corridor Word
  documents are downloaded over the Drive v3 REST API with the shared read-only
  source credential, verified against what Drive states about the file, and
  converted onto the same normalized snapshot a sheet produces.

Still open: HTTP acquisition and a workbook converter for the Drive-published
Grade 3 sources, the remaining source fixtures and parser profiles, the rest of
the operator surfaces, automated frontend tests, production deployment topology,
CI, and the planned observability stack.

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

The worker exposes internal process health at `/health/live` and operational readiness at
`/health/ready`. It binds to `SIRKADIYEN_WORKER__HEALTH_URL` (loopback port 5081 by default),
while the API probes `SIRKADIYEN_WORKER__BASE_URL`. Do not publish this listener through the
public reverse proxy; the authenticated SuperAdmin API is the external health surface.

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
