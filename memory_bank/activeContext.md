# Active Context

## Current phase

Repository foundation and architecture definition.

The initial .NET 10 solution and accepted clean-layer project boundaries now exist.
No production business capability, persistence, external integration, frontend, or
parser implementation exists yet.

## Latest implementation session

- Added the root `Sirkadiyen.slnx` solution.
- Added Domain, Application, Contracts, Infrastructure, API, and Worker projects.
- Enforced nullable reference types, latest analysis, warnings-as-errors, and
  deterministic builds through `Directory.Build.props`.
- Added repository formatting, ignore, environment placeholder, and SDK pinning files.
- Added a minimal API health endpoint and cancellable worker host.
- Verified a Release build with zero warnings and zero errors.
- Verified formatting with `dotnet format --verify-no-changes`.

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

## Immediate objectives

1. Reconcile `sheets/source-manifest.md` with the fixtures already under `sheets/`.
2. Define the normalized Google Sheets snapshot contract.
3. Define the parser HTTP request and response contracts.
4. Define the initial canonical schedule schema.
5. Establish .NET unit and architecture test projects.
6. Add the Python parser service and test foundations.
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

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable
