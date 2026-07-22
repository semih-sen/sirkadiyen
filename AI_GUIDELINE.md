# AI Agent Guidelines

## 1. Mission

You are contributing to Sirkadiyen, a production-grade academic schedule synchronization platform.

Your job is not merely to make the current task pass. Your job is to preserve correctness, traceability, maintainability, idempotency, and safe synchronization behavior across the whole system.

The system handles real student calendars. A silent parser mistake may create, move, duplicate, or delete hundreds of calendar events. Treat all source parsing and calendar mutation code as high-risk code.

## 2. Mandatory reading order

Before starting any non-trivial task, read:

1. `memory_bank/projectbrief.md`
2. `memory_bank/productContext.md`
3. `memory_bank/systemPatterns.md`
4. `memory_bank/techContext.md`
5. `memory_bank/activeContext.md`
6. `memory_bank/progress.md`
7. `memory_bank/decisionLog.md`

When working on spreadsheets or parsing, also read:

8. `sheets/README.md`
9. Any source-specific notes located beside the relevant spreadsheet fixture.

Do not assume prior chat context is available. The repository files are the source of truth.

## 3. Memory bank discipline

At the end of every meaningful implementation session:

- Update `memory_bank/activeContext.md`.
- Update `memory_bank/progress.md`.
- Add architectural or behavioral decisions to `memory_bank/decisionLog.md`.
- Update `memory_bank/systemPatterns.md` or `memory_bank/techContext.md` only when the accepted system design changes.
- Never rewrite historical decisions to make the past appear cleaner.
- Mark superseded decisions explicitly.

## 4. Scope discipline

Do not introduce a new framework, database, queue, authentication method, or architectural pattern without:

1. Explaining the need.
2. Comparing it with the current approach.
3. Recording the accepted decision in `decisionLog.md`.

Avoid speculative abstractions. Implement the smallest design that safely supports the current requirement and known near-term roadmap.

Do not add features merely because they are fashionable.

## 5. Architecture boundaries

The following boundaries are strict:

### Frontend

Responsible for:

- Google sign-in entry point
- onboarding
- license activation UI
- student profile UI
- synchronization status
- admin interfaces
- user-facing validation and feedback

The frontend must not parse schedules or directly mutate Google Calendar events.

### ASP.NET Core backend

Responsible for:

- authentication and authorization
- user accounts
- license activation
- student profile management
- Google OAuth token lifecycle
- schedule source configuration
- canonical schedule persistence
- diff calculation
- affected-user resolution
- calendar synchronization orchestration
- audit logging
- administrative operations

### .NET worker

Responsible for:

- source polling
- snapshot acquisition
- parse job orchestration
- revision validation
- diff generation
- synchronization jobs
- retries
- reconciliation
- scheduled maintenance

### Python parser

Responsible only for:

- accepting a source snapshot and parser profile
- interpreting spreadsheet structure
- producing canonical candidate records
- returning evidence, warnings, metrics, and confidence
- deterministic parsing

The Python parser must not:

- authenticate users
- access user profiles
- manage licenses
- call Google Calendar
- update production database records directly
- decide which users receive an event
- contain business rules unrelated to interpreting a spreadsheet

## 6. Authentication rules

- Registration and login use Google authentication only.
- Do not implement password registration.
- Do not store user passwords.
- A Google-authenticated account is not automatically an active Sirkadiyen account.
- Account activation requires a valid license code.
- Authorization must be enforced by the backend, never only by the frontend.
- Admin permissions must use explicit roles or policies.
- Never trust role, license, grade, language, or group data supplied by the client without server-side validation.

## 7. License rules

License codes are security-sensitive.

- Store only a secure hash of each license code.
- Display plaintext codes only at creation time if needed.
- A license code must have an explicit status.
- Support revocation and expiration even if the first release does not expose every option in the UI.
- License redemption must be transactional and idempotent.
- Prevent race conditions where the same single-use code is redeemed twice.
- Record who created, redeemed, revoked, or modified a license.
- Never log plaintext license codes.
- Rate-limit redemption attempts.

Suggested states:

```text
Created
Active
Redeemed
Revoked
Expired
```

## 8. User onboarding rules

The expected onboarding flow is:

1. User signs in with Google.
2. Backend creates or finds the local user account.
3. User enters a license code.
4. Backend validates and redeems the code.
5. User enters academic profile data.
6. Backend validates supported combinations.
7. User grants the necessary Google Calendar permission if not already granted.
8. User starts the initial synchronization.
9. Backend creates synchronization jobs.
10. UI shows progress and final status.

The system must tolerate interrupted onboarding and allow safe continuation.

## 9. Schedule parsing rules

Every source snapshot must be immutable.

Never overwrite a raw snapshot.

Every parser response must include:

- parser profile
- parser version
- source identifier
- snapshot identifier
- canonical candidate records
- source evidence
- warnings
- metrics
- confidence or validation indicators

Parsing must be deterministic. The same parser version and same snapshot must produce the same output.

Do not silently discard rows. Every ignored row must be explainable through metrics, evidence, or warnings.

Do not infer missing dates, times, groups, or course identities unless an explicit rule exists in the parser profile.

A parser warning must never be converted into a silent success merely to keep the pipeline moving.

## 10. Canonical schedule rules

External spreadsheet layout must never leak into calendar synchronization logic.

All parsed lessons must be converted into a canonical schedule representation before publication.

A canonical record must be able to express:

- academic year
- class year
- program language
- curriculum group
- practice group or subgroup
- event type
- normalized course identity
- original display title
- date
- start and end time
- instructor
- academic department when explicitly stated by the source
- location
- source provenance
- parser version
- stable identity
- content hash
- publication status

Stable identity and mutable content must be separate concepts.

Changing a room, instructor, title formatting, start time, or end time should normally update the existing logical lesson instead of creating a duplicate.

## 11. Revision and publication rules

Parsed output must not immediately become live schedule data.

Required flow:

```text
Snapshot
→ Parse
→ Validate
→ Review if necessary
→ Publish revision
→ Calculate semantic diff
→ Resolve affected users
→ Synchronize calendars
```

A revision must be rejected or held for review when anomaly thresholds are exceeded.

Examples:

- unexpected mass deletion
- dramatic event count drop
- invalid dates
- overlapping records
- unknown group expressions
- impossible lesson durations
- low-confidence required fields

Never publish a suspicious revision merely because parsing technically succeeded.

## 12. Diff rules

Diffing must be semantic, not merely row-based.

Classify records as:

```text
Created
Updated
Deleted
Unchanged
Ambiguous
```

Never convert an ambiguous match into a destructive delete-and-create operation without an explicit safe rule.

Prefer updating a known Google event over deleting and recreating it.

For a lesson whose start time changed, secondary matching may use normalized
lesson title, instructor, and explicitly sourced academic department according
to ADR-035. Missing attributes must not be inferred from evidence. Any
one-to-many or many-to-one candidate set remains `Ambiguous`.

## 13. Google Calendar rules

Every managed event must be traceable to:

- user
- canonical schedule record
- schedule revision
- Google calendar
- Google event ID
- last applied content hash

Use private extended properties on Google events to store Sirkadiyen identifiers.

All calendar write operations must be idempotent.

Do not perform full-range delete-and-recreate synchronization except for an explicit, audited repair operation.

Do not delete an event merely because a parser temporarily failed to see it.

Deletion requires a published revision and a valid semantic diff.

Use retries with exponential backoff for transient Google API failures.

Handle revoked permissions and expired tokens without corrupting synchronization state.

## 14. Data integrity and transactions

Use database transactions for:

- license redemption
- publication of a schedule revision
- updating the current canonical version
- creating sync jobs from a diff
- mapping newly created Google events
- state transitions that must not partially succeed

Use optimistic concurrency or appropriate locking for contested records.

Every background job must be safe to retry.

## 15. Security

- Keep secrets out of source control.
- Use environment variables or a secret manager.
- Never log access tokens, refresh tokens, authorization headers, license plaintext, or sensitive Google responses.
- Encrypt refresh tokens at rest.
- Apply least-privilege Google scopes.
- Validate all external payloads.
- Use anti-forgery and secure cookie practices where relevant.
- Apply rate limiting to authentication-adjacent and license endpoints.
- Treat spreadsheet contents as untrusted input.

## 16. Coding standards

### .NET

- Use nullable reference types.
- Use cancellation tokens for I/O and background operations.
- Use dependency injection.
- Prefer explicit domain types over primitive strings for important concepts.
- Keep domain logic outside controllers.
- Keep infrastructure details outside the domain layer.
- Use structured logging.
- Do not catch exceptions without handling, translating, or rethrowing them meaningfully.
- Prefer immutable request and response contracts.
- Validate commands at application boundaries.
- Use UTC internally, with explicit `Europe/Istanbul` conversion for schedule interpretation.
- Do not use `DateTime.Now` directly in domain logic. Inject a clock abstraction.

### Python

- Use type hints.
- Use Pydantic models for parser input and output contracts.
- Keep parser functions deterministic.
- Separate normalization, structure detection, extraction, and validation.
- Avoid modifying global state.
- Do not use pandas as a substitute for understanding merged-cell and layout semantics.
- Add fixture-based tests for every parser profile.
- Include parser version in every output.

### Frontend

- Treat backend state as authoritative.
- Do not duplicate authorization logic.
- Use typed API contracts.
- Show actionable synchronization errors.
- Never claim synchronization succeeded before backend confirmation.

## 17. Testing rules

No critical behavior is complete without tests.

Required categories:

- domain unit tests
- parser fixture tests
- golden-file tests
- integration tests
- idempotency tests
- concurrency tests for license redemption
- semantic diff tests
- Google Calendar adapter tests with mocked API
- migration tests where appropriate
- architecture boundary tests

For every parser bug, add a regression fixture before or together with the fix.

For every calendar duplication bug, add an idempotency regression test.

## 18. Database migration rules

- Use version-controlled migrations.
- Never edit an already-applied migration.
- Avoid destructive schema changes without a data migration plan.
- Add indexes intentionally.
- Record assumptions about unique constraints.
- Prefer explicit status enums represented safely for future evolution.

## 19. Observability

Every important workflow must expose:

- correlation ID
- source ID
- snapshot ID
- revision ID
- user ID when applicable
- sync job ID
- operation status
- retry count
- failure category
- duration

Never rely only on free-form log messages.

Track metrics for:

- source poll latency
- parser duration
- parser warning count
- revision event count
- diff counts
- queue depth
- sync latency
- Google API error rate
- duplicate-prevention hits
- users out of sync

## 20. Documentation behavior

When behavior changes:

- update relevant documentation in the same task
- update API contracts
- update examples
- update the memory bank
- record unresolved risks in `activeContext.md`

Do not leave documentation knowingly inconsistent with the code.

## 21. Work reporting

At the end of a task, report:

- what changed
- files changed
- tests added or updated
- tests executed
- unresolved risks
- required follow-up
- memory bank updates

Do not claim success when tests were not run.

## 22. Prohibited shortcuts

Do not:

- delete and recreate all nearby calendar events as normal sync behavior
- put parsing logic in controllers
- put calendar logic in the Python parser
- store plaintext license codes
- use spreadsheet row numbers as permanent lesson identity
- trust client-supplied roles or activation state
- silently ignore parsing anomalies
- publish suspicious revisions automatically
- introduce broad catch-all exception handlers
- bypass tests because the change looks small
- make undocumented architectural changes
