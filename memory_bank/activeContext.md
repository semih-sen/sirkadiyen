# Active Context

## Current phase

First fixture-backed parser profile.

`grade1_yearly_v1` now parses both Grade 1 annual sources end to end from real
snapshots, with golden-file regression cover. The 18 confirmed sources have a
typed mixed-transport catalog, and Google Sheets responses and local XLSX
fixtures normalize deterministically. Credential factories support offline OAuth
refresh tokens or service accounts, but the local environment still holds only a
client ID and secret. Persistence, polling, Drive/HTTP adapters, the practice
profile, and the frontend do not exist yet.

## Latest implementation session

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

1. Implement `grade1_practice_v1` against the Grade 1 practice rotation matrix.
   Its structure is documented under "Grade 1 practice source structure" below.
2. Obtain an offline source refresh token or a service-account credential; do
   not reuse end-user Calendar authorization for source ingestion.
3. Persist immutable snapshots in PostgreSQL and add the unchanged short circuit.
4. Add worker polling for Google Sheets, then Drive and HTTP sources.
5. Define the initial canonical schedule domain schema.
6. Establish .NET architecture tests.
7. Acquire the missing Grade 1 anatomy and Grade 3 English fixtures.
8. Add DOCX conversion for the confirmed special-program sources.
9. Add Docker Compose and CI quality gates.
10. Decide frontend, session, licensing, initial-sync, and managed-calendar rules.

## Grade 1 practice source structure

Read from `g1-tr-practice.snapshot.json` and not yet implemented. Unlike the
annual sources it is not row-per-lesson:

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

Consequences for the profile: the audience comes from the cell value, the course
from the column header, and the date and time from the row, so a candidate is a
cell rather than a row. Group values mix a plain letter with a letter plus a
subgroup digit, which `parse_group_expression` already handles as one dimension.
Totals, topic lists and lookup tables must be excluded by explicit rules and
counted.

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

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable
