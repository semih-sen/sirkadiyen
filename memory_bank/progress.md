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
- [ ] Initialize Git repository
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
- [ ] Define semantic diff model
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
- [ ] Add polling worker
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

- [~] Persist parser results
- [ ] Implement record validation
- [ ] Implement revision validation
- [ ] Implement anomaly thresholds
- [ ] Implement review-required state
- [ ] Implement admin revision review
- [ ] Implement transactional publication
- [ ] Implement revision rollback strategy
- [ ] Add validation regression tests

## Phase 8: Semantic diff

- [ ] Implement stable identity generation
- [ ] Implement content hashing
- [ ] Implement exact identity matching
- [ ] Implement deterministic secondary matching
- [ ] Implement ambiguity quarantine
- [ ] Implement created/updated/deleted classification
- [ ] Add mass-deletion safety guard
- [ ] Add semantic diff test matrix

## Phase 9: Calendar synchronization

- [ ] Decide managed-calendar strategy
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

Three Grade 1 sources now parse from real snapshots with golden-file cover:
901 and 953 annual candidates from the Turkish and English workbooks, and 426
practice candidates from the Turkish rotation matrix. PostgreSQL holds sources,
snapshots, parse runs, revisions and canonical records, and the unchanged-source
short circuit is proved against a real database.

Next compose the worker polling workflow, which is the first thing that joins
the pieces end to end:

```text
list polling-enabled sources
→ acquire a snapshot per source
→ store through the short circuit
→ on change, call the parser over HTTP
→ persist the parse run
→ create a revision and its canonical records
```

Each step exists in isolation. What is missing is the parser HTTP client, the
job that sequences them, and the revision-creation transaction.

In parallel, obtain either an offline source refresh token or a service-account
credential with access to the Sheets sources; without one the workflow can only
run against local fixtures. Drive/HTTP acquisition adapters and DOCX conversion
follow the same transport/format boundary.
