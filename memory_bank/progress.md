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
- [x] Add canonical curriculum block with explicit source provenance (ADR-047)
- [x] Add the canonical academic department list with an explicit marker rule (ADR-049)
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
- [x] Declare the numeric date order per parser profile (ADR-051)

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
- [x] Recover parse runs left running by an abrupt worker shutdown (ADR-050)

## Phase 8: Semantic diff

- [x] Implement stable identity generation
- [x] Implement content hashing
- [x] Implement exact identity matching
- [x] Implement deterministic secondary matching
- [x] Implement ambiguity quarantine
- [x] Implement created/updated/deleted classification
- [x] Add mass-deletion safety guard (validation rule and diff dispatch gate)
- [x] Add semantic diff test matrix
- [x] Populate canonical departments in profiles whose source explicitly states them
- [x] Match a moved lesson without a comparable department (ADR-035 amendment)
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

The day-first numeric date assumption is gone. Each parser profile now declares
its `numeric_date_order`, and an undeclared profile publishes a numeric date only
when both readings agree (ADR-051). No committed fixture writes one: the metrics
report 896 serial and 5 month-name dates in Grade 1 Turkish annual, 953 serial in
Grade 1 English annual, and 60 serial and 100 month-name rotation rows in Grade 1
Turkish practice, so the branch that could have misparsed silently was never
reached by a real source.

Semantic secondary matching now works in production. It previously could not:
`Department` gated it and no parser could populate the field, so every lesson
whose time moved became a delete plus a create. The annual
`DİLİM ADI / ANABİLİM DALI` cell is now split into canonical `CurriculumBlock` and
a `Departments` list under an explicit marker rule (ADR-047, ADR-049), and ADR-035
is amended so a lesson with no comparable department is still matched on title and
instructor against a higher bar. Parse runs left running by a killed worker are
recovered after a timeout instead of wedging their snapshot (ADR-050).

The next implementation step is all-day holidays and semester breaks (ADR-046),
which is the last known canonical-model gap and would publish twenty-two rows
that are currently dropped. After that the consumer side begins with identity, the
`users` table with its explicit `role` column and the ADR-045 SuperAdmin
bootstrap, then affected-user resolution over `Ready` and `Released` diffs.

There is deliberately no rollback (ADR-033). A bad publication is corrected at
the authoritative source and reaches calendars as a newer forward-fix revision.
The existing acquisition, parsing and publication boundaries are frozen now;
diff dispatch and downstream jobs do not exist yet and must be gated as they are
introduced.

The initial operator model is fully decided: one Google-verified SuperAdmin,
`halil.semih.sen@gmail.com`, granted through an explicit `role` column on the
future `users` table (ADR-045 as amended). It is no longer blocked on a decision,
only on Google sign-in existing.

The Google source credential is resolved; a service account is configured.
Drive/HTTP acquisition adapters, DOCX conversion and the remaining 17 parser
profiles are still required.
