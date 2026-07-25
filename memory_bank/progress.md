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

- [~] Define user entity and onboarding states (user, license, profile and Calendar-authorization states complete; sync states pending)
- [x] Define license entity and state transitions
- [x] Define student profile model
- [x] Define supported profile option model
- [x] Define Google connection model
- [x] Define schedule source model
- [x] Define immutable snapshot model
- [x] Define parser request and response contracts
- [x] Define canonical schedule model
- [x] Add all-day canonical schedule items for holidays and semester breaks (ADR-046)
- [x] Add canonical curriculum block with explicit source provenance (ADR-047)
- [x] Add the canonical academic department list with an explicit marker rule (ADR-049)
- [x] Distinguish free-study availability from generic other events (ADR-069)
- [x] Define schedule revision model
- [x] Define semantic diff model
- [x] Define user calendar event mapping
- [ ] Define sync job state machine
- [ ] Define audit event model

## Phase 2: Authentication and licensing

- [x] Implement Google sign-in
- [x] Implement local user creation
- [x] Implement secure session
- [x] Implement admin role authorization
- [x] Implement license generation
- [x] Implement secure license hashing
- [x] Implement license redemption transaction
- [x] Implement license revocation
- [x] Add authentication-adjacent rate limiting
- [x] Add license audit logging
- [x] Add license concurrency tests

## Phase 3: Student onboarding

- [x] Implement dynamic profile schema
- [ ] Implement supported option administration (schema is server-owned code, no admin CRUD yet — ADR-055)
- [x] Implement profile validation
- [~] Implement resumable onboarding (license-required, profile-required, calendar-authorization-required, ready-for-initial-sync, initial-sync-in-progress, active and suspended states complete)
- [x] Implement Calendar permission state
- [x] Implement initial sync request
- [~] Implement user-visible progress state (backend `GET /api/calendar/sync` returns state and mapped-event count; no frontend yet)

## Phase 4: Source inventory and ingestion

- [ ] Add first-year source fixtures
- [x] Add second-year annual and Turkish practice source fixtures (`g2-{tr,en}-annual`, `g2-tr-practice`)
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
- [x] Refuse a numeric time cell that is not a day fraction (parser engine 0.2.0, ADR-073)
- [x] Declare per-profile group-rotation subjects owned by a companion source (ADR-073)
- [x] Add the slot-column rotation reader and a bounded multi-letter cohort run (ADR-074)

## Phase 6: Parser profiles

- [x] First-year Turkish annual
- [x] First-year Turkish practice
- [x] First-year English annual
- [ ] First-year English practice
- [ ] First-year anatomy practice
- [x] Second-year Turkish annual (`grade2_yearly_v1`, ADR-073)
- [x] Second-year Turkish practice (`grade2_practice_v1`, slot-column layout, ADR-074)
- [x] Second-year English annual (same profile, ADR-073)
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
- [x] Refine AudienceOverlap: quarantine same-course duplicates, tolerate parallel offerings (ADR-068)
- [x] Treat source-authored free-study overlaps as non-blocking availability (ADR-069)
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
- [x] Implement Google Calendar client
- [x] Implement calendar creation or selection
- [x] Implement event insert
- [x] Implement event patch (ADR-059)
- [x] Implement confirmed event delete (diff-authorized, ADR-059)
- [x] Implement private extended properties
- [x] Implement durable event mapping
- [x] Implement idempotency keys
- [x] Implement retries and failure classification (transient back-off + `Failed`; dead credential → `NeedsReauthorization`, ADR-059)
- [x] Implement affected-user resolution (per-user initial side + diff-driven fan-out over Ready/Released diffs, ADR-059)
- [x] Implement initial sync
- [x] Implement incremental sync (diff-driven insert/patch/delete dispatcher, ADR-059)
- [x] Implement reconciliation (semantic replay, non-destructive Calendar/ledger inventory,
  orphan-calendar recovery, and multi-worker fence; ADR-060 through ADR-064)
- [x] Add mocked adapter tests
- [x] Add quota-aware batching (initial-sync event budget, diffs-per-cycle admission,
  and ledger-resumable per-diff Calendar mutation budget; ADR-065)

## Phase 10: Administration and operations

- [x] Implement audited global freeze core and pipeline gates (ADR-034, ADR-043)
- [x] Add authenticated freeze/unfreeze administration surface
- [~] License administration (create/revoke/manual activation complete; listing and audit inspection pending)
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

## Phase 11: Consumer frontend

- [x] Scaffold the Next.js App Router project (`web/`, ADR-066)
- [x] Same-origin HTTPS proxy dev topology (no backend CORS/SameSite change)
- [x] CSRF-aware typed API client and session provider
- [x] Google sign-in (GIS ID token)
- [x] License redemption UI
- [x] Academic profile UI (dynamic cohort dimensions from `/api/profile/options`)
- [x] Calendar authorization UI (popup code flow)
- [x] Initial-sync start and progress polling UI
- [x] Onboarding route gating by authoritative backend state
- [x] SuperAdmin routed to admin panel instead of student onboarding (ADR-067)
- [~] Admin/operator interfaces (minimal `/admin`: operational-freeze control and
  SuperAdmin self-activation; source/revision/diff/license/audit surfaces pending)
- [ ] Component system / design system
- [ ] Automated frontend tests
- [ ] Production deployment topology and reverse-proxy config

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

The runtime global freeze is persisted as one authoritative PostgreSQL row with
append-only transition audit. It gates acquisition, parse-run admission and
publication and fails closed when its state cannot be read. The
SuperAdmin-protected API reads it and performs CSRF-protected audited
freeze/unfreeze transitions. Future diff dispatch and every Calendar job must use
the same gate.

Snapshot payload retention now preserves the first snapshot of the source's
active academic year, the latest content, the last ten days of changes, and
parser-recovery inputs while pruning only expired normalized JSON (ADR-044).
Metadata and the entire parse/revision/diff trail remain.

Holidays and semester breaks now publish as all-day items (ADR-046), closing the
last known canonical-model gap. 22 Turkish and 11 English rows that were dropped
for having no times are now canonical records, and
`rows.ignored.noScheduledTimeAndNoClosure` is zero for both sources, so every
untimed dated row they state is accounted for. A row becomes all-day only when it
states a date, a closure title and no times at all: the English source writes its
own semester break as timed rows, and the eve of Republic Day is three real hours
of teaching, so the title alone never decides.

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

The canonical model now has no known gap. The consumer-side identity and
activation foundation is implemented: Google sign-in, secure cookie/CSRF
session, explicit roles, keyed single-use license hashes, transaction-safe
redemption/revocation, append-only license audits and backend-derived onboarding
state (ADR-052, ADR-053). The validated student profile is now implemented too
(ADR-055): a `StudentProfile` aggregate with relational year/class/language and a
JSONB selector document, a server-owned code-defined supported schema and shared
validator, transactional upsert persistence, and a CSRF-protected profile API. The
profile also carries the university student number (Öğrenci Numarası), validated in
three layers — a ten-digit structural invariant in the domain and at the database,
and semantic cross-validation in the application layer that pins the faculty code to
Istanbul Medical Faculty and the program-language digits to the selected program
(ADR-056). Calendar authorization is implemented too (ADR-057): a separate, minimally
scoped offline consent whose one-time code is exchanged server-side for a refresh
token, encrypted at rest with Data Protection and held in a `UserId`-unique
`GoogleCalendarConnection`. The grant is refused unless Google actually returned the
required scope, and it requires an active license and a profile first. Per-user initial sync is
implemented too (ADR-058): a student at `ReadyForInitialSync` starts synchronization, and
the worker creates their dedicated calendar (ADR-024), then writes every currently-published
event that applies to their profile — resolved by academic year, class year, program language
and cohort selectors — with idempotent, resumable Calendar writes marked by private extended
properties and recorded in a `UserCalendarEventMapping` ledger. Onboarding now walks an
activated account from `ProfileRequired` through `CalendarAuthorizationRequired`,
`ReadyForInitialSync` and `InitialSyncInProgress` to `Active`. Diff-driven incremental sync is
now implemented too (ADR-059): a new worker stage dispatches every `Ready`/`Released` diff into
per-user insert/patch/delete operations, driven by a `CalendarDispatchState` on the diff and made
resumable by the same deterministic-id + ledger idempotency as initial sync. The mapping ledger is
the authority for who holds a lesson; a pure `IncrementalSyncPlanner` decides the operation per
user. Transient Google failures back off and retry, then give up to `Failed`; a dead credential
flags the connection `NeedsReauthorization`, skips that user, and leaves their events.
Re-authorization catch-up is implemented (ADR-060). The freeze-gated worker reads only
`Ready`/`Released` diffs already marked `Dispatched` after each user's ordered cursor and
replays them one user at a time. Cursor advancement happens only after a whole diff converges;
an empty scan completes the request. Deletion remains authorized by a replayed semantic diff,
never by current-state absence. Secondary-matched time moves preserve the Google event ID while
atomically moving the ledger identity (ADR-061).

Calendar reconciliation is complete (ADR-062 through ADR-064). The periodic inventory
enumerates marked Google events and compares them with expected current records and the ledger;
it recreates or patches expected state but never turns absence, an unexpected event, or a
duplicate into deletion authority. Initial sync recovers exactly one marker-matched orphan
calendar, and PostgreSQL advisory locking fences dispatch, replay and inventory across workers.

Calendar synchronization and reconciliation hardening is complete through ADR-065, including
intra-diff quota-aware fan-out.

The Grade 2 annual slice is implemented (ADR-073). One profile, `grade2_yearly_v1` 1.0.0,
serves both `G2-TR-ANNUAL` (790 candidates) and `G2-EN-ANNUAL` (935), because the two
workbooks are the Grade 1 row layout with different header wording and term text. It needed
no new canonical field. Two source-driven rules came with it: a subject a profile declares as
a **group rotation** is excluded from the whole-class program — Grade 2 writes one dissection
session as three consecutive daily slots and the anatomy group list assigns each student one
of them — and a **numeric time cell that is not a day fraction** is refused instead of being
reduced modulo one day, which used to publish an English free-study block from midnight.

The Grade 2 Turkish practice slice is implemented too (ADR-074). `grade2_practice_v1` 1.0.0
reads the transpose of the Grade 1 rotation table through a new `parsers/practice_slots.py`:
a column is a dated slot and a row is a practice subject. It publishes 163 candidates for the
eight `A`-`H` cohorts and is predicted to validate with no findings.

The next schedule-source slices are the Grade 2 anatomy and vertical-corridor DOCX sources,
which own the dissection rotation and the 95 skill-practice sessions both Grade 2 profiles now
defer, and the Grade 2 English practice source, whose committed fixture is still from
2024-2025. Before any Grade 2 student can receive these revisions, the supported-profile
schema (ADR-055) must be extended past class year 1.

Calendar backlog scheduling is complete through ADR-070. Ordinary 100-operation
quota yields no longer wait for the adaptive source polling interval: the worker
continues initial sync, incremental dispatch, and reconciliation after a configurable
five-second Calendar-only delay, without re-polling schedule sources.

Grade 1 annual/practice source ownership is corrected through ADR-071. Bare
`UYGULAMA`/`PRACTICE` annual slot placeholders are excluded, while named practice
lessons remain. Profile 1.3.0 reparses retained snapshots and removes already-synced
placeholders through the normal semantic deletion diff.

Google Calendar event presentation is implemented through ADR-072. A shared policy
gives every source-stated department a deterministic calendar-scoped label and custom
RGB color, with fixed requested colors for the core departments, exams, and free
study. Calendar summaries preserve source-authored lesson sequence numbers;
descriptions label instructor, curriculum block, and department fields. Annual profile 1.4.0 omits
amphitheatre-program lookup instructions from canonical locations, while the Calendar
policy suppresses legacy copies defensively. Inventory compares event label IDs, so
existing monochrome events are repairable without direct edits.

The consumer frontend now has a runnable foundation in `web/` (ADR-066). It is a Next.js App
Router + TypeScript project that walks a student through the whole onboarding path — Google
sign-in, license redemption, academic profile, Calendar authorization, and initial-sync
progress — against the existing backend APIs, with the backend staying authoritative for
onboarding state. Local development is same-origin behind an HTTPS proxy edge
(`next.config.mjs` proxies `/api/*` to Kestrel), so the hardened `Secure`/`__Host-`/`SameSite`
cookies are exercised unchanged and no backend CORS or cookie relaxation was introduced. The
only external step for local OAuth testing is adding `https://localhost:3000` to the OAuth
client's Authorized JavaScript origins. Still open on the frontend: a component/design system,
automated tests, the admin/operator interfaces, and the production reverse-proxy topology.

There is deliberately no rollback (ADR-033). A bad publication is corrected at
the authoritative source and reaches calendars as a newer forward-fix revision.
Diff dispatch is now live but stays diff-authorized: a held diff never reaches a calendar, and
deletion requires a published revision and a dispatchable diff (AI_GUIDELINE §13). The
acquisition, parsing, publication and dispatch boundaries are all freeze-gated.

The initial operator model is implemented: one Google-verified SuperAdmin,
`halil.semih.sen@gmail.com`, grants an explicit persisted `role` (ADR-045 as
amended). The shared key is gone; approval and release derive the actor from the
verified session.

The Google source credential is resolved; a service account is configured.
Drive/HTTP acquisition adapters, DOCX conversion and the remaining 17 parser
profiles are still required.
