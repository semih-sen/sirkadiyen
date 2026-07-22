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
| `canonical_schedule_records` | the lessons of one revision, with candidate ID, scheduled/cancelled status, stable identity, content hash and an optional explicitly sourced academic department (ADR-018, ADR-035) |
| `revision_validation_findings` | why a revision was validated, held for review, or rejected, with evidence (ADR-029) |

`schedule_revisions.ApprovedBy`, `ApprovalReason` and `ApprovedAtUtc` record who
released a quarantined revision and why (ADR-032). They are null on the ordinary
path: a null means the revision was published on its own validation, **not** that
the approver went unrecorded. There is no identity provider yet, so `ApprovedBy`
is a claim the caller made, not a verified identity.

`schedule_sources.SupportedAudienceSelectors` is a nullable JSONB document naming
the selector values each source may state. **Null means "not declared"** and
leaves the unknown-selector rule unenforced for that source; a declared dimension
with an empty list asserts the dimension may not appear at all. The two must stay
distinguishable, so do not default the column.

`canonical_schedule_records.Department` is nullable by design. Existing records
and sources that do not state an academic department remain null. The semantic
diff never derives it from a title or evidence and does not use secondary
matching unless both records explicitly carry it (ADR-035). Migration
`AddCanonicalDepartment` is additive and does not rewrite historical records.

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
- A store that opens its own transaction must go through `RetriableTransaction`.
  The hosts enable retry on transient failures, and saving inside a hand-rolled
  transaction under a retrying execution strategy throws. A plain test context
  does not reproduce it, so `RetriableTransactionTests` exercises those paths
  against a context configured the way the hosts configure theirs.
- Timestamps are `timestamptz` and are written in UTC. Schedule dates and times
  are stored as local `date` and `time` with an explicit timezone identifier,
  because a lesson is scheduled in `Europe/Istanbul` wall-clock terms.
