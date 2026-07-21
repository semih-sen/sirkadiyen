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
- [ ] Add Docker Compose development environment
- [ ] Add CI workflow
- [x] Add contribution and local setup documentation

## Phase 1: Domain and contracts

- [ ] Define user entity and onboarding states
- [ ] Define license entity and state transitions
- [ ] Define student profile model
- [ ] Define supported profile option model
- [ ] Define Google connection model
- [ ] Define schedule source model
- [ ] Define immutable snapshot model
- [x] Define parser request and response contracts
- [ ] Define canonical schedule model
- [ ] Define schedule revision model
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
- [ ] Implement Google Sheets client
- [ ] Implement value acquisition
- [ ] Implement merge and metadata acquisition
- [x] Implement normalized snapshot contract
- [ ] Implement snapshot hashing
- [ ] Persist immutable snapshots
- [ ] Add polling worker
- [ ] Add unchanged-source short circuit

## Phase 5: Python parser foundation

- [x] Initialize FastAPI parser service
- [x] Add Pydantic contracts
- [x] Add parser registry
- [ ] Add shared cell normalization
- [ ] Add merged-cell expansion
- [ ] Add date resolver
- [ ] Add time resolver
- [ ] Add group expression parser
- [ ] Add course title normalization
- [ ] Add instructor extraction
- [ ] Add evidence model
- [ ] Add warning model
- [ ] Add parser metrics
- [ ] Add parser versioning
- [ ] Add golden-file test harness

## Phase 6: Parser profiles

- [ ] First-year Turkish annual
- [ ] First-year Turkish practice
- [ ] First-year English annual
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

- [ ] Persist parser results
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

Add shared normalization primitives and the golden-file test harness, then
implement the first fixture-backed annual and practice parser profiles.

In parallel with implementation, obtain raw Google Sheets snapshots for the
DOCX-only source references and the fixtures still listed as missing in
`sheets/source-manifest.md`.
