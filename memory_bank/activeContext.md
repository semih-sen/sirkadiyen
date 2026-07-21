# Active Context

## Current phase

Repository foundation and architecture definition.

The initial .NET 10 solution and accepted clean-layer project boundaries now exist.
No production business capability, persistence, external integration, frontend, or
parser implementation exists yet.

## Latest implementation session

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
- source schedules are online Google Sheets
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

1. Decide how to obtain normalized snapshot JSON for the existing fixtures, so
   parser profiles can be written against real source structure. Either
   implement the .NET Google Sheets acquisition path, or add a clearly separated
   local `.xlsx` to snapshot development converter. This currently blocks all
   fixture-backed profile work.
2. Implement the first fixture-backed annual and practice parser profiles.
3. Define the initial canonical schedule domain schema.
4. Establish .NET architecture tests.
5. Acquire the missing Grade 1 anatomy fixture and raw Google Sheets snapshots
   for DOCX-only source references.
6. Acquire missing Grade 3 English and raw bedside Google Sheets fixtures.
7. Add Docker Compose for PostgreSQL and Redis development dependencies.
8. Add CI quality gates.
9. Decide frontend technology and authentication session flow before frontend work.
10. Define domain entities and state machines.
11. Define license redemption rules.
12. Define initial synchronization workflow.
13. Decide the Google managed-calendar strategy.

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

### Source acquisition

- Google Sheets API snapshots only
- periodic exported `.xlsx` snapshots as backup
- whether Google Drive metadata is used for a preliminary change signal

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

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable
