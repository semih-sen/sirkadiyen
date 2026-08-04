# Database

PostgreSQL holds local users, the schedule pipeline, and its operational safety
state. Entity Framework Core owns the schema through version-controlled
migrations.

## What is stored, and what is not

| Table | Holds |
| --- | --- |
| `users` | Google subject, verified/normalized email, display name, explicit role and sign-in timestamps (ADR-045, ADR-052) |
| `licenses` | explicit `Code`/`Manual` activation kind, optional keyed code hash, lifecycle state, redemption/revocation ownership and timestamps (ADR-022, ADR-053, ADR-054) |
| `license_audits` | append-only creation, redemption, expiration and revocation transitions; never plaintext codes |
| `schedule_sources` | the configured catalog, including the source context a workbook never states (ADR-017) |
| `source_snapshots` | immutable acquisition metadata and retained normalized payloads, one row per changed poll (ADR-007, ADR-044) |
| `parse_runs` | one deterministic parser execution per snapshot/profile, including retry attempt count |
| `schedule_revisions` | candidate schedules and the states they move through before publication |
| `canonical_schedule_records` | the lessons of one revision, with candidate ID, scheduled/cancelled status, stable identity, content hash and an optional explicitly sourced academic department (ADR-018, ADR-035) |
| `revision_validation_findings` | why a revision was validated, held for review, or rejected, with evidence (ADR-029) |
| `schedule_diffs` | one stored semantic diff per published revision, including its dispatch state and any release audit fields (ADR-039, ADR-040, ADR-042) |
| `schedule_diff_entries` | the created, updated, deleted, unchanged, or ambiguous record pairs that make up a diff |
| `operational_freeze_control` | the singleton runtime switch read before acquisition, parsing and publication (ADR-034, ADR-043) |
| `operational_freeze_audits` | append-only freeze and unfreeze transitions with actor, reason, timestamp and correlation ID |
| `finance_account_holders` | whose cash box or bank account something is; an optional linked user and a basis-point profit-distribution share (0 = not a partner) (ADR-093) |
| `finance_accounts` | one cash or bank account per holder; TRY only; no balance column — see "Finance" below |
| `finance_transactions` | the editable business event behind one or more ledger postings: opening balance, income, expense, transfer, or distribution payout |
| `finance_ledger_entries` | one signed posting per affected account; always rewritten wholesale on edit, never patched |
| `finance_audits` | the module's own append-only log — distinct from `audit_events` — with a full before/after image including entries |
| `finance_obligations` | receivables and debts; posts no ledger entries of its own |
| `finance_settlements` | links one obligation to the ordinary cash transaction that settled part of it |
| `finance_distributions` | one profit-distribution execution, non-repeatable per period and idempotent per confirmation token |
| `profit_distribution_shares` | one partner's payout within a distribution, with the pre-rounding numerator kept for auditability |

The freeze control migration seeds exactly one unfrozen baseline row. That seed
is not an operator action and therefore has no audit entry. Every later state
transition updates the singleton and appends its audit row in one transaction;
repeating the current state is idempotent and writes no fictional transition.
The API exposes `GET /api/operations/freeze` and the CSRF-protected
`POST /api/operations/freeze` behind the `SuperAdmin` policy. The write endpoint
derives its actor from the verified session and uses the same atomic store as the
worker gates; operators must not bypass it with direct SQL.

`source_snapshots.AcademicYear` copies the source context at acquisition, so an
academic-year catalog rollover cannot reclassify historical evidence. The
worker keeps the first payload of the source's current academic year, the latest
payload, every changed payload from the last ten days and every payload needed
by an absent/running/failed parse run. It sets `Payload` to null and records
`PayloadPrunedAtUtc` only after a snapshot is eligible (ADR-044). Metadata,
parse responses, revisions, canonical records and diffs remain. This is an
explicit retention deletion, never an overwrite with different evidence.

`schedule_revisions.ApprovedBy`, `ApprovalReason` and `ApprovedAtUtc` record who
released a quarantined revision and why (ADR-032). They are null on the ordinary
path: a null means the revision was published on its own validation, **not** that
the approver went unrecorded. `ApprovedBy` is now the verified email derived
from the authenticated SuperAdmin session; it is never accepted from the
approval payload.

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

Student profiles, Google Calendar connections and event mappings are **not**
here yet. Licensing is implemented by `licenses` and `license_audits`; profile
and Calendar schemas remain future migrations.

## Finance

An account's balance is never a stored column. It is derived on every read as
`SUM(Amount) WHERE OccurredOn <= X` over `finance_ledger_entries`, so a rewritten posting (an
edit) simply produces a different sum — nothing needs a fix-up pass. `finance_accounts` still
matters even though it holds no balance: it is the `SELECT … FOR UPDATE` lock target that makes
"read balance, then debit" safe under concurrency for transfers and distribution payouts.
Ordinary income/expense entry takes no lock and may legitimately leave a negative balance
(overdraft is reported, not blocked).

There is no opening-balance column either. An account's opening balance is itself a
`finance_transactions` row of `Kind = 'OpeningBalance'`, dated on the account's opening date, so
"at most one opening balance per account" is a filtered unique index
(`finance_ledger_entries (FinanceAccountId, Kind) WHERE Kind = 'OpeningBalance'`) rather than a
special case in every balance query.

Transactions are editable and hard-deletable, not reversal-only. `finance_audits` is what makes
that safe: every create, edit, and delete writes one row in the same commit as the change,
carrying a full before/after image — including the ledger entries, not just the transaction row
— serialized with `ContractJson.CreateOptions()`. A deleted transaction is fully reconstructable
from its audit row alone. No update or delete method exists on the audit store; it is append-only
by construction, not by convention.

A transaction referenced by a `finance_settlements` row or a `profit_distribution_shares` row
cannot be edited or deleted. This is enforced twice: both FKs are `DeleteBehavior.Restrict`, so the
database refuses regardless of code path, and the store pre-checks so the operator gets a named
outcome (`TransactionSettlesAnObligation` / `TransactionIsADistributionPayout`) telling them what
to undo first, rather than a raw constraint-violation error.

`finance_obligations.SettledAmount` is a write guard for that row, not a reporting source. A
historical period's Receivables/Debts figure recomputes from `finance_settlements` dated on or
before the period end instead — reading the cached field would silently use today's settlement
state for a question about the past.

A profit distribution is non-repeatable per period (`UNIQUE (PeriodStartOn, PeriodEndOn) WHERE
Status = 'Executed'`) and idempotent per confirmation token (`UNIQUE (ConfirmationToken)`), both
enforced by the schema rather than application logic alone. `finance_transactions.FinanceDistributionId`
has no FK in the `AddFinanceLedger` migration that introduces the column — `finance_distributions`
does not exist yet at that point — and gains one later, in `AddFinanceDistributions`, once EF can
model the relationship.

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
dotnet dotnet-ef database update --project src/Sirkadiyen.Infrastructure
```

The design-time factory reads `SIRKADIYEN_DATABASE__CONNECTION_STRING` from the
environment, then from the repository's `.env` (ADR-041), and falls back to a
local development host when neither supplies one. Export the variable for the
session to override the file:

```powershell
$env:SIRKADIYEN_DATABASE__CONNECTION_STRING = "Host=localhost;Port=15432;Database=sirkadiyen;Username=sirkadiyen;Password=sirkadiyen"
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
published revision per source, the unique lesson identity per revision, the row
lock that makes the unchanged-source short circuit safe under concurrent polls,
and single-use license redemption under competing requests. They report
themselves as **skipped** when no database is configured rather than passing
quietly:

```powershell
$env:SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING = "Host=localhost;Port=5432;Database=sirkadiyen_tests;Username=sirkadiyen;Password=sirkadiyen"
dotnet test
```

The fixture drops and re-migrates its database on every run, so a migration that
does not apply cleanly fails there rather than in production.

## Conventions

- Enums are stored by name, so their numeric values may be reordered freely.
- Google subject and normalized email are independently unique; the application
  never auto-links two Google subjects merely because their verified email
  collides.
- License code hashes are unique and exactly 32 bytes. A partial unique index on
  `RedeemedByUserId` allows at most one current `Redeemed` activation per user;
  revoked history remains and a later explicit replacement license is possible.
- `Code` licenses require a 32-byte hash. `Manual` activations require the hash
  to be null and are distinguished explicitly rather than backed by a hidden
  generated code.
- Candidate IDs and scheduled/cancelled status are retained rather than inferred
  later from parser response JSON.
- A failed parser transport attempt resumes the same deterministic parse run and
  increments `AttemptCount`; it does not create a duplicate run.
- Retained evidence documents — snapshot payloads, audience selectors, parser
  evidence — are `jsonb`, so they can be inspected and queried in place.
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
- Money is `decimal` mapped `numeric(18,2)`. There is no `Money` value object and no EF complex
  type — `Sirkadiyen.Domain.Finance.FinanceAmount` is the only guard, and it **rejects** a value
  with more than two decimal places rather than rounding it. Postgres itself does not protect
  this: a raw `numeric(18,2)` insert with three decimal places silently rounds (half away from
  zero, not half to even), which `FinanceConstraintTests` documents rather than assumes.
- `FinanceCategory` is one enum with disjoint income and expense members
  (`FinanceCategories.IsIncome`/`IsExpense`), so the check constraint that mirrors it stays
  unambiguous without a separate direction column.
