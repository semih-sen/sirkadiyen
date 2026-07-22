# Active Context

## Current phase

Grade 1 profiles and schedule persistence.

Both Grade 1 profiles parse real snapshots with golden-file cover, and
PostgreSQL now holds the ingestion and publication core. The 18 confirmed
sources have a typed catalog that also carries the source context. Credential
factories support offline OAuth refresh tokens or service accounts, but the
local environment still holds only a client ID and secret. Polling, the parser
HTTP client, Drive/HTTP adapters, identity, licensing and the frontend do not
exist yet.

## Latest implementation session

- Implemented `grade1_practice_v1` for the rotation-matrix practice program:
  426 candidates from the Grade 1 Turkish source, with 20 refused cells that are
  all makeup markers naming no group.
- A candidate there is a cell, not a row: the group comes from the cell, the
  subject from the column header, and the date and time from the row.
- Added the lettered-cohort model to the shared group resolver (ADR-020) after
  the real source showed that reading `G` as an abbreviation silently dropped
  group G and turned subgroup `G2` into group 2.
- Added `record_ignored_cell` so matrix sources account for every unpublished
  cell the way row sources account for every unpublished row.
- Added the PostgreSQL schema for sources, snapshots, parse runs, revisions and
  canonical records (ADR-021), with EF Core 10, Npgsql, one migration and a
  design-time factory.
- Implemented the unchanged-source short circuit inside a transaction that locks
  the source row, and proved it against a real database.
- Added `academicYear`, `classYear`, `programLanguage` and `timeZoneId` to the
  source catalog, so the source context ADR-017 requires has a configured home,
  and the catalog now seeds `schedule_sources`.
- Added Docker Compose for PostgreSQL and Redis.
- Ran ruff, ruff format, mypy strict and pytest (237 passing) plus the .NET
  Release build with zero warnings and all 40 .NET tests, 24 of them against a
  real PostgreSQL.

## Previous parser-profile session

- Added `sourceContext` to the parse request in both the C# and Pydantic
  contracts, carrying academic year, class year, program language and timezone
  (ADR-017), and updated the shared contract fixture and both test suites.
- Implemented `grade1_yearly_v1` in `src/parser/sirkadiyen_parser/parsers/`,
  with a parser registry that separates a described profile from an implemented
  one; `/v1/parse` now runs it and `/v1/profiles` reports implementation status.
- Columns are selected by Turkish and English header aliases, so one profile
  serves `G1-TR-ANNUAL` and `G1-EN-ANNUAL`; worksheets without a header row are
  skipped with a recorded reason, and a snapshot with no parsable worksheet is
  rejected rather than reported as an empty success.
- Added stable identity and content hashing (ADR-018) and the rule that a second
  row claiming a published identity is refused, informationally when the rows
  are identical and as a warning when they disagree.
- Confirmed on real data that the sources contain time cells the spreadsheet
  software converted into dates; format-driven resolution refuses them instead
  of publishing midnight lessons.
- Extended the shared primitives: instructor titles written without spaces
  (`Prof.Dr.`), trailing-instructor splitting that never truncates a title, and
  ordinal stripping for `1-` style lecture numbers.
- Added parse golden files as digest projections (ADR-019) and committed the
  Grade 1 English annual snapshot fixture.
- Ran ruff, ruff format, mypy strict and pytest (204 passing) plus the .NET
  Release build with zero warnings and all 16 .NET tests.

## Previous catalog and fixture session

- Added `config/schedule-sources.json` with all 18 supplied source IDs, URLs,
  transports, document formats, parser profiles, and fixture mappings.
- Verified representative Google Sheets and Drive exports against collected
  fixture bytes; the amphitheatre CDN rejects a generic probe with HTTP 403.
- Added a deterministic Open XML fixture converter and snapshot CLI with
  semantic used-range trimming.
- Generated and contract-validated the Grade 1 Turkish annual and practice
  normalized snapshots.
- Added read-only Google credential composition for either an offline refresh
  token or a service account; client ID/secret alone remains insufficient.
- Added six .NET regression tests and two Python real-snapshot contract tests;
  all 15 .NET tests and all 139 Python tests pass.

## Previous ingestion implementation session

- Added the application-layer `ISpreadsheetSnapshotAcquirer` port with explicit
  source, snapshot, spreadsheet, acquisition-time, and range inputs.
- Added the Google Sheets v4 production adapter and pinned
  `Google.Apis.Sheets.v4` 1.75.0.4178.
- Added deterministic normalization of typed values, formulas, notes, effective
  formatting, merges, hidden dimensions, sparse cells, requested ranges, and A1
  evidence addresses.
- Added overlap-conflict diagnostics and SHA-256 content hashing over normalized
  content plus acquisition diagnostics (ADR-014).
- Added a dedicated infrastructure test project with six mapper/hash regression
  tests; the Release build and all nine .NET tests pass.

## Previous parser implementation session

- Added the shared parser normalization primitives under
  `src/parser/sirkadiyen_parser/normalization/`: text folding and identity keys,
  merge-aware grid access with evidence construction, date, time, group, course
  title and instructor resolvers.
- Established the no-inference rule: every resolver reports its rule, confidence
  and a reason when unresolved, and serial dates, missing years and compact
  times are opt-in per parser profile (ADR-011).
- Added `ParseDiagnostics`, which accounts for every ignored row by reason and
  derives the parser result status from what was recorded.
- Added `PARSER_ENGINE_VERSION` covering the shared primitives, separate from
  the transport contract version and the parser-profile versions.
- Added the golden-file harness with explicit regeneration and a direct
  determinism assertion (ADR-012), plus a labelled synthetic snapshot fixture.
- Split the Pydantic contract bases so inbound models stay camel-case-only while
  the parser can construct outbound response models by field name (ADR-013).
- Ran ruff, ruff format, mypy strict and pytest (137 passing) plus the .NET
  Release test run (3 passing).

## Earlier sessions

- Added the root `Sirkadiyen.slnx` solution.
- Added Domain, Application, Contracts, Infrastructure, API, and Worker projects.
- Enforced nullable reference types, latest analysis, warnings-as-errors, and
  deterministic builds through `Directory.Build.props`.
- Added repository formatting, ignore, environment placeholder, and SDK pinning files.
- Added a minimal API health endpoint and cancellable worker host.
- Verified a Release build with zero warnings and zero errors.
- Verified formatting with `dotnet format --verify-no-changes`.
- Reconciled the source manifest with all currently identifiable fixtures.
- Inspected all 17 XLSX fixtures and documented the annual, practice, and weekly
  amphitheatre structural families and known fixture gaps.
- Added the v1 normalized spreadsheet snapshot and parser request/response
  contracts.
- Added camel-case JSON serialization with camel-case string enums.
- Added the first .NET unit test project and contract serialization tests.
- Confirmed the Grade 2 anatomy and vertical-corridor DOCX source families and
  recorded their cross-program and annual-program matching rules.
- Added the Python 3.13 FastAPI parser service foundation and strict Pydantic v1
  transport models mirroring the C# contracts.
- Added the versioned parser profile registry, including independent
  `anatomyGroup` selectors and annual `Diseksiyon`/`Uygulama` markers.
- Added a shared JSON fixture validated by both .NET and Python tests.
- Added Ruff, Mypy, pytest, and HTTP endpoint quality gates.

## Current confirmed requirements

- Google-only registration and login
- administrator-issued license code activation
- user profile collection after activation
- user-triggered initial synchronization
- support for first, second, and third years
- support for Turkish and English programs where sources exist
- Python is parser-only
- source schedules mix Google Sheets, Drive XLSX/DOCX files, and HTTP XLSX files
- sources may change daily
- polling and change detection are required
- only changed calendar events should be modified
- source formats are irregular and require specialized parser profiles
- raw source fixtures will be placed under `sheets/`
- first- and second-year anatomy groups use `1`, `2`, and `3`
- anatomy group is independent from the normal practice group
- second-year anatomy and vertical-corridor schedules are shared by Turkish and English programs
- annual programs label anatomy lessons as `Diseksiyon`
- annual programs label vertical-corridor and other practice lessons as `Uygulama`

## Immediate objectives

1. Compose the worker polling workflow: acquire, store through the short
   circuit, call the parser over HTTP, persist the parse run and create a
   revision. Every piece now exists; nothing joins them.
2. Obtain an offline source refresh token or a service-account credential; do
   not reuse end-user Calendar authorization for source ingestion.
3. Add the parser HTTP client and persist parse runs and canonical records.
4. Implement `grade2_yearly_v1`, which should reuse the annual implementation
   with its own header aliases.
5. Widen the group resolver for the English practice cohort labels (`İ1`) after
   reviewing that source's structure; it also lays dates out differently.
6. Establish .NET architecture tests.
7. Acquire the missing Grade 1 anatomy and Grade 3 English fixtures.
8. Add DOCX conversion for the confirmed special-program sources.
9. Add CI quality gates, including a PostgreSQL service for the integration
   tests.
10. Decide frontend, session, licensing, initial-sync, and managed-calendar rules.

## Grade 1 practice source structure

Implemented by `grade1_practice_v1`. Unlike the annual sources it is not
row-per-lesson:

- The worksheet holds several blocks, one per curriculum block (`TIBBA MERHABA
  DİLİMİ`, `YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ`, `HÜCRE DİLİMİ`, …), separated
  by blank rows and introduced by a merged heading row.
- Each block has its own header row: `Uygulama Tarihi`, `Saat`, then one column
  per practice subject. Later blocks add a `Dikey Koridor` heading spanning
  several subject columns, and subject headers there carry the instructor on a
  second line.
- A data cell holds the group letter or letters attending that practice in that
  slot: `A`, `AB`, `C1`, `E2`, or the words `Telafi` / `TELAFİ` for a makeup.
- Dates appear both as serials and as Turkish text with a weekday. Times are
  ranges in one cell, written with `:` or `.` separators.
- Blocks end with an `Uygulama Sayısı` totals row, followed by
  `UYGULAMA KONU BAŞLIKLARI` free-text topic lists per department, and notes.
- Rows 1 to 23 are location and skill-laboratory lookup tables, not schedule.

The `HAREKET DİLİMİ` block contains a second schedule table nested inside
columns E to G, listing 21 anatomy practice dates with no group column. The
profile detects it, reports it, and reads none of its columns. Those anatomy
sessions cannot be published until the missing Grade 1 anatomy source supplies
the group assignment.

The `HAYATIN EVRELERİ DİLİMİ` block has three dated rows but no subject header,
so it carries no rotation to publish.

## Important unresolved decisions

### License policy

- single-use or multi-use
- expiration
- cohort restrictions
- revocation consequences
- whether one user may redeem multiple licenses

### Google Calendar strategy

- create one dedicated Sirkadiyen calendar
- use a user-selected existing calendar
- create separate calendars by academic year

Preferred direction is one dedicated managed Sirkadiyen calendar per user, but this is not yet final.

### Session architecture

- backend-managed secure HTTP-only cookie
- frontend authentication library integrated with backend session
- token-based approach

Preferred direction is backend-managed secure cookie for the web application.

### Source acquisition operations

- polling interval and retry policy per transport
- whether Google Drive metadata is used for a preliminary change signal
- discovery strategy for the dated amphitheatre CDN URL, whose generic probe
  currently returns HTTP 403

### Publication governance

- which revision anomalies require admin approval
- whether low-risk sources may auto-publish
- emergency freeze and rollback behavior

### Profile schema

Exact required groups for each class year and language must be derived from source files.

## Known risks

- spreadsheet formats may change without warning
- merge and formatting metadata may carry semantic meaning
- source deletion may be temporary or accidental
- course titles may not be stable enough for identity
- users may revoke Google authorization
- concurrent sync jobs may duplicate events without strong idempotency
- initial sync may hit Google API quotas
- license brute-force attempts require rate limiting
- a profile change may require removing and adding many events safely
- weekly amphitheatre data may conflict with annual schedules
- the shared resolvers are calibrated against synthetic fixtures only, so real
  sources will contain date, time and group forms they refuse; each refusal must
  be reviewed as evidence before the resolver is widened
- the day-first reading of numeric dates is a documented assumption; a source
  using month-first order would parse silently wrong whenever both components
  are twelve or lower
- group values are capped at two digits, which is correct for every confirmed
  cohort but would refuse a three-digit group if one is ever introduced
- the annual sources contain time cells that the spreadsheet software converted
  into dates; the parser refuses them, so seven Grade 1 Turkish rows and six
  Grade 1 English rows are currently unpublished and need a source-side fix
- six recurring rows written as `HER HAFTA PAZARTESİ` carry real lessons that no
  profile publishes yet, because completing them would mean inventing dates
- twenty-two holiday and semester-break rows carry no times and are therefore
  not published; students will not see them until all-day entries are modelled
- the annual event type is keyword-classified, so a lecture whose title mentions
  a practice is labelled `practice`; four Grade 1 Turkish lessons are affected
  and only the label is wrong
- the curriculum block stated by the annual sources has no canonical field and
  survives only as evidence and as part of the content hash
- twenty Grade 1 Turkish practice cells say only `TELAFİ` and name no group, so
  those makeup sessions are not published
- the Grade 1 English practice source labels cohorts `İ1`, `İ2`, `İ3` and lays
  its dates out differently, so `grade1_practice_v1` publishes almost nothing
  from it; its fixture is deliberately not committed until the source has been
  reviewed
- reading `AB` as groups A and B follows from the cohort model rather than from
  the cell, so those candidates carry reduced confidence
- snapshot payloads are stored whole, and the retention policy is still open;
  one Grade 1 annual snapshot is about seven megabytes

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable
