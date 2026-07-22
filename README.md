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

The API exposes `GET /health` plus the internal revision review and approval
endpoints guarded by the required administrative key. Beyond that:

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
- The worker seeds the source catalog, polls Google Sheets on an adaptive
  Istanbul-time schedule, calls the Python parser over its strict v1 HTTP
  contract, transactionally persists candidate revisions, validates them, and
  publishes healthy revisions while quarantining suspicious ones for review.
- The first semantic diff slice is implemented as a pure deterministic engine:
  exact identity/content comparison, created/updated/deleted/unchanged
  classification, and ambiguity-safe secondary matching for time changes using
  normalized lesson title, instructor and explicitly sourced academic
  department. Persistence and post-publication orchestration are the next step.

Not implemented: Drive/HTTP acquisition, DOCX conversion, semantic diff
persistence/orchestration, the global operational freeze, calendar
synchronization, identity, licensing, and the Next.js frontend.

Published schedule mistakes use forward-fix rather than rollback: correct the
authoritative source and let polling publish a newer revision (ADR-033).

## Local development

Prerequisite: the .NET SDK version selected by `global.json`.

Restore and build the solution:

```powershell
dotnet restore Sirkadiyen.slnx
dotnet build Sirkadiyen.slnx --configuration Release --no-restore
```

Run the API or worker:

```powershell
dotnet run --project src/Sirkadiyen.Api
dotnet run --project src/Sirkadiyen.Worker
```

Start the development dependencies and apply the schema:

```powershell
docker compose up -d postgres redis
dotnet tool restore
$env:SIRKADIYEN_DATABASE__CONNECTION_STRING = "Host=localhost;Port=5432;Database=sirkadiyen;Username=sirkadiyen;Password=sirkadiyen"
dotnet dotnet-ef database update --project src/Sirkadiyen.Infrastructure
```

Copy `.env.example` to a local untracked environment file when configuration is
introduced. Never commit real credentials or tokens.

Database integration tests skip themselves unless
`SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING` is set. `docs/database.md` covers
the schema, migrations, and test conventions.

Parser setup and commands are documented in `src/parser/README.md`.

Generate a deterministic local snapshot from a catalog fixture:

```powershell
dotnet run --project tools/Sirkadiyen.SnapshotTool -- `
  --repository-root . `
  --source-id G1-TR-ANNUAL `
  --output src/parser/tests/fixtures/real/g1-tr-annual.snapshot.json `
  --acquired-at-utc 2026-07-21T00:00:00Z
```

This command is for fixture development only. Production ingestion uses
transport-specific adapters and persists immutable snapshots before parsing.
