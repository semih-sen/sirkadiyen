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
- [ ] Implement snapshot retention and cleanup

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
- [ ] Provide an operator path for releasing a held diff

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

- [ ] Implement audited global freeze (ADR-034)
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
→ acquire a snapshot per source
→ store through the short circuit
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
operation may be derived from it (ADR-040).

The next step is the consumer side: affected-user resolution and the Google
Calendar adapter, which read `Ready` diffs. A held diff currently stops where it
is, because no operator path exists to release one — safe, but an operator
cannot yet act on the source correction other than by fixing the source.

There is deliberately no rollback (ADR-033). A bad publication is corrected at
the authoritative source and reaches calendars as a newer forward-fix revision.
Before calendar work begins, the runtime-readable global freeze from ADR-034
must gate acquisition, publication, diff dispatch and downstream jobs.

Two things block on decisions already made rather than on discussion:

- snapshot retention and cleanup, which is unimplemented while storage grows
- per-profile declaration of date format, replacing the global day-first
  assumption that would silently misparse a month-first source

The Google source credential is resolved; a service account is configured.
Drive/HTTP acquisition adapters, DOCX conversion, recovery of stale `Running`
parse runs, and the remaining 17 parser profiles are still required.
