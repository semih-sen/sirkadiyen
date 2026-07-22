# Database

PostgreSQL holds the schedule pipeline: configured sources, immutable snapshots,
parse runs, revisions and canonical records. Entity Framework Core owns the
schema through version-controlled migrations.

## What is stored, and what is not

| Table | Holds |
| --- | --- |
| `schedule_sources` | the configured catalog, including the source context a workbook never states (ADR-017) |
| `source_snapshots` | immutable acquisitions, one row per changed poll (ADR-007) |
| `parse_runs` | one deterministic parser execution per snapshot/profile, including retry attempt count |
| `schedule_revisions` | candidate schedules and the states they move through before publication |
| `canonical_schedule_records` | the lessons of one revision, with candidate ID, scheduled/cancelled status, stable identity and content hash (ADR-018) |

Identity, licensing, student profiles and calendar event mappings are **not**
here yet. Their behavioral decisions are now recorded in ADR-022 through
ADR-027, but their schemas remain future migrations rather than changes to the
already-applied schedule-pipeline migration.

## Local setup

```powershell
docker compose up -d postgres
```

The compose file reads `SIRKADIYEN_POSTGRES_PORT` when the default 5432 is
already taken by a locally installed PostgreSQL:

```powershell
$env:SIRKADIYEN_POSTGRES_PORT = "15432"; docker compose up -d postgres
```

Check that the container is actually listening before pointing anything at it.
When the port is already bound by a local server, the container exits and the
connection silently reaches the local server instead:

```powershell
docker ps --filter name=sirkadiyen-postgres --format "{{.Status}} {{.Ports}}"
```

Then apply the migrations:

```powershell
dotnet tool restore
$env:SIRKADIYEN_DATABASE__CONNECTION_STRING = "Host=localhost;Port=5432;Database=sirkadiyen;Username=sirkadiyen;Password=sirkadiyen"
dotnet dotnet-ef database update --project src/Sirkadiyen.Infrastructure
```

## Migrations

```powershell
dotnet dotnet-ef migrations add <Name> --project src/Sirkadiyen.Infrastructure --output-dir Persistence/Migrations
```

An applied migration is never edited. A schema change is a new migration, and a
destructive change needs a data migration plan recorded with it.

## Tests

Model mapping is asserted without a database, so a lost index or a dropped
unique constraint fails the ordinary test run.

The integration tests need a real PostgreSQL, because the guarantees they check
are enforced by the database rather than by application code: the single
published revision per source, the unique lesson identity per revision, and the
row lock that makes the unchanged-source short circuit safe under concurrent
polls. They report themselves as **skipped** when no database is configured
rather than passing quietly:

```powershell
$env:SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING = "Host=localhost;Port=5432;Database=sirkadiyen_tests;Username=sirkadiyen;Password=sirkadiyen"
dotnet test
```

The fixture drops and re-migrates its database on every run, so a migration that
does not apply cleanly fails there rather than in production.

## Conventions

- Enums are stored by name, so their numeric values may be reordered freely.
- Candidate IDs and scheduled/cancelled status are retained rather than inferred
  later from parser response JSON.
- A failed parser transport attempt resumes the same deterministic parse run and
  increments `AttemptCount`; it does not create a duplicate run.
- Evidence documents — snapshot payloads, audience selectors, parser evidence —
  are `jsonb`, so they can be inspected and queried in place.
- Contested rows carry PostgreSQL's `xmin` as an optimistic concurrency token.
  Raw SQL that materializes such an entity must select `xmin` explicitly.
- Timestamps are `timestamptz` and are written in UTC. Schedule dates and times
  are stored as local `date` and `time` with an explicit timezone identifier,
  because a lesson is scheduled in `Europe/Istanbul` wall-clock terms.
