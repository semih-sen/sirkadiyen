# Progress

## Legend

```text
[ ] Not started
[~] In progress
[x] Completed
[!] Blocked or requires decision
```

## Phase 0: Repository foundation

- [x] Create AI agent guidelines
- [x] Create memory bank structure
- [x] Create source fixture conventions
- [x] Initialize Git repository
- [x] Add root solution and project structure
- [x] Add formatting and editor configuration
- [x] Add `.env.example`
- [x] Load the repository `.env` in the hosts, tools and tests (ADR-041)
- [x] Add Docker Compose development environment
- [ ] Add CI workflow
- [x] Add contribution and local setup documentation

## Phase 1: Domain and contracts

- [ ] Define user entity and onboarding states
- [ ] Define license entity and state transitions
- [ ] Define student profile model
- [ ] Define supported profile option model
- [ ] Define Google connection model
- [x] Define schedule source model
- [x] Define immutable snapshot model
- [x] Define parser request and response contracts
- [x] Define canonical schedule model
- [ ] Add all-day canonical schedule items for holidays and semester breaks (ADR-046)
- [ ] Add canonical curriculum block with explicit source provenance (ADR-047)
- [x] Define schedule revision model
- [x] Define semantic diff model
- [ ] Define user calendar event mapping
- [ ] Define sync job state machine
- [ ] Define audit event model

## Phase 2: Authentication and licensing

- [ ] Implement Google sign-in
- [ ] Implement local user creation
- [ ] Implement secure session
- [ ] Implement admin role authorization
- [ ] Implement license generation
- [ ] Implement secure license hashing
- [ ] Implement license redemption transaction
- [ ] Implement license revocation
- [ ] Add rate limiting
- [ ] Add audit logging
- [ ] Add concurrency tests

## Phase 3: Student onboarding

- [ ] Implement dynamic profile schema
- [ ] Implement supported option administration
- [ ] Implement profile validation
- [ ] Implement resumable onboarding
- [ ] Implement Calendar permission state
- [ ] Implement initial sync request
- [ ] Implement user-visible progress state

## Phase 4: Source inventory and ingestion

- [ ] Add first-year source fixtures
- [ ] Add second-year source fixtures
- [ ] Add third-year source fixtures
- [ ] Add weekly amphitheatre fixtures
- [ ] Document every source
- [x] Add confirmed mixed-transport source catalog
- [x] Implement Google Sheets client
- [x] Implement value acquisition
- [x] Implement merge and metadata acquisition
- [x] Implement normalized snapshot contract
- [x] Implement snapshot hashing
- [x] Implement local XLSX snapshot converter
- [x] Persist immutable snapshots
- [x] Add polling worker
- [x] Add unchanged-source short circuit

## Phase 5: Python parser foundation

- [x] Initialize FastAPI parser service
- [x] Add Pydantic contracts
- [x] Add parser registry
- [x] Add shared cell normalization
- [x] Add merged-cell expansion
- [x] Add date resolver
- [x] Add time resolver
- [x] Add group expression parser
- [x] Add course title normalization
- [x] Add instructor extraction
- [x] Add evidence model
- [x] Add warning model
- [x] Add parser metrics
- [x] Add parser versioning
- [x] Add golden-file test harness
- [x] Add parser profile implementation registry
- [x] Add stable identity and content hashing

## Phase 6: Parser profiles

- [x] First-year Turkish annual
- [x] First-year Turkish practice
- [x] First-year English annual
- [ ] First-year English practice
- [ ] First-year anatomy practice
- [ ] Second-year Turkish annual
- [ ] Second-year Turkish practice
- [ ] Second-year English annual
- [ ] Second-year English practice
- [ ] Second-year anatomy autumn
- [ ] Second-year anatomy spring
- [ ] Second-year vertical corridor
- [ ] Third-year Turkish A annual
- [ ] Third-year Turkish A bedside
- [ ] Third-year Turkish A faculty practice
- [ ] Third-year Turkish B annual
- [ ] Third-year Turkish B bedside
- [ ] Third-year Turkish B faculty practice
- [ ] Third-year English source profiles
- [ ] Weekly amphitheatre enrichment

## Phase 7: Revision and validation pipeline

- [x] Persist parser results
- [x] Implement record validation
- [x] Implement revision validation
- [x] Implement anomaly thresholds
- [x] Implement review-required state
- [x] Implement admin revision review
- [x] Implement transactional publication
- [x] Decide forward-fix policy; no rollback operation (ADR-033)
- [x] Add validation regression tests
- [x] Implement bounded snapshot payload retention and cleanup (ADR-044)

## Phase 8: Semantic diff

- [x] Implement stable identity generation
- [x] Implement content hashing
- [x] Implement exact identity matching
- [x] Implement deterministic secondary matching
- [x] Implement ambiguity quarantine
- [x] Implement created/updated/deleted classification
- [x] Add mass-deletion safety guard (validation rule and diff dispatch gate)
- [x] Add semantic diff test matrix
- [ ] Populate canonical Department in profiles whose source explicitly states it
- [x] Persist semantic diffs
- [x] Calculate and store a diff after publication
- [x] Provide an operator path for releasing a held diff (ADR-042)

## Phase 9: Calendar synchronization

- [x] Decide managed-calendar strategy
- [ ] Implement Google Calendar client
- [ ] Implement calendar creation or selection
- [ ] Implement event insert
- [ ] Implement event patch
- [ ] Implement confirmed event delete
- [ ] Implement private extended properties
- [ ] Implement durable event mapping
- [ ] Implement idempotency keys
- [ ] Implement retries and failure classification
- [ ] Implement affected-user resolution
- [ ] Implement initial sync
- [ ] Implement incremental sync
- [ ] Implement reconciliation
- [ ] Add mocked adapter tests
- [ ] Add quota-aware batching

## Phase 10: Administration and operations

- [x] Implement audited global freeze core and pipeline gates (ADR-034, ADR-043)
- [ ] Add authenticated freeze/unfreeze administration surface
- [ ] License administration
- [ ] Source status dashboard
- [ ] Snapshot inspection
- [ ] Parser warning review
- [ ] Revision diff viewer
- [ ] Manual publish and reject
- [ ] User sync status
- [ ] Retry failed jobs
- [ ] Audit log viewer
- [ ] Health checks
- [ ] Metrics
- [ ] Structured logs
- [ ] Alerts

## Current next action

The schedule pipeline now runs from polling to a stored semantic diff. The
completed Google Sheets path is:

```text
list polling-enabled sources
→ check the runtime global freeze
→ acquire a snapshot per source
→ store through the short circuit
→ re-check the freeze before starting or resuming parsing
→ begin or resume the parse run
→ call the parser over HTTP
→ transactionally create a revision and canonical records
→ validate into Validated, ReviewRequired or Rejected
→ publish every Validated revision, superseding the one it replaces
→ diff every published revision against the one it superseded and store it
```

A quarantined revision joins that path only through
`POST /api/revisions/{id}/approve`, which records who approved it and why.

The diff is calculated after publication in its own transaction, driven by
revision state (ADR-039), and is stored exactly once per revision. It is created
`Ready` or `Held`; ambiguity or a mass deletion holds it and no calendar
operation may be derived from it (ADR-040). A held diff reaches dispatch only
through `POST /api/diffs/{id}/release`, which records who took responsibility
and why — except an ambiguous one, which is only ever fixed at the source
(ADR-042).

The runtime global freeze is now persisted as one authoritative PostgreSQL row
with append-only transition audit. It gates acquisition, parse-run admission and
publication and fails closed when its state cannot be read. The operator-key API
exposes the state read-only at `GET /api/operations/freeze`; authenticated
freeze/unfreeze administration remains deliberately deferred until real
operator identity exists. Future diff dispatch and every Calendar job must use
the same gate.

Snapshot payload retention now preserves the first snapshot of the source's
active academic year, the latest content, the last ten days of changes, and
parser-recovery inputs while pruning only expired normalized JSON (ADR-044).
Metadata and the entire parse/revision/diff trail remain.

The next implementation step is to survey parser profiles for explicitly stated
academic departments and populate canonical `Department` only where the source
provides it. Without that data, historical and current records safely skip
secondary matching. The newly accepted all-day item shape and canonical
`CurriculumBlock` (ADR-046, ADR-047) then close known canonical-model gaps before
the consumer side begins with user/profile modeling and affected-user resolution
over `Ready` and `Released` diffs.

There is deliberately no rollback (ADR-033). A bad publication is corrected at
the authoritative source and reaches calendars as a newer forward-fix revision.
The existing acquisition, parsing and publication boundaries are frozen now;
diff dispatch and downstream jobs do not exist yet and must be gated as they are
introduced.

One parser-safety item blocks on implementation rather than discussion:

- per-profile declaration of date format, replacing the global day-first
  assumption that would silently misparse a month-first source

The initial operator model is also decided: one Google-verified SuperAdmin
email (ADR-045). Its exact address must be supplied before the shared key can be
removed.

The Google source credential is resolved; a service account is configured.
Drive/HTTP acquisition adapters, DOCX conversion, recovery of stale `Running`
parse runs, and the remaining 17 parser profiles are still required.
