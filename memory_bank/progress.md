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
- [~] Define sync job state machine (per-diff dispatch lifecycle with an operator retry, ADR-097;
  no unified job aggregate)
- [x] Define audit event model (append-only account-access/activity log `AuditEvent`, ADR-089)

## Phase 2: Authentication and licensing

- [x] Implement Google sign-in
- [x] Implement local user creation
- [x] Implement secure persistent session (30-day sliding HTTP-only secure cookie, ADR-091)
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
- [x] Add third-year source fixtures (all eight 2026-2027 documents, `g3-*.snapshot.json`)
- [x] Add weekly amphitheatre fixtures (`shared-amphi.snapshot.json`, the 31 Aug - 4 Sep 2026 workbook)
- [ ] Document every source
- [x] Add confirmed mixed-transport source catalog
- [x] Implement Google Sheets client
- [x] Implement Google Drive client (Drive v3 REST download, verified, DOCX and XLSX — ADR-083)
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
- [x] Declare the numeric date order the Grade 2 practice source writes (ADR-075)
- [x] Convert a Word document onto the normalized snapshot contract (ADR-076)
- [x] Publish the vertical-corridor sessions the other Grade 2 profiles defer (ADR-077)
- [x] Publish the dissection rotation the Grade 2 annual profile defers (ADR-078)

## Phase 6: Parser profiles

- [x] First-year Turkish annual
- [x] First-year Turkish practice
- [x] First-year English annual
- [ ] First-year English practice
- [ ] First-year anatomy practice
- [x] Second-year Turkish annual (`grade2_yearly_v1`, ADR-073)
- [x] Second-year Turkish practice (`grade2_practice_v1`, slot-column layout, ADR-074)
- [x] Second-year English annual (same profile, ADR-073)
- [x] Second-year English practice (`grade2_practice_v1` 1.2.0, ADR-084)
- [x] Second-year anatomy autumn (`grade2_anatomy_autumn_v1`, ADR-078)
- [x] Second-year anatomy spring (`grade2_anatomy_spring_v1`, same implementation)
- [x] Second-year vertical corridor (`grade2_vertical_corridor_v1`, ADR-077)
- [x] Third-year Turkish A annual (`grade3_yearly_v1`, ADR-098/100)
- [x] Third-year Turkish A bedside (`grade3_bedside_v1`, publishes nothing by design, ADR-100)
- [x] Third-year Turkish A faculty practice (`grade3_faculty_practice_v1`, ADR-099)
- [x] Third-year Turkish B annual (same profile)
- [x] Third-year Turkish B bedside (same profile)
- [x] Third-year Turkish B faculty practice (same profile)
- [x] Third-year English annual (same profile; its program states no A/B division, ADR-098)
- [ ] Third-year faculty-practice room lookup (`grade3_faculty_locations_v1` declared, unimplemented)
- [x] Weekly amphitheatre enrichment (`weekly_amphitheatre_v1` reads it, the three annual
  profiles take rooms from it as an ADR-102 companion, ADR-133)
- [x] Weekly document discovery: the Drive folder is the source address and the file is
  resolved per poll (ADR-133)

## Phase 7: Revision and validation pipeline

- [x] Persist parser results
- [x] Implement record validation
- [x] Implement revision validation
- [x] Implement anomaly thresholds
- [x] Refine AudienceOverlap: quarantine same-course duplicates, tolerate parallel offerings (ADR-068)
- [x] Treat source-authored free-study overlaps as non-blocking availability (ADR-069)
- [x] Implement review-required state
- [x] Implement admin revision review
- [x] Implement audited manual rejection of a quarantined revision (ADR-097)
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
- [x] Provide an operator path for retrying a terminally failed diff dispatch (ADR-097)

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
- [x] Admit newly queued Calendar work independently of the adaptive source-polling
  delay, including an initial-sync request created while the worker is idle (ADR-082)
- [x] Add the 45-department faculty catalog, admin color defaults, per-user color
  overrides, audited persistence and inventory-driven recoloring (ADR-086)
- [x] Require an active license before any calendar write, so revocation stops future
  synchronization while preserving what was already written (ADR-095)
- [x] Converge a student's calendar onto a changed academic profile (ADR-096)

## Phase 10: Administration and operations

- [x] Implement audited global and class/program-scoped freeze core and pipeline gates (ADR-034, ADR-043, ADR-091)
- [x] Add authenticated global and scoped freeze/unfreeze administration surface
- [x] Administrative document acquisition surface (endpoint ADR-080, `/admin` UI and
  `GET /api/sources/uploadable` ADR-081)
- [x] Department color administration surface with required audit reason (ADR-086)
- [x] License administration (create/revoke/manual activation; listing + audit inspection
  `GET /api/admin/licenses(+detail)`, ADR-089)
- [x] Source status dashboard backend (`GET /api/admin/sources`, poll status + latest parse
  run/revision, ADR-089)
- [x] Snapshot inspection backend (`GET /api/admin/sources/{id}` recent snapshots, ADR-089)
- [x] Parser warning review (latest persisted parser warnings and source evidence exposed in source detail; revision validation findings remain on `GET /api/revisions/{id}`)
- [x] Revision diff viewer (`/admin/diffs` renders each actionable entry's previous/current lesson)
- [~] Manual publish and reject (reject implemented end to end — `POST /api/revisions/{id}/reject`
  plus its review-queue UI, ADR-097; manual publish of a validated revision is still the worker's
  job only)
- [x] User sync status backend (`GET /api/admin/users(+detail)`: profile, licenses,
  managed-event count, onboarding state, recent sign-ins, ADR-089)
- [x] Account directory filtering and sorting (`GET /api/admin/users` accepts licence state,
  profile presence, academic year, class year, program language, `selector=key:value` pairs,
  Calendar presence/status/initial-sync state, created and last-signed-in ranges, `sort`; search
  matches e-mail, display name and student-number prefix; ADR-108)
- [x] Per-account operator page (`/admin/users/{id}`: profile, licences, Calendar connection,
  onboarding, the user's audit trail, what the mapping ledger says is on their managed calendar
  via `GET /api/admin/users/{id}/calendar-events(+changes)`, manual activation, licence revocation
  and a warning composed in place; ADR-108)
- [x] Account deletion (self-service `POST /api/account/delete` and operator
  `POST /api/admin/users/{id}/delete`; personal data erased via cascade, cross-cutting audit trail
  anonymized, licences detached, managed Google calendar deleted + token revoked best-effort,
  `AuditEventCategory.AccountDeleted`, SuperAdmin refused; ADR-118)
- [x] Administrative role change (`POST /api/admin/users/{id}/role`; promote to / demote from
  SuperAdmin, audited `RoleChanged`, guarded against self-change and demoting the bootstrap operator;
  per-account "Yetki (rol)" card; ADR-119)
- [x] Worker-instance monitoring (ADR-124): each worker instance upserts a heartbeat (`worker_instances`,
  stage/uptime/last-seen) with 1-day auto-retention; `GET /api/admin/workers` + "Worker instance'ları"
  panel list all live instances and warn when more than one is active — the observability gap behind the
  ADR-122 incident. Supersedes ADR-091's no-heartbeat clause only.
- [x] Operator recovery tools (ADR-127): four levers a SuperAdmin lacked when the diff/revision
  machinery stalled. (1) **Discard a held diff** — terminal `ScheduleDiffState.Discarded`,
  `POST /api/diffs/{id}/discard`, "Reddet" on the held queue; forward-fix preserved, ambiguous holds
  discardable. (2) **Manual re-poll with force** — `POST /api/admin/sources/{sourceId}/poll` enqueues a
  `source_poll_requests` row the worker drains every cycle (`ManualSourcePollTask`, `FOR UPDATE SKIP
  LOCKED`); force salts the parse-run fingerprint so an unchanged snapshot+profile reparses; "Şimdi
  çek"/"Force ile çek" on the source detail. (3) **History views** — `GET /api/diffs/recent` and
  `/api/revisions/recent` (any state, newest first, optional `sourceId`) behind "Geçmiş" tabs. (4)
  **Next-poll visibility** — worker publishes `NextSourcePollAtUtc` on its heartbeat, shown per instance
  on the server-status screen. One migration adds the discard columns, the poll-request table and the
  heartbeat column.
- [x] Initial-sync concurrency fix (ADR-122): initial calendar sync now runs inside the shared
  cross-instance advisory fence (was unfenced), closing the two-worker race that split a user's events
  across two calendars — the root cause of a live "500 events missing on Google" incident
- [x] On-demand non-destructive calendar repair (`POST /api/admin/users/{id}/calendar-repair`; queues the
  worker's fenced inventory pass to re-insert missing events + patch drift, never deletes; "Onar" button
  on the verification result; ADR-123). Destructive previous-year surplus cleanup still deferred.
- [x] On-demand read-only calendar verification against Google (`GET /api/admin/users/{id}/calendar-verify`;
  reads the actual Google calendar and compares it with the mapping ledger and current published truth,
  reporting MissingOnGoogle/ContentDrift/ExtraOnGoogle/Duplicate/StaleLedger; writes nothing, not even
  connection health; synchronous API read per the ADR-118 precedent; "Google ile doğrula" card in the
  user calendar tab; ADR-121)
- [x] Operator-triggered snapshot payload prune (`POST /api/admin/sources/snapshots/{id}/prune-payload`;
  reclaims a chosen old snapshot's payload on demand while keeping its immutable identity and the whole
  parse/revision/diff trail; refuses the newest, the year baseline, recovery-needed and frozen scopes
  with a reason; audited `SnapshotPayloadPruned`; per-snapshot "Payload'ı buda" control; ADR-120)
- [ ] Operator-authored academic profile edit (no backend write exists; a wrong cohort is still
  fixable only by the student)
- [~] Retry failed jobs (`POST /api/diffs/{id}/retry` plus `GET /api/diffs?dispatchState=Failed`
  and their `/admin/diffs` queue, ADR-097; a persistently failing per-user initial sync still has
  no terminal state to retry from)
- [x] Audit log viewer backend (`GET /api/admin/audit`, `GET /api/admin/access-logs` with
  masked IP + audited unmask, ADR-089)
- [x] Audit a student profile change (`AuditEventCategory.ProfileUpdated` with the resolved
  audience and both outcome flags, never the student number; ADR-105)
- [x] Administrator calendar announcements — bulk cohort event and single-user warning as one
  domain (`/api/admin/announcements/*`, server-resolved audience with exclusion reasons, binding
  plan hash, deterministic campaign/warning key, per-recipient delivery ledger, freeze-gated worker
  delivery, audited edit and cancellation; ADR-107)
- [x] Health checks (API and internal Worker `/health/live` + `/health/ready`, parser `/health` probe, ADR-089/091)
- [x] Metrics (`GET /api/admin/metrics` JSON operational-count snapshot, ADR-089)
- [~] Structured logs (correlation-id middleware stamps every request/log line; a full
  structured-logging/OpenTelemetry stack is still pending)
- [~] Alerts (ADR-144: the worker sends Telegram alerts for a created revision, a calculated or
  held diff, an unreadable source, a discovery fallback, a recovered parse run, an unvalidatable
  revision, a failing stage, and the ADR-143 stall report — severity floor and repeat cooldown
  configurable, off entirely without `SIRKADIYEN_TELEGRAM__BOT_TOKEN`. Still not alerted on:
  `ScheduleDiff.DispatchRetryCount` before it gives up, and an announcement that reached its
  delivery attempt cap; both remain readable in the UI only. Terminally failed dispatches do
  reach the channel, through the stall report.)

## Phase 11: Consumer frontend

- [x] Scaffold the Next.js App Router project (`web/`, ADR-066)
- [x] Same-origin HTTPS proxy dev topology (no backend CORS/SameSite change)
- [x] CSRF-aware typed API client and session provider
- [x] Google sign-in (GIS ID token)
- [x] License redemption UI
- [x] Academic profile UI (dynamic cohort dimensions from `/api/profile/options`)
- [x] Academic profile **edit** surface for a student past onboarding (`/profile`, shared
  `AcademicProfileForm`, honest `calendarResyncRequested` reporting, ADR-105)
- [x] Calendar authorization UI (popup code flow)
- [x] Initial-sync start and progress polling UI
- [x] Onboarding route gating by authoritative backend state
- [x] SuperAdmin routed to admin panel instead of student onboarding (ADR-067)
- [~] Admin/operator interfaces (freeze including class/program scopes, source warning
  evidence, the filterable account directory with its per-account operator page (ADR-108),
  license administration, audit/access logs, worker/parser health, the held/failed diff queues
  with revision rejection, and the bulk-event / user-warning announcement workspaces are wired;
  only endpointless product domains remain — contact, notifications and per-user sync history)
- [x] Administrative document upload UI, driven by `GET /api/sources/uploadable` (ADR-081)
- [x] Component system / design system (ported the Wise-inspired prototype design
  system into `web/src/app/globals.css` + shared `web/src/components/ui.tsx`; light
  theme, tokens, two densities; Tailwind deliberately not added)
- [x] Public + legal surface (landing `/`, `/gizlilik`, `/kosullar`, `/iletisim`)
  and re-skinned onboarding/dashboard/admin against the design system
- [x] Automated frontend tests (Vitest + React Testing Library, ADR-090)
- [x] Student dashboard UX polish (2026-08-22): "Sıradaki dersler" and "Son program
  değişiklikleri" are collapsible cards; the ledger-count "Senkronizasyon görünümü" card is
  replaced by a student-useful "Takvim özeti" (next lesson, 14-day count, total, relative last
  update, Calendar badge); the initial-sync page gained a real progress bar (indeterminate sweep
  while running, 100% only on backend Completed) replacing the frozen fake 99%
- [ ] Production deployment topology and reverse-proxy config

## Current next action

The schedule pipeline now runs from polling to a stored semantic diff. The
completed path, for a source published as a sheet or as a Drive document, is:

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

The Grade 2 Turkish and English practice slices are implemented (ADR-074, ADR-084).
`grade2_practice_v1` 1.2.0 reads the transpose of the Grade 1 rotation table through
`parsers/practice_slots.py`: a column is a dated slot and a row is a practice subject.
The Turkish source still publishes 164 candidates for the eight `A`-`H` cohorts. The
English source publishes 49 candidates from 17 September 2025 through 22 May 2026 for
the independent `İ1`/`İ2` practice groups. Its workbook filename says 2024-2025, but
the schedule content is 2025-2026; its sole 2024 cell is an anatomy date in a row this
profile already defers to the anatomy source. The profile is also the only one that
declares a numeric date order — `dayFirst`, read off the Turkish annual workbook's
serial for the same session rather than off a writing convention (ADR-075).

DOCX conversion exists (ADR-076). A Word document is converted onto the same normalized
snapshot contract as a workbook — a table becomes a worksheet, a run of paragraphs between
tables becomes a single-column worksheet, and merges and line structure survive — so a
parser profile never learns which format its source was published in. The four Grade 2
Word documents are converted and committed as fixtures. The two families needed different
transports, which is why they were separate work: the anatomy documents are handed out once
a semester and unchanged afterwards, which suits an administrative upload, while Student
Affairs edits the vertical-corridor documents during the year, which needs a re-read. Both
transports now exist (ADR-080, ADR-083).

The vertical-corridor slice is implemented (ADR-077). `grade2_vertical_corridor_v1` 1.0.0
reads both Word documents and publishes 42 sessions that previously reached no calendar,
selecting students by the `A`-`H` practice group they already have rather than by a third
grouping. It is **not** the whole programme: the practice table marks 95 slots and the
faculty has scheduled a fraction of them, so re-acquisition rather than a parser change
publishes the rest. Nine dated rows contradict their own weekday and are refused; three of
them carry groups, so four cohort-sessions wait on a source correction.

The anatomy slice is implemented (ADR-078), which gives **every Grade 2 source a parser
profile**. One implementation serves both semesters and publishes 156 dissection sessions,
90 in autumn and 66 in spring, one per anatomy group per teaching day — the rotation
ADR-073 predicted from the annual program and could not prove from it. A day is recognized
as a run of hours stating exactly one date, because the same document writes a day both as
a vertical merge and as a date typed into the middle of three rows.

Grade 2 Turkish onboarding is open (ADR-079). The two anatomy documents are catalogued
under a new `administrativeUpload` transport: a source that is handed out rather than
published names itself `urn:sirkadiyen:upload:{sourceId}` instead of claiming a location,
and the catalog refuses any other URI for it. With their `anatomyGroup` `1`/`2`/`3`
declared, Grade 2 Turkish enters the supported-profile schema (version `1.1`) with
`practiceGroup`, `practiceSubgroup` and the independent `anatomyGroup`, all three required.
Grade 2 English still stays out of onboarding, but no longer because its practice
fixture lacks current-year evidence. The practice source now declares `İ1`/`İ2`.
Before admission, the group-labelled `İ1`-`İ5` rows in the annual source need safe
audience handling and the shared vertical-corridor document needs an English source
path; otherwise the calendar would be over-broad or incomplete (ADR-084).

Administrative acquisition is implemented (ADR-080). A SuperAdmin uploads a handed-out
document to `POST /api/sources/{sourceId}/document`; it is converted and stored as
immutable evidence, and the worker parses it on its next cycle under the same rules as a
polled source. Sources whose document is literally the same file declare a
`sharedDocumentGroup`, so **one upload serves every program the document serves**: the
anatomy pair gained English counterparts, and one upload now produces a Turkish and an
English snapshot from a single file. Every upload is audited per target with the uploader,
the file name, the byte count and the digest of the bytes.

That upload now has an operator surface (ADR-081). `/admin` carries a document-upload
module driven by `GET /api/sources/uploadable`, which projects the catalog entries whose
transport is `administrativeUpload` — so the frontend asks which sources accept a document
rather than restating a list that changes at rollover. The panel groups those entries by
`sharedDocumentGroup`, so **one handed-out document is one choice** naming every program it
covers, reports each target's `Stored`/`Unchanged` outcome, merges every member's upload
audit trail so an interrupted fan-out is visible, and says the document was stored as
evidence rather than published: parsing, validation and publication remain the worker's
next cycle and the review thresholds' decision.

The Grade 2 anatomy program is shared between the Turkish and English tracks, with the same
`1`/`2`/`3` groups. It is still modelled as one source per program because
`CalendarAudienceResolver` matches a record to a student only when the program languages are
equal; the faithful single-source model is deferred until Grade 2 English enters the
supported-profile schema (ADR-081 amendment).

Google Drive acquisition is implemented (ADR-083), which gives **every Grade 2 Turkish
source a way to be acquired**. `GoogleDriveHttpClient` downloads a catalogued Drive file
over the Drive v3 REST API with the shared read-only source credential, and refuses one
that is trashed, is not the declared format, exceeds the 8 MB bound, or does not match the
length, digest or container Drive stated for it, rather than converting a bad acquisition
into a snapshot. The snapshot records only that it was downloaded: acquisition diagnostics
are part of the content hash, so a name or a modification time recorded as provenance would
make an unedited re-save look like a change. Drive metadata is therefore not a change
signal; the converted content hash is. A poll separates `UnsupportedTransport` from
`UnsupportedDocumentFormat`; since Drive learned to read XLSX (2026-08-15) the only
source reporting either is `SHARED-AMPHI`, which waits on an HTTP adapter.

Every Grade 2 source now has a parser profile, including the verified English practice
source. Grade 2 English itself is not yet admitted to the supported-profile schema:
the annual program embeds `İ1`-`İ5` in titles without canonical audiences, and the
shared vertical-corridor document has only Turkish catalog entries/parser audience
handling. The English anatomy revisions therefore still publish to an empty audience
until those two audience gaps are closed (ADR-084).

Calendar scheduling is complete through ADR-082. Ordinary 100-operation quota yields
and work queued after an otherwise empty pass no longer wait for the adaptive source
polling interval: the worker checks initial sync, incremental dispatch, and
reconciliation after a configurable five-second Calendar-only delay, without
re-polling schedule sources or drifting the retained source deadline.

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

ADR-086 makes department colors configurable without making department identity
client-controlled. A reviewed catalog contains all 45 faculty departments and their
known Turkish/English variations. The effective color order is personal override,
administrator default, then system default; both admin and user panels expose it.
Mutations are audited and mark completed calendars due for ordinary non-destructive
inventory, which updates label definitions and repairs visible events.

The consumer frontend now has a runnable foundation in `web/` (ADR-066). It is a Next.js App
Router + TypeScript project that walks a student through the whole onboarding path — Google
sign-in, license redemption, academic profile, Calendar authorization, and initial-sync
progress — against the existing backend APIs, with the backend staying authoritative for
onboarding state. Local development is same-origin behind an HTTPS proxy edge
(`next.config.mjs` proxies `/api/*` to Kestrel), so the hardened `Secure`/`__Host-`/`SameSite`
cookies are exercised unchanged and no backend CORS or cookie relaxation was introduced. The
only external step for local OAuth testing is adding `https://localhost:3000` to the OAuth
client's Authorized JavaScript origins. The admin panel covers the operational freeze, the
revision review queue and administrative document upload. Still open on the frontend: a
component/design system, automated tests, the remaining operator surfaces (source status,
diff release, license administration, audit inspection), and the production reverse-proxy
topology.

There is deliberately no rollback (ADR-033). A bad publication is corrected at
the authoritative source and reaches calendars as a newer forward-fix revision.
Diff dispatch is now live but stays diff-authorized: a held diff never reaches a calendar, and
deletion requires a published revision and a dispatchable diff (AI_GUIDELINE §13). The
acquisition, parsing, publication and dispatch boundaries are all freeze-gated.

The initial operator model is implemented: one Google-verified SuperAdmin,
`halil.semih.sen@gmail.com`, grants an explicit persisted `role` (ADR-045 as
amended). The shared key is gone; approval and release derive the actor from the
verified session.

The Google source credential is resolved; a service account is configured. It now
carries the Drive read-only scope beside the Sheets one, so the Cloud project needs
the Drive API enabled and the vertical-corridor documents shared with the account
(ADR-083). DOCX conversion, administrative DOCX acquisition, Drive DOCX acquisition
and — since 2026-08-15 — Drive XLSX acquisition are implemented. An HTTP acquisition
adapter is still required, and `SHARED-AMPHI` is the only source waiting on it.

ADR-087 separates the admin information architecture into dedicated routes; the
overview no longer embeds operational forms. Live source, revision, color, freeze and
license operations are connected to backend-authoritative APIs, while missing-domain
routes render honest empty states. The department palette is now searchable,
filterable, previewable and uses an explicit save action.

The department-color persistence regression found after the first live admin mutation
is fixed. Both color scopes use the shared retriable-transaction wrapper required by
the hosts' Npgsql retry strategy, and a production-like PostgreSQL test prevents the
manual-transaction failure from returning.

ADR-088 extends the runtime palette to two bounded event categories without a schema
change. Integrated sessions now share one configurable label/color regardless of their
department combination. All application types, including vertical-corridor activities
and dissections, share a configurable attention color; visible titles use the
`UYGULAMA - ...` and `DİSEKSİYON` presentation rules. The canonical schedule remains
source-faithful, and ordinary inventory patches existing Calendar events in place.

## Latest full-stack session (2026-08-17, calendar announcements)

- **The last two backendless prototype screens are built** (ADR-107): the bulk calendar event and
  the single-user warning, as one `CalendarAnnouncement` domain behind two React workspaces. The
  recipient set is the only thing that differed between them, so a second domain would have
  duplicated the idempotent write, the delivery ledger, the freeze gate and the cancel path.
- Domain (`CalendarAnnouncement`, `CalendarAnnouncementDelivery`, `AnnouncementCampaignKey`) →
  persistence (`AddCalendarAnnouncements`, two tables, a unique campaign-key index and two check
  constraints) → API (`/api/admin/announcements/*`, SuperAdmin, CSRF, three new audit categories)
  → worker (`AnnouncementDispatchTask`, last inside the shared Calendar fence) → frontend
  (`BulkEventComposer`, `UserWarningComposer`, `AnnouncementShared`).
- **Inventory had to learn about the new kind.** An announcement is Sirkadiyen-marked but has no
  stable identity, so the three-way inventory scan would have reported every one as an unexpected
  marked event on every pass. Announcement events carry `sirkadiyenKind=announcement` and are
  skipped; lessons keep no marker, so nothing already written had to change.
- **`ManagedCalendarEvent` gained a nullable reminder**, left null by every lesson so students'
  own notification defaults keep working.
- **`ICalendarConnectionHealthWriter`** was split out of `ICalendarSyncConnectionStore` so a
  service that only writes calendars depends on two members rather than fourteen.
- 614 Infrastructure unit tests (up from 581), 249 Persistence tests against real PostgreSQL (up
  from 240), 8 Api, 6 Contracts, 34 frontend tests (up from 27). Format clean, Release build 0
  warnings, frontend typecheck clean, production build 27 routes.
- **Not done:** per-recipient retry of one failed delivery, scheduled (future-dated) delivery as
  opposed to a future event date, recurring announcements, and alerting on an announcement that
  reached its attempt cap.

## Latest frontend session (2026-08-04, ADR-089 API integration)

- The active student dashboard now uses schedule upcoming/changes, ledger-backed sync
  progress, license status and audited reconciliation endpoints. Unavailable history and
  notification domains remain explicit placeholders.
- Admin users/licenses, source status, access/audit and server health workspaces now read
  the authoritative paged/detail contracts. Full IP addresses remain masked until a
  reason-required audited action and are retained only in component state.
- The Next same-origin edge proxies `/health/*`; server monitoring shows only liveness,
  readiness and the database-backed metric snapshot.
- Vitest + React Testing Library cover the request client, dashboard semantics, IP reveal
  guard, source read-only behavior and honest metrics rendering (ADR-090).
- Remaining frontend gaps are sync history, notifications, contact, held-diff release,
  bulk event and user warning. Finance moved from "no endpoint" to "endpoint exists, UI not
  built" once the backend below landed (`web/GAPS.md`).

## Latest refactor session (2026-08-04, Worker composition)

- The 324-line Worker startup and 807-line background service were decomposed into a small
  composition root, a lifecycle/scheduling host and focused pipeline tasks (ADR-092).
- Source processing and Calendar maintenance keep the same execution order and failure
  isolation. The existing shared Calendar fence still spans dispatch, replay and inventory.
- Worker configuration retains every existing environment key, fallback and validation step;
  regression tests cover default and configured values.
- The Worker project now uses namespace-aligned feature folders (`Composition`, `Configuration`,
  `Health`, `Scheduling`, `Sources`, `Calendars`) rather than a flat source directory.

## Latest backend session (2026-08-05, Finance module)

- **The finance module backend is implemented** (ADR-093) — the last "new product domain",
  per the sequencing this file previously recorded above. Built and verified phase by phase
  against a real PostgreSQL database, in the order: domain → persistence/ledger → transaction
  API → obligations → summary/trend reporting → profit distribution → CSV export.
- An audited, editable cash ledger (`FinanceAccountHolder`/`FinanceAccount`/`FinanceTransaction`/
  `FinanceLedgerEntry`) derives every balance from ledger entries rather than a stored column.
  Transactions can be edited (rewriting the whole posting) or hard-deleted; the module's own
  append-only `finance_audits` log — not the cross-cutting `AuditEvent` table — captures a full
  before/after image, including entries, in the same commit as the change.
  `POST /api/admin/finance/transactions/{id}/delete` also raises a cross-cutting
  `AuditEventCategory.FinanceTransactionDeleted` event, since a hard delete of money data is
  high-risk enough to appear in the access/activity log too.
- Obligations (`FinanceObligation`/`FinanceSettlement`) track receivables and debts as an accrual
  layer beside the ledger: settling one writes an ordinary Income/Expense transaction plus a
  settlement link, so double counting is structurally impossible rather than merely avoided by
  convention.
- `GET /api/admin/finance/summary` answers all ten period figures (carried-over, income,
  expenses, balance, current cash, to-be-carried-over, receivables, collections, debts,
  payments) plus category totals; `GET /api/admin/finance/trend` adds a monthly series. Current
  cash balance is deliberately never clamped to the reporting period, so a past or future period
  is labeled honestly rather than silently relabeling a historical figure as "current".
- Profit distribution (`FinanceDistribution`/`FinanceDistributionShare`) follows the six-step
  high-risk pattern from the design plan: scope, server-computed preview with a binding SHA-256
  plan hash and typed partner exclusions, confirmation-phrase execution that recomputes and
  compares the plan rather than trusting the client's copy, and reversal. Allocation uses
  largest-remainder rounding in integer minor units (`ProfitShareAllocator`, pure, no I/O) so
  partner payouts always sum exactly to the distributable amount. Non-repeatability per period
  and idempotency per confirmation token are both enforced by unique database indexes.
- Three migrations: `AddFinanceLedger`, `AddFinanceObligations`, `AddFinanceDistributions` (the
  last also adds the FK from `finance_transactions.FinanceDistributionId`, deferred from the
  first migration because that table did not exist yet).
- 742 `.NET` tests pass across Infrastructure unit, Contracts unit and Persistence integration
  suites, all verified against a real PostgreSQL database in this session (not just compiled):
  constraint tests proving each check constraint bites via raw SQL, whole-table integrity
  sweeps, and concurrency tests for competing transfers, edits, deletes, settlements, and
  distribution executions.
- **Backend only, as scoped.** `web/src/app/admin/finance/page.tsx` remains the
  `AdminUnavailable` placeholder; see `activeContext.md` for the open risks (no period close,
  manually-entered license-sales income, partner-share-sum enforcement not yet a database
  constraint) carried forward from this session.

## Latest frontend session (2026-08-05, Finance administration)

- `/admin/finance` is now fully wired to the ADR-093 backend. Its six workspaces cover the ten
  summary figures and trend/category reporting, filtered/paged transaction CRUD and CSV export,
  receivable/payable lifecycle, account/holder/share management, binding preview/execute/reverse
  profit distribution and finance-audit inspection.
- The prototype's document upload was intentionally not ported because the backend stores a text
  reference rather than an attachment. All displayed amounts and outcomes come from authoritative
  API responses; client code validates input shape but does not derive balances or allocations.
- `FinanceObligationListItem.Settlements` is an additive detail read-model field. The persistence
  read joins each settlement to its ordinary cash transaction for the reference; paged obligation
  lists keep the collection empty to avoid loading unused detail data.
- Four focused frontend regressions cover summary/trend/category rendering, income creation,
  historical settlement-link cancellation and exact distribution preview binding. A test exposed
  and fixed modal focus being reapplied on every render.
- Verification: 15/15 frontend tests, TypeScript typecheck, Next.js production build and 748/748
  .NET tests pass, including 217 PostgreSQL persistence tests.

## Latest backend session (2026-08-05, sync gating and operator recovery)

Four gaps found by an audit of the backend against the memory bank were closed. Each was a case
where the documentation described behaviour the code did not have, or a state the pipeline could
enter and never leave.

- **License revocation now stops synchronization** (ADR-095). ADR-022 and `systemPatterns` Â§13 both
  said it did; nothing observed the transition. All four queries that select users for Calendar work
  â€” cohort fan-out, ledger-holder targets, periodic inventory, initial sync and re-authorization
  replay â€” now require an active license, expressed once in `ActiveLicenseQuery`. It gates future
  work only: no event is deleted and the ledger is untouched, so a revoked student keeps the calendar
  they had, and restoring access re-admits them on the next cycle with no sweep.
- **A profile change now re-synchronizes the calendar** (ADR-096). `StudentProfileService.SaveAsync`
  previously persisted the row and stopped, so a student who corrected their practice or anatomy
  group kept the old cohort's events forever and gained the new cohort's only by accident: initial
  sync runs once, diff dispatch is edge-triggered by a revision, and inventory never deletes. A
  profile write that changes the resolved audience now records
  `ProfileResyncRequiredSinceUtc` on the connection **in the same transaction**, and a new
  freeze-gated, fenced worker stage (`ProfileChangeResyncService`) inserts what now applies and
  removes what no longer does.
  - Its deletions are bounded by publication: a ledger row is removed only when its
    `(SourceId, StableIdentity)` is still in the currently published schedule *and* the audience rule
    says it is not this student's. A lesson absent from published truth is left completely alone â€”
    that remains the semantic diff's decision (AI_GUIDELINE Â§13).
  - The stable identity is the join key throughout, because a mapping's `CanonicalRecordId` points at
    whichever revision wrote the event and an `Unchanged` diff entry never advances it.
  - Bounded per cycle, resumable from the ledger, and completed only by a clean pass presenting the
    original request timestamp as an optimistic workflow token, so a second change made mid-pass
    survives.
- **A quarantined revision can be rejected** (ADR-097). `ReviewRequired` had exactly one exit â€”
  approve â€” so an operator who concluded the parse was wrong could only leave it in the queue,
  indistinguishable from one nobody had read. `Reject` records `RejectedBy`/`RejectionReason`/
  `RejectedAtUtc` (never the approval fields) and moves it to the terminal `Rejected` state.
  `POST /api/revisions/{id}/reject`.
- **A terminally failed diff can be retried** (ADR-097). `CalendarDispatchState.Failed` is terminal
  by design so a broken diff stops churning, but nothing could move it out, and the failed queue was
  not even enumerable â€” a failed diff is still `Ready` in its review state. `GET /api/diffs` now
  accepts `dispatchState`, the summary carries the dispatch fields, and
  `POST /api/diffs/{id}/retry` returns the diff to `Pending` with fresh attempts while counting the
  retry and naming who made it. Retry grants no new authority: the same idempotent, ledger-resumable
  fan-out re-runs.

- Migration `AddProfileResyncRevisionRejectionAndDiffRetry` adds seven columns, one index and two
  check constraints; no existing column changes meaning.
- 812 `.NET` tests pass, 0 skipped, across Contracts (6), Api (5), Infrastructure (564) and
  Persistence (237) â€” the last against the real PostgreSQL database. 64 are new. `dotnet format
  --verify-no-changes` is clean and the Release build has no warnings.
- **Backend only, as scoped.** Neither operator route has a UI yet; `web/GAPS.md` records both under
  "endpoint exists, UI not built". A profile change is still not written to the cross-cutting
  `AuditEvent` log, so the trail for a resync deletion is the ledger and the worker log.


## Grade 3 is parsed and onboardable (2026-08-15)

Grade 3 was the last catalogued class year with no parser. Three profiles are now
implemented and registered, all eight 2026-2027 documents are committed and snapshotted, and
a Grade 3 student can declare a profile.

- **`grade3_yearly_v1`** reuses `annual.py` behind three profile-gated changes: an unlabelled
  term column, a term cell that states its class year twice (`Dönem 3A+3B Grubu`), and
  `curriculumGroup` audiences. Grade 1 and 2 goldens are byte-identical, which was verified
  rather than assumed. It publishes all 92 bedside sessions with the times the annual states,
  and excludes the 64 `Öğretim üyesi Uygulama` rotation rows under ADR-073.
- **`grade3_faculty_practice_v1`** reads the eight-block rotation matrix. The hyphen enumerates
  (settled against the data: 127 of 128 rows state each cohort exactly once under that reading,
  and zero rows require the alternative), and a contradictory row is refused **per cohort**, not
  whole — the one faulty row publishes six cohorts, refuses two ambiguous cells and records one
  cohort absent (ADR-099).
- **`grade3_bedside_v1`** publishes nothing by design. It is the reader the annual profile calls
  for practice topics, and it is registered and golden-tested so the document is accounted for.
- **`grade3_faculty_locations_v1`** is declared but unimplemented, so the room lookup returns
  501 instead of being dispatched to the matrix parser. The room join itself is still unbuilt.
- **Bedside topics reach the event description.** A canonical record now carries free-text
  `notes`, part of the content hash and of no stable identity (ADR-101), rendered as a trailing
  `Konu:` paragraph. 88 of 92 topics resolve for the A group and 87 of 92 for B; the rest are
  genuine gaps in the documents' own catalogues.
- **A parse may read companion snapshots** (ADR-102). A source names its companions in the
  catalog, the poller attaches each companion's latest stored snapshot, and the companion set is
  part of the parse run's identity via a `CompanionFingerprint` — without which editing the
  bedside document alone would leave the annual short-circuited as already parsed. A companion
  that has never been acquired is simply absent: the annual publishes with no topic line rather
  than waiting.
- **Each supported program states its own academic year** (ADR-103, schema 1.2). The Grade 3
  documents are 2026-2027 while Grades 1 and 2 are still 2025-2026, and audience resolution
  matches a record to a student on that year, so one schema-wide year would have given every
  Grade 3 student an empty calendar with no fault reported anywhere.
- **Two .NET fixes were prerequisites.** `tools/Sirkadiyen.SnapshotTool` did not compile, and
  the XLSX number-format classifier read the `[$-F800]` *locale* prefix as a currency, silently
  turning one date row into text and dropping eight sessions from an otherwise complete
  schedule.
- Migration `AddCompanionSourceEvidence` adds `schedule_sources.CompanionSourceIds` (jsonb,
  default `[]`) and `parse_runs.CompanionFingerprint`, and widens the parse-run uniqueness key
  to include it. Migration `AddCanonicalScheduleRecordNotes` adds one nullable text column.
- 827 `.NET` tests pass (Contracts 6, Api 5, Infrastructure 577, Persistence 239 against the
  real PostgreSQL database), 487 Python tests pass, ruff and mypy are clean over 56 files, and
  the solution builds with 0 warnings. Eight new golden files cover every Grade 3 source, plus
  one extra case covering the A annual **without** its companion.
- **Not verified:** no live Drive acquisition was run, because no Google source credential is
  configured in this environment. The XLSX download path is covered by unit tests only.

## The three operator UIs are wired (2026-08-15)

Every backend-supported operator action now has a surface. This closes the whole of
`web/GAPS.md` §3.2 — the "endpoint exists, UI not built" category is empty — and with it the
class of problem ADR-097 opened the backend half of: a state the pipeline can enter with no
way out.

- **`/admin/diffs` is a new route carrying two queues as separate tabs.** *Bekletilen diff'ler*
  reads `GET /api/diffs/?state=Held` and releases (ADR-042); *Başarısız dağıtım* reads
  `GET /api/diffs/?dispatchState=Failed` and retries (ADR-097). They are deliberately not one
  merged list: the axes are orthogonal, a terminally failed diff is still `Ready`/`Released`, and
  merging would hide that a *released* diff can still fail its fan-out.
- **A refusal is stated, never rendered as a disabled button.** An ambiguity hold
  (`isReleasable` false) replaces the reason field and the action entirely with the explanation
  that releasing it would leave the previous lesson in every affected calendar and never write
  its replacement — the source has to say which lesson is which. Same shape for a dispatch state
  that is not terminally failed.
- **The changed lessons are shown before either action is offered.** A row expands into
  `GET /api/diffs/{id}` and lists each actionable entry's previous and current lesson, saying how
  many of `actionableEntryCount` are displayed. Releasing without seeing which lessons disappear
  is what the hold exists to prevent.
- **The retry count is surfaced beside the failure reason,** since a diff retried repeatedly is
  the signal that the failure is not transient. Nothing watches it — alerting is still unbuilt.
- **Revision rejection** lives in the existing review screen behind a confirmation step with its
  own required reason, and the confirmation says in words that the action is terminal and the
  correction is a newer revision published over it, never a rollback (ADR-033).
- **The review screen gained a `ReviewRequired` / `Rejected` queue selector**, which rejection
  being terminal made necessary: a rejected revision leaves the review queue, so without it the
  recorded reason was unreachable.
- **One backend change was required.** `ScheduleRevisionDetail` did not project `RejectedBy` /
  `RejectionReason` / `RejectedAtUtc`, so the reject endpoint wrote a record no read path could
  return. Three fields added to the application contract and the persistence projection. No
  migration, no behaviour change; the approval fields stay separate so the trail can never state
  the opposite of what happened.
- 828 `.NET` tests pass, 0 skipped (Contracts 6, Api 5, Infrastructure 577, Persistence 240
  against the real PostgreSQL database) — one new persistence test proving the rejection record
  reads back. 20 frontend tests pass, up from 15: the five new ones cover the reason requirement
  on release, the ambiguity refusal rendering as words rather than a disabled control, the failed
  queue being fetched by `dispatchState` rather than review state, rejection's confirm-plus-reason
  path, and a rejected revision reading back with no action offered. `npm run typecheck` is clean
  and the production build succeeds with 24 routes.
- **Pre-existing, untouched by this session:** `dotnet format --verify-no-changes` reports an
  import-ordering error in `src/Sirkadiyen.Api/Composition/ApiEndpointRouteBuilderExtensions.cs`,
  a file with no changes in this session. Every file this session touched is clean.

## The profile edit surface and its audit (2026-08-16)

A live backend feature had no way in. ADR-096 made an audience-changing profile write converge the
student's calendar through a fenced worker stage, and `PUT /api/profile` reported
`calendarResyncRequested` so a screen could say so — but the only academic-profile screen was
`/onboarding/profile`, gated to `ProfileRequired`, a state a student leaves permanently once they
have a profile. The whole ADR-096 path was reachable only by calling the API directly (ADR-105).

- **`/profile`** renders the shared `AcademicProfileForm` prefilled from `GET /api/profile`, for
  every onboarding state in which a profile already exists. The dashboard's academic-profile card
  links to it. `Suspended` is excluded because the backend refuses the write for an unactivated
  account; offering the form there would be a promise the API cannot keep.
- **`calendarResyncRequested` was missing from the typed browser contract entirely** — a contract
  gap that hid a whole feature rather than a field.
- **A profile change now writes a `ProfileUpdated` audit event**, closing the documented gap
  against AI_GUIDELINE §19. `audienceChanged` and `calendarResyncRequested` are recorded
  separately because they genuinely differ: an audience change on an account with no completed
  calendar connection queues nothing. `SaveStudentProfileResult` gained `AudienceChanged`, which
  the store already reported and the service had been dropping. The student number is never
  recorded, and a test asserts its absence.
- **The admin audit category filter** had also been missing both finance categories since ADR-093;
  it now carries all six.
- **The pre-existing `dotnet format` import-ordering error is fixed.**
  `dotnet format --verify-no-changes` is clean over the solution.
- 594 .NET tests pass in the suites that could run (Contracts 6, Api 8 up from 5, Infrastructure
  581 up from 577) and 27 frontend tests, up from 20. Release build 0 warnings, typecheck clean,
  production build 25 routes.
- **Not run:** the Persistence integration suite — Docker is not running here and `localhost:15432`
  is unreachable, so no real PostgreSQL was available. No persistence, migration or query code
  changed; `StudentProfileStoreTests` already covers all four combinations of the two flags.
- **Not done:** an operator still cannot change a student's profile on their behalf, so the audit
  row always records the student as the actor.

## Two Grade 3 duplication bugs, from a student's calendar back to their causes (2026-08-18)

A Grade 3 student saw the faculty-practice rotation eight times per slot, and every session both
halves of the class attend twice. Two unrelated causes; each fixed and committed on its own.

- **Eight faculty practices (ADR-109).** `CalendarAudienceResolver` matched a record if *any*
  selector matched the student, whatever dimension it belonged to. A faculty-practice record states
  `curriculumGroup=3-A` *and* `facultyPracticeGroup=A3`, so a student in cohort A5 matched the
  curriculum-group half of all eight records. Selectors now enumerate within a dimension and narrow
  across dimensions. One pure function; every write path already routed through it.
  Verified first against every committed real snapshot that faculty practice is the *only* source
  family emitting more than one dimension per candidate, so no other program's audience moves.
- **Duplicate joint sessions (ADR-110).** Both Turkish Grade 3 workbooks state the joint sessions,
  in different wordings (`Simüle Hasta Uygulaması` vs `Simüle Hasta FM Uygulaması` on 11 Jan 2027),
  so their stable identities differ and nothing could recognize them as one lesson. Each source now
  declares the audience it owns and publishes only that; the A workbook's joint rows address `3-A`,
  the B workbook's `3-B`. Configuration in `ParseSourceContext`, applied by the parser before
  identity is computed, refused-and-counted when a row addresses only an unowned group.
- 891 .NET tests pass (Contracts 6, Api 8, Infrastructure 622, Persistence 255) and 490 parser tests,
  with `ruff` and `mypy --strict` clean. Persistence *did* run this time.
- Goldens regenerated deliberately and read before committing: 60 identity changes in the A annual,
  46 in the B, version-and-digest only in the English one. No candidate added or removed anywhere.
- **Required follow-up, not done:** existing Grade 3 calendars are not repaired. Inventory
  reconciliation never deletes from absence (ADR-089), so the seven surplus faculty-practice events
  per slot and the duplicate joint sessions stay until an audited repair runs. Recorded in
  `activeContext.md` as an open risk.
- **Open risk:** nothing checks that each curriculum group is owned by exactly one source.

## The Grade 3 repair path and the ownership coverage rule (2026-08-18)

Closing the two open items ADR-109/110 left behind. 909 .NET tests pass (Contracts 6, Api 8,
Infrastructure 636, Persistence 259 against real PostgreSQL); `dotnet format` clean.

- **The repair requests convergence rather than deleting (ADR-111).** `CohortCalendarRepairService`
  plans what a program's calendars hold that is no longer applicable, and on a hash-bound
  confirmation flags those connections for the existing `ProfileChangeResyncService` pass. Every
  deletion is still made there, under bounds already tested: publication-gated, budgeted,
  freeze-aware, resumable, credential-aware. No second deletion path, and no exception carved into
  ADR-089.
- **Surfaces:** `POST /api/operations/calendar-repairs/preview` and `POST /api/operations/calendar-repairs`,
  SuperAdmin + antiforgery + required reason, with a `CalendarRepairRequested` audit entry carrying
  the plan hash and counts. No frontend.
- **Only the ADR-109 surplus needs this.** The ADR-110 joint duplicates are handled by publishing
  the 1.1.0 revision: those records change, so the diff emits `Deleted` and incremental sync removes
  them. Verified against `IncrementalSyncPlanner`, which resolves a no-longer-applicable record for
  an existing holder to `Delete`.
- **Ownership coverage now fails the catalog load (ADR-111).** Among sources sharing one program and
  one parser profile, every audience share must be owned exactly once — not twice, not by nobody,
  and not by one sibling while the other declares nothing. Writing it caught an unrealistic fixture
  in ADR-110's own tests, which was the point.
- Two design flaws surfaced while testing and were fixed rather than papered over: a student whose
  only anomaly was an unpublished leftover vanished from the plan entirely (the count is now
  cohort-wide), and the gap check was unreachable behind the overlap check for the fixture I first
  wrote.
- **Not done:** no frontend for the repair; the admin audit-category dropdown remains four categories
  behind (pre-existing drift since ADR-107).

## The calendar repair gets an operator screen (2026-08-18)

ADR-111 shipped API-only; the repair is now a control on `/admin/operations` beside the freeze.

- **`CalendarRepairControl`** is a two-step control: preview, then a confirmation carrying the
  `planHash` the backend computed. Editing any part of the scope drops the previewed plan, because
  the hash belongs to the cohort it was computed for and a stale one attached to a changed form is
  precisely what the hash exists to prevent.
- **The plan is shown in the terms the backend planned it**: how many events are deleted, how many
  written, how many students are affected out of the whole cohort, and how many rows are
  deliberately left alone. That last figure makes the ADR-089 boundary visible instead of hiding it
  — an operator seeing "3 rows untouched" can ask why, which is the point of reporting it at all.
- A per-student breakdown sits behind a `<details>`, so the summary stays readable for a cohort of
  hundreds while the detail is one click away.
- The 409 from a stale plan hash clears the plan and asks for a fresh preview rather than letting
  the operator retry a confirmation that cannot succeed.
- 52 frontend tests pass (up from 46), typecheck clean, production build compiles with 25 routes and
  no warnings. `GAPS.md` records the new wiring.
- **Not done:** the screen has not been exercised against a live backend — that needs PostgreSQL,
  the API and a SuperAdmin session. Behaviour is covered by tests against a mocked client only.

## Grade 3 bedside topics reach the calendar (2026-08-18)

- **Fixed (ADR-112):** `ScheduleSourceStore.UpsertAsync` now copies `CompanionSourceIds` onto an
  existing row. Without it the catalog's companion declaration applied only to a database seeded
  after it was written, so both Grade 3 annuals parsed with no companion and every bedside event
  reached students with an empty description.
- **Regression test:** `ADeclaredCompanionReachesARowThatWasSeededWithoutOne` seeds a row without a
  companion, upserts one that declares it, and asserts the row carries it. Verified to fail on the
  unfixed store.
- **Tests executed:** 261 persistence tests pass. The Infrastructure suite was not run — the running
  Worker and Api processes lock `Sirkadiyen.Infrastructure.dll` and the project cannot relink.
- **Not done:** the worker has not been restarted, so the two annual rows still hold an empty
  companion list and the 368 bedside records still have no `notes`. No code change can do that part.

## Bedside and patient-practice events name their department (2026-08-18)

- **Added (ADR-113):** the annual profile publishes the department a title states for the groups a
  record addresses. `resolve_group_departments` reads the `A Grubu (İç H.) B Grubu (ÇSvH)`
  construction; `_stated_departments` selects from it by audience. `grade3_yearly_v1` → 1.2.0,
  catalog versions bumped, four Grade 3 goldens regenerated.
- **Added:** `DepartmentCatalog` aliases for the two source abbreviations, and
  `CalendarEventPresentationPolicy.Description` now names every department through the catalog,
  falling back to the source's words.
- **Tests added:** seven parser cases (six for the resolver including the Grade 1 false positive,
  five at profile level) and three calendar-presentation cases.
- **Tests executed:** 502 parser tests, ruff and mypy clean. **No .NET test was run** — the running
  Worker and Api lock the assemblies, so the Infrastructure suite cannot build.
- **Not done:** the .NET side is unverified, and the one-time description rewrite of every
  department-bearing event has not been performed or scheduled.

## The schedule source catalog becomes an editable, audited document (2026-08-19)

- **Added (ADR-114):** `ScheduleSourceCatalogEditingService` (read/preview/apply),
  `ScheduleSourceCatalogPlanner` (field-level diff, risk classification, plan hash),
  `ScheduleSourceCatalogFile` (atomic write, write probe), `ScheduleSourceCatalogRevision` +
  `schedule_source_catalog_revisions` migration, the `/api/admin/source-catalog` endpoint group,
  and the `SourceCatalogEditor` React surface as a third tab on `/admin/sources`.
- **Changed:** `ScheduleSourceCatalogLoader` gained `Parse(string)` and now refuses unknown JSON
  properties, so the worker and the admin panel validate by one implementation; its failures are
  `ScheduleSourceCatalogValidationException` rather than `InvalidDataException`. The source upsert
  moved to `ScheduleSourceUpsert.StageAsync` so the startup seed and the admin edit apply
  configuration identically, the second inside the transaction that records its revision.
- **Changed (deployment):** the live catalog is `/srv/sirkadiyen/shared/config/schedule-sources.json`;
  `sirkadiyen-activate` seeds it, the API unit may write it, the worker only reads it.
- **Tests added:** 14 editing-service cases, 6 catalog-file cases, 4 persistence cases for the
  commit transaction, 7 web component cases; 12 loader tests updated for the new exception type.
- **Tests executed:** 661 Infrastructure unit tests, 8 API unit tests, 6 Contracts tests, 53 web
  tests, `tsc --noEmit` clean. **The persistence suite did not run** — no database is reachable on
  this machine.
- **Not done:** no poll or parse can be triggered from the panel, so a corrected source is picked up
  on its next scheduled cycle.

## Yasal metinler ve arayüz sadeleştirmesi (2026-08-19)

- **Changed:** `web/src/app/gizlilik/page.tsx` ve `web/src/app/kosullar/page.tsx` tamamen yeniden
  yazıldı (KVKK aydınlatma yapısı + Google Limited Use beyanı + gerçek saklama/çerez tabloları).
  `LegalDocument` artık `bannerText` olmadan da render ediyor ve `effective` tarihi gösteriyor.
- **Changed:** `onboarding/calendar` izin ekranı, `calendarlist.readonly` kapsamını doğru anlatacak
  şekilde düzeltildi.
- **Removed:** `ImplNote` bileşeni, `.impl-note` CSS'i ve sekiz sayfadaki kullanımları.
- **Tests executed:** 53 web testi, `tsc --noEmit`, `next build`. Yeni test eklenmedi — değişiklik
  metin ve içerik düzeyinde.
- **Not done:** saklama sürelerini uygulayan otomatik silme işi ve kullanıcının kendi hesabını
  silmesini sağlayan uç nokta yok; ikisi de metinde taahhüt olarak duruyor.

- **Changed (aynı gün):** saklama süreleri ölçüt bazlı anlatıma çevrildi, iletişim bilgileri
  `web/src/lib/contact.ts` üzerinden gerçek adres/telefonlarla değiştirildi, lisans adımına
  WhatsApp ile kod isteme bağlantıları eklendi.

## Dönem 2 Türkçe akademik yıl taşıması (2026-08-19)

- **Root cause:** silme eşleşme defterinden (yıl sormaz), ekleme kohort sorgusundan (yıla göre
  süzer) sürülüyor. Kaynak 2026-2027'ye taşınınca saklanan profiller 2025-2026'da kaldı ve yeni
  kayıtlar kimseye denk gelmedi. ADR-103 bu hatayı öngörmüştü ama yalnızca *yeni* profiller için
  çözmüştü; Dönem 3'te mevcut öğrenci yoktu, Dönem 2'de vardı. Ayrıntı: ADR-115.
- **Added:** `ProfileAcademicYearRolloverService`, `IProfileAcademicYearRolloverStore` +
  PostgreSQL uygulaması, `StudentProfile.MoveToAcademicYear`,
  `SupportedProfileSchemaCatalogCheck`, `AuditEventCategory.ProfileAcademicYearRolled`,
  `POST /api/operations/profile-rollovers[/preview]`,
  `POST /api/admin/users/{id}/calendar-recheck[/preview]`,
  `ProfileRolloverControl` bileşeni.
- **Changed:** `CurrentSupportedProfileSchema` — Dönem 2 Türkçe 2026-2027, şema sürümü 1.3,
  `Grade3AcademicYear` → `RolledOverAcademicYear`. `CohortCalendarRepairService` tek kullanıcı
  yoluyla genişletildi (`PlanForUserAsync` / `RequestForUserAsync`), planlayıcı holding listesi
  üzerinden çalışacak şekilde ayrıldı. `SourceCatalogInitializer` açılışta ayrışmayı raporluyor.
  `config/schedule-sources.json` G2-TR-ANNUAL/PRACTICE için 2026-2027.
- **Tests executed:** 681 backend birim testi (tümü geçti), 62 web testi, `tsc --noEmit`,
  `dotnet build`. Yeni testler: rollover servisi (11), kullanıcı bazlı yeniden kontrol (5),
  şema/katalog ayrışma kontrolü (4), `ProfileRolloverControl` (7), `AdminUserDetail` yeniden
  kontrol (2).
- **Tests not executed:** `Sirkadiyen.Persistence.Tests` — PostgreSQL ayakta değildi (Docker
  kapalı), 226 test bağlantı hatasıyla düştü. Bu düşüşler bu değişiklikten önce de vardı;
  `ProfileAcademicYearRolloverStore` ve `CohortCalendarRepairStore.FindUserHoldingAsync` için
  persistence testi **yazılmadı** ve yazılması gerekiyor.
- **Not done:** taşımanın kendisi çalıştırılmadı (operatör kararı). Dönem 2 anatomi ve dikey
  koridor kaynakları 2025-2026'da bırakıldı; 26-27 belgeleri henüz yok.

## Silinen yönetilen takvimin kurtarılması (2026-08-19)

- **Root cause:** `MarkManagedCalendarUnavailable` bayrağını hiçbir şey temizlemiyordu —
  `Reauthorize` bile — ve bayrağı temizleyebilen tek metot (`AttachManagedCalendar`) takvim
  kimliği doluyken `throw` ediyordu. Onboarding `ActionRequired` → izin ekranı → izin yenile →
  yine `ActionRequired`. Kapalı döngü. ADR-062'nin "explicit repair flow" olarak ertelediği akış
  hiç yazılmamıştı. Ayrıntı: ADR-116.
- **Added:** `GoogleCalendarConnection.ResetForCalendarRebuild`, `ManagedCalendarRebuildService`,
  `IUserCalendarConnectionStore.RebuildManagedCalendarAsync` + PostgreSQL uygulaması,
  `ManagedCalendarRebuildResult`/`Outcome`/`Assessment`,
  `AuditEventCategory.ManagedCalendarRebuilt`, `GET`/`POST /api/calendar/rebuild`,
  `GET`/`POST /api/admin/users/{id}/calendar-rebuild`, `CalendarRebuildEndpoints`,
  onboarding `RebuildCalendar` ve admin `CalendarRebuild` bileşenleri.
- **Changed:** `/onboarding/calendar` artık `ActionRequired` için doğru sebebi gösteriyor
  (eski metin "Google erişimi iptal edilmiş" diyordu — yanlıştı; iptal edilmiş bir izin
  `CalendarAuthorizationRequired` üretir). `AdminUserDetail` uyarı banner'ı eyleme dönüştü.
- **Tests executed:** 693 backend birim testi, 68 web testi, `tsc --noEmit`, `dotnet build` —
  hepsi geçti. Yeni testler: `ManagedCalendarRebuildServiceTests` (12, domain reset dahil),
  onboarding takvim sayfası (4), admin yeniden kurma (2).
- **Tests not executed:** `Sirkadiyen.Persistence.Tests` — PostgreSQL ayakta değil (Docker
  kapalı). `RebuildManagedCalendarAsync` için persistence testi **yazılmadı**: `ExecuteDeleteAsync`
  ile domain reset'in tek transaction'da davrandığını yalnızca gerçek veritabanı doğrulayabilir.
  Yazılması gerekiyor.
- **Not done:** tespit hâlâ pasif — silinen takvimi arayan bir izleyici yok, bayrak ilk başarısız
  yazmada basılıyor.

## Akademik yıl uzlaştırıcısı (2026-08-19)

- **Root cause:** ADR-115'in operatör ekranı manuel bir adımdı ve atlanabiliyordu. Kararın
  kendisi şema yayına alındığında zaten verilmiş oluyor. Ayrıntı: ADR-117.
- **Added:** `ProfileAcademicYearRolloverService.ReconcileDriftAsync`,
  `IProfileAcademicYearRolloverStore.ListDriftedAsync` + PostgreSQL uygulaması, `DriftedProfile`,
  `ProfileAcademicYearDriftOptions` (+ worker options factory girdisi
  `SIRKADIYEN_SYNC:ACADEMIC_YEAR_DRIFT_PROFILES_PER_PROGRAM`, varsayılan 25),
  `ProfileDriftReconcileRunResult`/`ProfileDriftReconciliation`/`ProfileDriftOutcome`,
  `ProfileAcademicYearDriftTask`, `WorkerCompositionTests`.
- **Changed:** `FencedCalendarMaintenanceTask` yeni aşamayı profil-resync'in hemen öncesine
  aldı. Worker kompozisyonu artık `SupportedProfileSchema`, `AuditEventRecorder` ve
  `IAuditIpProtector` kaydediyor — worker ilk kez `AuditEvent` yazıyor.
  `SelectorsRemainValid` profil görünümü üzerinden çalışacak şekilde genelleştirildi.
- **Tests executed:** 702 backend birim testi, `dotnet build` — hepsi geçti. Yeni testler:
  uzlaştırıcı davranışı (6), worker DI çözümlemesi (3), drift options varsayılan/override (2).
- **Not done:** sınıf geçişi mekanizması yok ve bilerek yok (ADR-117); öğrenci kendi profilinden
  dönemini değiştirir. `ListDriftedAsync` için persistence testi yazılmadı.

## Onarma, silinmiş event tombstone'unu diriltiyor (2026-08-21)

- **Root cause:** ADR-123 onarımı/envanteri, Google'da `status: cancelled` tombstone olarak
  duran event'leri `PatchEventAsync` ile yamalıyordu ama `ToGoogleEvent` gövdesi `status`
  yazmadığından tombstone cancelled kalıyor, görünmez oluyordu. Worker log'u belirleyiciydi:
  `0 inserted, 500 patched`. Ledger sağlamdı (`on_other = 0`), iki-takvim bölünmesi değildi.
  Ayrıntı: ADR-125.
- **Changed:** `GoogleCalendarClient.ToGoogleEvent` artık `Status = "confirmed"` yazıyor
  (insert + patch gövdesi). Canlı event için no-op, cancelled tombstone'u patch'le diriltir.
- **Tests executed:** `Sirkadiyen.Infrastructure` Release derlemesi — 0 uyarı, 0 hata. Bu istemci
  canlı Google ile konuşur ve birim testi yoktur; sahte `IUserCalendarClient` etkilenmez.
- **Not done / not verified:** uçtan uca diriltme yerelde çalıştırılmadı (Google kimlik bilgisi
  yok). Deploy sonrası ADR-121 doğrulamasıyla teyit: eksik 500 → 0, uyumlu 317 → 817. Fazla 815
  (2025-2026) §13 gereği dokunulmadan kalır — ayrı yetkili işe bırakıldı.

## Kapsanmayan grup rotasyonu tümüyle yayımlanıyor (2026-08-21)

- **Root cause:** ADR-073'ün "diseksiyonu anatomi listesine devret" kararı, listenin hiç
  yüklenmediği (ve rollover sonrası yürüyen yıl için hiç yüklenemediği) durumu kapsamıyordu:
  dönem 2 öğrencisi hiç diseksiyon görmüyordu. Ayrıntı: ADR-126.
- **Changed:** parser tarafında `group_rotation_fallback` profil bildirimi, `grade2_yearly_v1`
  1.1.0, `groupRotationCoveredDates` sözleşme alanı, saat etiketi + açıklama notu, yeni sayaçlar
  (`rows.publishedGroupRotationFallback`, `groupRotationFallback.days`,
  `rows.ignored.groupRotationCoveredByCompanion`) ve snapshot başına tek uyarı. .NET tarafında
  `ScheduleSource.GroupRotationSourceIds` + `AddGroupRotationOwners` migration'ı,
  `IGroupRotationCoverageStore`/`GroupRotationCoverageStore`, poller'ın kapsama çözümlemesi,
  parse-run parmak izine kapsama, katalog doğrulaması, katalogda G2-TR/EN-ANNUAL kayıtları ve
  admin katalog editöründe `groupRotationSourceIds` alanı.
- **Tests executed:** parser 509 test (yeni: yedek yayımlama, kapsanan tarih, dil, saat sırası,
  tek uyarı, fallback bildirmeyen profil, /v1/profiles alanı) + ruff + mypy temiz; g2-tr/g2-en
  altın dosyaları yeniden üretildi ve gözden geçirildi (790→949, 935→1094). .NET: Infrastructure
  731, Contracts 6, Api 8 test geçti; `dotnet build` 0 uyarı. Web: typecheck temiz, 78 test geçti.
- **Not done / not verified:** `GroupRotationCoverageStoreTests` yazıldı ama burada
  çalıştırılamadı — Docker kapalı, PostgreSQL yok (CI'da koşacak). Kapsamanın gerçek snapshot
  üzerindeki yolu için ayrı bir altın dosya eklenmedi; birim testleri sentetik satırlarla
  kapsıyor.


## G2-EN-ANNUAL yeniden parse ediliyor (2026-08-29)

- **Root cause:** 2026-2027 İngilizce Dönem 2 kitabı dönem sütununun başlığını (`A1`) boş
  bırakmış. Yıllık okuyucu zorunlu rolleri başlık alias'ıyla eşlediği için başlık satırı
  tanınamadı ve snapshot tümüyle reddedildi (`worksheetWithoutHeaderRow` →
  `noParsableWorksheet`). Sütunun altı hâlâ `Time Table 2` diyor; yerleşim değişmemiş.
  Ayrıntı: ADR-128.
- **Changed:** `grade2_yearly_v1` 1.2.0 ve `term_column_may_be_unlabelled` bildirimi;
  `GET /v1/profiles` bu bildirimi de yayımlıyor; `config/schedule-sources.json` içinde iki
  Dönem 2 yıllık kaynağın sabitlenmiş `parserProfileVersion` değeri; `g2-en-annual` snapshot
  fixture'ı ve altın dosyası 2026-2027 kitabından yeniden üretildi (altın senaryosu `_Y2026`);
  `sheets/donem-2-ing/xlsx/2026-2027 ...xlsx` depoya eklendi; parser README, fixture README ve
  `sheets/source-manifest.md` güncellendi.
- **Tests executed:** parser 512 test geçti (yeni: başlıksız İngilizce başlık satırı regresyonu,
  etiketli başlığın hâlâ tercih edildiği, `/v1/profiles` alanı) + ruff check + mypy temiz.
  `dotnet build` 0 uyarı 0 hata; `dotnet test`: Api 8, Contracts 6, Infrastructure 736,
  Persistence 40 geçti / 236 atlandı (Docker kapalı, PostgreSQL yok — CI'da koşacak).
- **Not done / not verified:** `ruff format --check` `sirkadiyen_parser/parsers/bedside.py`
  için tek bir biçim farkı bildiriyor; bu dosyaya dokunulmadı, fark bu değişiklikten önce de
  vardı. `G2-TR-ANNUAL` katalogda 2026-2027'ye bakıyor ama fixture'ı hâlâ 2025-2026 —
  yeniden üretilmedi, ayrı bir altın gözden geçirmesi gerekiyor.

## Anatomi kaynakları 2026-2027'ye taşındı, kalan yedi kaynak belgesini bekliyor (2026-08-29)

- **Root cause:** Katalogdaki 11 kaynak hâlâ 2025-2026'daydı. `academicYear` hem kararlı kimlik
  bileşeni hem de kayıt-öğrenci eşleşme alanı olduğu için toplu çevirme ADR-115 arızasını
  tekrarlardı; ayrıntı ADR-129.
- **Changed:** `config/schedule-sources.json` içinde dört `G2-ANATOMY-*` kaynağı 2026-2027'ye
  alındı ve notları ne beklediklerini söyleyecek şekilde yazıldı; `sheets/source-manifest.md`
  anatomi satırları güncellendi; ADR-129 eklendi. Başka hiçbir kaynağın yılı değiştirilmedi.
- **Tests executed:** `dotnet test`: Api 8, Contracts 6, Infrastructure 736, Persistence 40 geçti
  / 236 atlandı (Docker kapalı). Katalog JSON'u yeniden ayrıştırılarak doğrulandı. Parser tarafı
  bu değişiklikten etkilenmiyor (altın senaryoları yılı kataloğdan değil test durumundan okuyor).
- **Not done / not verified:** Dönem 1'in dört kaynağı, iki dikey koridor kaynağı ve SHARED-AMPHI
  2025-2026'da bırakıldı — 2026-2027 belgeleri yok. Dikey koridor DOCX'leri indirilip içerikleri
  doğrulandı (hâlâ 2025-2026). Dönem 1 elektronik tabloları servis hesabı olmadan okunamadı
  (HTTP 401), yani içerikleri hakkında bir şey doğrulanmadı. `CurrentSupportedProfileSchema`
  1.3'te bırakıldı; Dönem 1 taşınmadığı için bumplanmadı.

## grade1_yearly_v1 1.6.0 — başlıksız dönem sütunu (2026-08-29)

- **Root cause:** 2026-2027 Dönem 1 Türkçe kitabı da dönem sütununun başlığını boş bırakmış;
  G2-EN ile aynı `noParsableWorksheet` reddi. ADR-128 uzatıldı.
- **Changed:** `grade1_yearly_v1` 1.6.0 + `term_column_may_be_unlabelled`; parsers registry
  anahtarı; `config/schedule-sources.json` içinde G1-TR/EN-ANNUAL sabit sürümü 1.6.0; iki Dönem 1
  altın dosyası yeniden üretildi (yalnızca sürüm + özet değişti); `test_api.py` bildirim iddiası
  üç yıllık profili de kapsayacak şekilde güncellendi ve iki uygulama profilinin bildirmediği
  eklendi; testlerdeki olumsuz durum için `UNDECLARED_TERM_PROFILE` ayrıldı; parser README.
- **Tests executed:** parser 512 test geçti, ruff + mypy temiz. `dotnet build` 0 uyarı/0 hata;
  `dotnet test`: Api 8, Contracts 6, Infrastructure 736, Persistence 40 geçti / 236 atlandı.
  Yeni 2026-2027 Türkçe kitabı bu bildirimle 794 aday üretiyor (2026-09-28 .. 2027-07-13).
- **Not done / not verified:** Dönem 1 kaynaklarının katalog girişleri hâlâ 2025-2026 belgelerini
  ve yılını gösteriyor — madde 3. `G1-EN-PRACTICE`'teki İ/i kohort tutarsızlığı (madde 2) ve
  `G1-TR-PRACTICE`'teki 2020-11-20 yazım hatası düzeltilmedi. `ruff format --check` hâlâ
  dokunulmamış `parsers/bedside.py` için tek bir fark bildiriyor.

## G1-EN-PRACTICE kohort yazımları ve programa göre alfabe sınırı (2026-08-29)

- **Root cause:** Kaynak aynı kohortu `İ1` (U+0130) ve `i1` (ASCII) diye yazıyor; grup okuyucusunun
  desenleri ASCII-only olduğu için noktalı olanlar düşüyor, ASCII olanlar katalogun bildirmediği
  `I1` değerini yayımlıyordu. 734 hücreden 36 aday. Ayrıntı: ADR-130.
- **Changed:** `normalization/groups.py` tek harfli etiket için Türkçe `i` katlaması; parser motoru
  0.3.0; `parsers/practice.py` içinde `TURKISH_COHORT_LETTERS` + `ENGLISH_PRACTICE_SUBGROUPS` ve
  programa göre sınırlı `_audience_selectors`; `grade1_practice_v1` 1.1.0, `grade2_practice_v1`
  1.3.0, `grade2_vertical_corridor_v1` 1.1.0, iki `grade2_anatomy_*` 1.1.0; katalogda on kaynağın
  sabit sürümü; `ScheduleSourceCatalogTests` beklentileri; parser README.
- **Tests executed:** parser 527 test geçti (yeni: dört yazımın tek değer yayımlaması, iki yönlü
  program sınırı, `TELAFİ`'nin hâlâ kohort olmaması, primitif düzeyinde katlama ve ASCII koşusunun
  etkilenmemesi) + ruff + mypy temiz. Yirmi altın dosyası yeniden üretildi; değişen her satır sürüm
  ya da özet. `dotnet build` 0 uyarı/0 hata; `dotnet test`: Api 8, Contracts 6, Infrastructure 736,
  Persistence 40 geçti / 236 atlandı.
- **Not done / not verified:** `G1-EN-PRACTICE` için hâlâ commit'li fixture ve altın dosyası yok —
  madde 3'te katalog girişleriyle birlikte eklenecek. Kaynaktaki 13 gürültü hücresi ve
  `G1-TR-PRACTICE`'teki 2020-11-20 yazım hatası Öğrenci İşleri'nde. `ruff format --check` hâlâ
  dokunulmamış `parsers/bedside.py` için tek bir fark bildiriyor.

## Dönem 1 kaynakları 2026-2027'ye taşındı (2026-08-29)

- **Root cause:** Değil — planlı rollover. ADR-129'un belge bekleyen yedi kaynağından dördünün
  2026-2027 belgeleri geldi. Ayrıntı: ADR-131.
- **Changed:** `config/schedule-sources.json` içinde dört Dönem 1 girişi yeni Drive id'lerine,
  `transport: googleDriveFile` + `documentFormat: xlsx`'e (üçü `googleSheets` + `sheetGid` idi),
  `academicYear: 2026-2027`'ye ve yeni fixture yollarına alındı; notlarına belgelerin bilinmesi
  gereken özellikleri yazıldı. Dört snapshot fixture'ı yeniden üretildi, `g1-en-practice` ilk kez
  eklendi (fixture + altın + `test_golden_parse` senaryosu + `test_real_snapshot_contracts` satırı);
  dört Dönem 1 altın senaryosu `_Y2026`'ya geçti. `CurrentSupportedProfileSchema` 1.4: Dönem 1 TR/EN
  2026-2027'ye, `RolledOverAcademicYear` `AcademicYear`'a katlandı (program başına alan duruyor).
  `CurrentSupportedProfileSchemaTests` ve `ScheduleSourceCatalogTests` beklentileri (transport
  sayıları 7/11 → 4/14, G1-TR-ANNUAL externalId + `SheetGid` null). Dört yeni xlsx `sheets/` altına;
  fixture README ve `sheets/source-manifest.md` güncellendi (durum `Collected` → `ParserImplemented`,
  aralıklar ve aday sayıları).
- **Tests executed:** parser 530 test geçti, ruff + mypy temiz. `dotnet build` 0 uyarı/0 hata;
  `dotnet test`: Api 8, Contracts 6, Infrastructure 736, Persistence 40 geçti / 236 atlandı.
  Parse sonuçları: TR yıllık 794 aday, EN yıllık 982, TR uygulama 302, EN uygulama 88.
- **Not done / not verified:** `POST /api/operations/profile-rollovers` **çalıştırılmadı** — operatör
  adımı, önce şema deploy edilmeli; o zamana kadar depodaki Dönem 1 profilleri 2025-2026 damgalı ve
  bu kaynaklardan hiçbir şey almıyor. `G2-VERTICAL-AUTUMN/SPRING` ve `SHARED-AMPHI` hâlâ 2025-2026.
  Dönem 2 yıllık/uygulama/anatomi kaynaklarının katalog yılı ile fixture yılı hâlâ ayrışık
  (ADR-128, ADR-129). Kaynak kusurları düzeltilmedi: G1-TR-PRACTICE'te 2020-11-20 yazım hatası
  (revizyon her seferinde tutulacak) ve iki yıllık kitapta beşer `HER HAFTA ...` satırı (o haftalık
  online dersler takvime düşmüyor). `ruff format --check` hâlâ dokunulmamış `parsers/bedside.py`
  için tek bir fark bildiriyor.


## Öğrenci listesi araması (ADR-085) uygulandı (2026-08-29)

- **Root cause:** Değil — ADR-085 kabul edilmiş ama hiç inşa edilmemişti. Ayrıntı: ADR-132.
- **Changed:** Yeni `config/student-rosters.json` (dört yayımlanmış liste: konum, kohort, düzen,
  değer eşlemeleri, kusur notları). Application'da `StudentRosters/` — katalog modeli, okuyucu,
  okuma sözleşmeleri, arama servisi ve sözleşmeleri, `IStudentRosterIndex`. Infrastructure'da
  `StudentRosterCatalogLoader` ve bellekte tutan `StudentRosterIndex` (bir saatlik yenileme,
  başarısız listede önceki okumayı koruyup hatayı ayrıca bildiriyor). API'de
  `POST /api/profile/roster-lookup` (antiforgery + yeni `RosterLookup` oran sınırı) ve
  yanıt sözleşmesi; Sheets/Drive edinicileri artık API'de de kayıtlı. Frontend'de
  `AcademicProfileForm` numara-önce akışına geçti: arama düğmesi, `RosterLookupNotice`, alan başına
  "listeden dolduruldu" / "kendin seçmen gerekiyor" ayrımı; `lookUpStudentRoster` ve tipleri.
  `decisionLog.md`'de ADR-085 durumu "implemented by ADR-132" olarak işaretlendi.
- **Tests executed:** `dotnet build` 0 uyarı/0 hata. `dotnet test`: Api 11, Contracts 6,
  Infrastructure 769, Persistence 40 geçti / 236 atlandı (Docker kapalı). Web: `tsc --noEmit` temiz,
  `vitest run` 85 test geçti. Okuyucu ayrıca dört gerçek belgeye karşı geçici bir testle
  doğrulandı — 384/113/331/96 öğrenci, sıfır reddedilen satır, beş geri kazanılmış baş sıfır; o test
  silindi çünkü belgeler kişisel veri içeriyor ve depoya giremez.
- **Not done / not verified:** Tarayıcıda doğrulanmadı — akış API ve PostgreSQL gerektiriyor, Docker
  kapalı. `97` önekli dört öğrenci hâlâ `StudentProfileValidator` tarafından reddediliyor (ürün
  kararı, alınmadı). Liste düzenlenirse bir saate kadar eski okuma sunulabilir; zorla yenileme yok.
  Dönem 2 İNG ve Dönem 3 İNG listeleri kataloglandı ama hiçbir şey önermiyor (ADR-084, ADR-098).
  API artık `SIRKADIYEN_GOOGLE` kaynak kimlik bilgisine ihtiyaç duyuyor; yoksa her liste
  "okunamadı" olarak bildiriliyor, onboarding çökmüyor.

## Öğrenci listesi kataloğu artık panelden düzenleniyor + duran deploy düzeltildi (ADR-134) (2026-08-30)

- **Root cause (bildirilen hata):** Kod değil, deploy. Panelde `schedule-sources.json` yüklenirken
  gelen *"The JSON property 'discoveryFolderId' could not be mapped"* hatası, sunucudaki API'nin
  ADR-133 öncesi sürüm olmasından. Deploy iş akışı fark tabanını `github.event.before`'dan
  hesaplıyordu: ADR-133'ün API + worker + migration'ını taşıyan push parser testlerinde patlayınca
  `deploy` job'ı hiç çalışmadı, sonraki push yalnız `src/parser/tests`'e dokunduğu için API ve
  migration "değişmemiş" sayıldı ve bir daha hiç gönderilmedi.
- **Changed (deploy):** `.github/workflows/deploy.yml` fark tabanını artık bu iş akışının `main`
  üzerindeki **son başarılı** koşusunun SHA'sından alıyor (Actions API, `actions: read`); bulunamazsa
  push aralığına, o da yoksa tam deploya düşüyor. Başarısız bir deploy artık kendini onarıyor.
- **Changed (öğrenci listeleri):** ADR-114'ün altı adımı öğrenci listesi kataloğuna uygulandı.
  Domain'de `StudentRosterCatalogRevision`; Application'da katalog dosyası ve revizyon portları,
  `StudentRosterCatalogPlanner` (alan bazlı fark, değer eşlemesi iki yönde tam yazılıyor) ve
  `StudentRosterCatalogEditingService`; Infrastructure'da `StudentRosterCatalogFile`, EF
  yapılandırması, revizyon store'u ve `student_roster_catalog_revisions` migration'ı; API'de
  `/api/admin/roster-catalog` (GET, preview, apply, revisions) ve yeni
  `StudentRosterCatalogUpdated` denetim kategorisi; frontend'de `/admin/rosters` sayfası ve
  `RosterCatalogEditor` (form / JSON / sürüm geçmişi sekmeleri). `IStudentRosterIndex.Invalidate()`
  eklendi: uygulanan düzenleme bellekteki okumayı düşürüyor, yoksa panel "uygulandı" derken aramalar
  bir saat boyunca eski belgeden cevap verirdi. Dosya sistemi kuralları iki katalog için
  `EditableDocumentFile`'da ortaklandı.
- **Changed (küçük):** `discoveryFolderId` artık kaynak form düzenleyicisinde de bir alan (JSON
  sekmesinden zaten geçiyordu, formda görünmüyordu) ve riskli olarak işaretli.
- **Tests executed:** `dotnet build` temiz; `dotnet test Sirkadiyen.slnx` 0 başarısız (Infrastructure
  797, Api 11, Contracts 6, Persistence 40 geçti / 236 atlandı — Docker kapalı). Yeni
  `StudentRosterCatalogEditingServiceTests` 13 test. Web: `tsc --noEmit` temiz, `vitest run` 19 dosya
  / 92 test geçti (yeni `RosterCatalogEditor.test.tsx` 7 test), `next build` başarılı,
  `/admin/rosters` üretiliyor.
- **Not done / not verified:** Tarayıcıda doğrulanmadı — sayfa oturum, API ve PostgreSQL gerektiriyor,
  bu makinede veritabanı yok. İki migration (`AddSourceDiscoveryFolder`, 
  `AddStudentRosterCatalogRevisions`) yazıldı ama uygulanmadı; ikisi de deploy hattının migration
  adımıyla gidecek. Pipeline `student-rosters.json`'ı sunucuya seed etmiyor (yalnız
  `schedule-sources.json`'ı ediyor); temiz sunucuda depo kopyası bir kez elle kurulmalı, bu
  `deploy/README.md`'ye yazıldı. Panel düzenlemesi listelerin **içeriğini** tazelemez, yalnız
  katalogu; Google'da düzenlenen bir liste için hâlâ bir saatlik yenileme geçerli.

## Takılı kalan revizyon süpürülüyor, inceleme ekranı açıklıyor (ADR-135) (2026-08-30)

- **Root cause (iki ayrı şey):** (1) `ValidatePendingAsync` güvenlik ağı olarak yazılmış ama
  **hiçbir yerden çağrılmıyordu**; poller doğrulamaya varamadan kesilen bir döngü revizyonu
  kalıcı olarak `Parsed`'da bırakıyordu. (2) `SHARED-AMPHI` bir companion; tasarımı gereği hiç
  candidate yayımlamaz, boş revizyon da her zaman reddedilir — ama hiçbir ekran bu boşluğun
  beklenen ve kalıcı olduğunu söylemiyordu, dolayısıyla "amfi programı hiç yayımlanmıyor" hata gibi
  okunuyordu. Amfi programının etkisi zaten yıllık kaynakların etkinliklerine yazılan oda bilgisi.
- **Changed:** Yeni `RevisionValidationTask` her döngüde, yayımlamadan **önce** koşuyor (kurtarılan
  revizyon aynı turda yayımlansın diye) ve bulduğunu warning olarak logluyor. Boş revizyon bulgusu
  artık iki durumu ayırıyor ("daha önce yayımlamış" / "hiç yayımlamamış — companion olabilir").
  `ScheduleRevisionSummary` kaynağın adı, kohortu, akademik yılı, hata/uyarı sayıları ve
  **yayımdaki revizyonun kayıt sayısı** ile genişletildi; hepsi tek sorguda (join + üç ilişkili
  alt sorgu) projeksiyonlanıyor. Frontend'de yeni `RevisionFindings` modülü: her kural Türkçe
  etiket + "ne bakıldı / genellikle ne demek / onaylarsan ne olur" açıklaması, durum açıklamaları,
  ve kanıtın tablo olarak render'ı. Satır başlığı artık kaynak adı, kohort, tarih, rozetler ve
  "yürürlükteki revizyon 180 kayıt taşıyor: bu revizyon 16 dersi kaldırıyor" cümlesini gösteriyor.
- **Bulunan ikinci hata:** Revizyon geçmişini kaynağa göre filtreleme (`ListRecentAsync(sourceId)`)
  `revision.SourceId.Value` ile karşılaştırıyordu — value converter'ın içine uzanan bu ifade EF
  tarafından çevrilemiyor, yani o sorgu hiç çalışmamış, çalıştırılsa runtime'da patlardı. Value
  object üzerinden karşılaştırmaya çevrildi; ayrıştırılamayan kimlik hata değil "boş sonuç".
- **Tests executed:** `dotnet test Sirkadiyen.slnx` 0 başarısız (Infrastructure 802, Api 11,
  Contracts 6, Persistence 40 geçti / 236 atlandı — Docker kapalı). Yeni
  `ScheduleRevisionReadStoreTranslationTests` (4 test) projeksiyonları veritabanısız çeviri
  testinden geçiriyor — yukarıdaki hatayı bu yakaladı. Web: `tsc --noEmit` temiz,
  `vitest run` 19 dosya / 94 test (RevisionReview'a iki yeni test: bağlam satırı ve
  `[object Object]` regresyonu), `next build` başarılı.
- **Not done / not verified:** Tarayıcıda doğrulanmadı — ekran oturum, API ve PostgreSQL istiyor.
  Companion'ın haftalık boş revizyonu hâlâ reddedilenler kuyruğuna düşüyor (haftada ~1 satır);
  bastırmak için katalogun "bu kaynak yayın yapmaz" demesi gerekir, o ayrı bir iş. Persistence
  testleri CI'da da atlanıyor (bağlantı dizesi ayarlı değil), bu yüzden store projeksiyonlarının
  güvencesi çeviri testi kadar.

## Amfi odalarının Dönem 3'te görünmemesinin sebebi: belgeler Drive çöp kutusunda (2026-08-30)

- **Root cause (üretim, kod değil):** `G3-EN-ANNUAL`, `G3-TR-A-ANNUAL`, `G3-TR-B-ANNUAL` belgeleri
  Drive'da çöpe taşınmış. Worker 26 Ağustos'tan beri her döngüde
  `DriveDocumentException ... is in the trash` alıyor, `LastPolledAtUtc` yalnız başarılı
  edinmede yazıldığı için üçü de 2026-08-26 06:15'te donmuş, yayımdaki revizyonları 25-26 Ağustos —
  yani amfi companion'ı var olmadan öncesine ait. Oda bu yüzden yok. Boru hattı doğru davrandı:
  çöpteki dosyayı okumayı reddetti.
- **Doğrulandı:** Aynı 2 Eylül sorgusunda `G2-TR-ANNUAL` 7 dersin 5'ine amfi yazmış — parser,
  companion, birleştirme ve yayın zinciri üretimde çalışıyor.
- **Görünürlük boşluğu (açık):** Edinme parse'tan önce başarısız olduğu için ortada parse run da
  revizyon da yok; panelin kaynak durumu ekranı yalnız bayatlayan bir `LastPolled` gösteriyor,
  başarısızlığın kendisini ve sebebini gösteremiyor. Dört gün boyunca yalnız journald'a yazıldı.
  Son poll hatasını kaynak satırına yazıp panelde göstermek ayrı bir iş olarak duruyor.
- **Aynı imzayla düşen diğerleri:** `G2-VERTICAL-AUTUMN`, `G2-VERTICAL-SPRING`,
  `G3-FACULTY-LOCATIONS` — hiç başarılı poll edilmemişler.

## Dönem 3 belgeleri yenilendi; poll hatası panelde; deploy katalogu kuruyor (2026-08-30)

- **Root cause (üretim):** Dönem 3'ün üç yıllık belgesi Drive çöp kutusundaydı; worker 26
  Ağustos'tan beri her döngüde `DriveDocumentException ... is in the trash` alıyordu. Amfi
  birleştirmesi çalışıyordu — aynı gün G2-TR'nin 2 Eylül derslerinin 5'inde oda yazılıydı — Dönem 3
  kaynakları dört gündür hiç yeniden okunamadığı için onların yayımdaki revizyonu companion'dan
  önceye aitti.
- **Changed (katalog):** `G3-TR-A-ANNUAL`, `G3-TR-B-ANNUAL`, `G3-EN-ANNUAL` yeni Google E-Tablo
  belgelerine çevrildi; taşıma `googleDriveFile`/`xlsx` → `googleSheets`/`googleSheet` ve gid'ler
  eklendi. Hangi linkin hangi kohort olduğu tahmin edilmedi: üç belge tarayıcıdan açılıp
  başlıklarından ("Dönem 3 A", "Class 3 English", "Dönem 3 B") doğrulandı — A ile B'yi karıştırmak
  sınıfın yarısına diğer yarısının programını verirdi. Katalog testi üç kaynağın ayrı belge
  kullandığını da pinliyor.
- **Changed (ADR-137, görünürlük):** `schedule_sources`'a `LastPollFailureAtUtc` +
  `LastPollFailureReason`; başarılı poll temizliyor, son başarılı poll zamanı korunuyor. Worker'ın
  zaten loglayan catch bloğu artık kaydediyor (kendi hatası döngüyü durdurmuyor). Panelde tablonun
  üstünde uyarı bandı, satırda "4 gündür alınamıyor", detayda edinicinin cümlesi ve "aşağıdakiler
  son başarılı okumaya ait" notu.
- **Changed (ADR-138, deploy):** Worker açılışta kendi sürümüyle gelen katalogu
  `ScheduleSourceCatalogEditingService` üzerinden kuruyor — aynı doğrulama, aynı atomik yazma, aynı
  kaynak upsert'ü, aynı sürüm geçmişi; yeni `Deployment` revizyon türü, gerekçe olarak release SHA.
  Aynı belge geliyorsa hiçbir şey yazılmıyor. Depodaki katalog artık gerçeğin kaynağı; panelde
  yapılıp depoya işlenmeyen düzenlemeyi bir sonraki deploy değiştirir (kaybolmaz, revizyon eski
  belgeyi bütünüyle taşır) — bu, katalog editörünün geçmiş sekmesinde de yazıyor.
- **Changed (ADR-136):** `DiscoveryFolderId` katalogdan çalışan veritabanına hiç kopyalanmıyordu
  (`ConfigurationOf` listesinde yoktu); eklendi ve `ScheduleSource`'un her alanının ya kopyalananlar
  ya da "satırın kendisine ait" listesinde olmasını şart koşan veritabanısız bir koruma yazıldı.
  Koruma aynı turda yeni eklenen iki poll-hatası alanı için de karar vermeye zorladı.
- **Tests executed:** `dotnet test Sirkadiyen.slnx` 0 başarısız (Infrastructure 808, Api 11,
  Contracts 6, Persistence 40 / 236 atlandı). Web: `tsc --noEmit` temiz, `vitest run` 19 dosya /
  95 test, `next build` başarılı. `dotnet ef migrations script --idempotent` üç bekleyen
  migration'ı da üretiyor.
- **Not done / not verified:** Tarayıcıda doğrulanmadı (oturum + API + PostgreSQL gerekiyor).
  Migration'lar uygulanmadı; deploy hattının migration adımıyla gidecek. `G2-VERTICAL-AUTUMN`,
  `G2-VERTICAL-SPRING`, `G3-FACULTY-LOCATIONS` belgeleri de çöpte — yeni linkleri verilmedi, o üç
  kaynak hâlâ düşüyor (artık panelde görünecek).

## 2026-08-31 — Sıra dışı tarihler okunuyor, kalanlar operatöre soruluyor (ADR-139)

- **Problem:** `G1-TR-PRACTICE` dört dersi akademik yıl dışına yazıyordu ve panelde yapılabilecek
  hiçbir şey yoktu. Onaylamak dersi altı yıl geriye koyuyor, reddetmek bütün Dönem 1 pratik
  programını bizim düzeltemeyeceğimiz bir belge için bekletiyordu.
- **Added (parser):** `normalization/date_sequence.py` — bir tarih sütununu kronolojik dizi olarak
  okur, en uzun azalmayan alt diziyi çıpa alır, dışında kalanı şüpheli sayar ve **yalnızca yılı
  değiştirerek** onarır. Onarım, tek bir aday akademik yılın içine ve gerçek iki çıpanın arasına
  (en fazla 60 gün açıklık) düşerse uygulanır. Hücrenin yazdığı gün adı iki yönlü karar verir: yılla
  çelişiyorsa onarım yapılmaz, uyuyorsa çıpaların yerini tutar. Dört tarihten kısa ya da %80'i sıralı
  olmayan dizi hiç okunmaz — bu güvenlik supabı, analizin her profilde açık olmasını güvenli kılıyor.
- **Added (parser):** `parsers/date_repair.py` — profillerin çağırdığı ortak yüzey: operatör
  düzeltmelerini uygular, analizi çalıştırır, onarımları ve geri çevirdiği önerileri tek bir şekilde
  raporlar. Sekiz profilin hepsi bağlandı (`annual`, `practice`, `practice_slots`, `anatomy`,
  `vertical_corridor`, `faculty_practice`, `bedside`, `amphitheatre`).
- **Changed (parser):** `ParserWarning` yapılandırılmış `detail` taşıyor — operatörün üzerine
  basacağı aday tarihler bir cümleye sığmıyor. `practice_slots` slot başlıklarını çözmeden önce
  parçalarına ayırıyor (`_read_slot_header`) ve diziyi **sütun sırasına göre değil slot numarasına
  göre** kuruyor; aynı numarayı taşıyan başlıklar kendi tarihlerine göre sıralanıyor. Sütun sırasını
  tarih sırası saymak `G2-TR-PRACTICE`'te üç sağlam tarihi anomali diye raporluyordu.
- **Sonuç (gerçek belgeler):** Altı fixture'da yedi hatalı tarih doğru okunuyor. `G1-TR-PRACTICE`'in
  üç 2020-11-20 dersi 2026-11-20'ye taşındı; `G2-ANATOMY-SPRING` üç diseksiyon saati,
  `G2-TR-PRACTICE` üç ders, `G2-VERTICAL-AUTUMN` bir ders kazandı (bunlar önce bütünüyle
  reddediliyordu); `G1-TR-ANNUAL`'da daha önce hiç görülmemiş bir 2027-11-30 yazım hatası bulundu.
  Dört yeni uyarı da gerçek: `G2-EN-ANNUAL`'da bir gün geriye yazılmış üç öğle arası,
  `G3-TR-B-ANNUAL`'da başlıksız bir satırın kayıp tarihi.
- **Added (.NET):** `ScheduleSourceDateCorrection` varlığı + `schedule_source_date_corrections`
  tablosu ve migration'ı, `IScheduleSourceDateCorrectionStore`, poller entegrasyonu (düzeltmeler
  `ParseSourceContext`'e giriyor **ve** parse-run parmak izinin parçası, yoksa değişmemiş belge
  "zaten ayrıştırıldı" diye kısa devre yapardı), `ParserDateAnomalyReader`, yeni doğrulama kuralı
  `RecordDateOutOfSequence` (onarım = uyarı, geri çevrilen = hata), iki yeni denetim kategorisi ve
  `/api/admin/sources/{id}/date-corrections` uçları (GET/POST/DELETE, gerekçe zorunlu, denetlenir).
- **Added (web):** İnceleme ekranında kural Türkçe açıklanıyor ve geri çevrilen her tarih için
  parser'ın ürettiği okumalar birer düğme olarak sunuluyor. Kabul etmek revizyonu kapatmıyor —
  kaynağı kalıcı düzeltiyor — ve ekran bunu açıkça söylüyor.
- **Versions:** Parser engine 0.4.0. Tarih okuyan her profil bump'landı
  (`grade1_yearly_v1` 1.8.0, `grade1_practice_v1` 1.2.0, `grade2_yearly_v1` 1.4.0,
  `grade2_practice_v1` 1.4.0, `grade2_anatomy_*` 1.2.0, `grade2_vertical_corridor_v1` 1.2.0,
  `grade3_yearly_v1` 1.4.0, paylaşılan `_PROFILE_VERSION` 1.1.0). `grade3_faculty_locations_v1`
  1.0.0'da pinlendi: belgesinde hiç tarih yok.
- **Tests executed:** Parser `pytest` 583 test geçti (golden dosyalar bilinçli olarak yenilendi,
  diff gözden geçirildi); `ruff check` ve `ruff format --check` temiz. `dotnet test Sirkadiyen.slnx`
  0 başarısız (Infrastructure 820, Api 11, Contracts 6, Persistence 40 / 236 atlandı). Web:
  `tsc --noEmit` temiz, `vitest run` 19 dosya / 97 test.
- **Not done / not verified:** Tarayıcıda doğrulanmadı (oturum + API + PostgreSQL gerekiyor).
  Migration uygulanmadı. Belgesi düzeltilen bir düzeltme katalogda hiçbir şeye uymadan kalıyor;
  her ayrıştırmada bildiriliyor ama otomatik emekliye ayrılmıyor.

## 2026-08-31 — Google Takvim tavsiyesi + mobil panel sırası (ADR-140)

- **Added (web):** `web/src/components/GoogleCalendarAppLinks.tsx` — tavsiye metni ve iki resmî
  mağaza bağlantısı (Google Play `com.google.android.calendar`, App Store `id909319292`), `card` ve
  `plain` varyantları, bağlantılar `target="_blank" rel="noreferrer noopener"`.
- **Changed (web):** Panel (`app/dashboard/page.tsx`) her kartı `.dash-cell dash-cell--*`
  sarmalayıcısına aldı ve uygulama kartını sağ sütuna ekledi; ilk senkronizasyon ekranı
  (`app/onboarding/sync/page.tsx`) bekleme sırasında aynı bileşeni gösteriyor.
- **Changed (css):** `globals.css` — `.store-badges`/`.store-badge` rozetleri (480px altında tek
  sütun) ve 900px altında `.dashboard-grid > .stack { display: contents }` + `.dash-cell` `order`
  sırası: profil, özet, sıradaki dersler, değişiklikler, uygulama, takvim bağlantısı, lisans,
  renkler, onarım, yakında, hesap silme. Masaüstü düzeni değişmedi.
- **Tests added:** `GoogleCalendarAppLinks.test.tsx` (3 test: tavsiye metni, iki mağaza adresi,
  yeni sekme/`noopener`, `plain` varyantında başlık yok) ve
  `app/onboarding/sync/page.test.tsx` (senkronizasyon ekranında tavsiye + iki bağlantı). Panel
  testine her kartın ayrı bir `.dash-cell--*` kancası taşıdığını doğrulayan bir test eklendi.
- **Tests executed:** `vitest run` 21 dosya / 102 test geçti; `tsc --noEmit` temiz.
- **Not done / not verified:** Gerçek bir telefonda tarayıcı doğrulaması yapılmadı (oturum + API
  gerekiyor). `next lint` bu depoda yapılandırılmamış (etkileşimli kurulum soruyor), çalıştırılmadı.

### Ek: mağaza rozetleri (aynı gün)

- **Added (web):** `public/store/app-store-badge.svg` (Apple'ın kendi dosyası, developer.apple.com),
  `public/store/google-play-badge.svg` (İngilizce tek dilli Play rozeti) ve kaynak/uyarıları
  anlatan `public/store/README.md`.
- **Changed (web):** `GoogleCalendarAppLinks` artık kendi çizdiği düğmeleri değil resmî rozet
  görsellerini kullanıyor (`next/image`, `unoptimized`); CSS'te `.store-badge` sade bir bağlantıya
  indi, siyah gövdeler `--play` 52px / `--apple` 40px ile eşitlendi.
- **Doğrulama:** Rozetler ve kart headless Chromium'da gerçekten render edilip görüntü olarak
  incelendi (dosya olarak ve `globals.css` ile birlikte kart hâlinde). `vitest run` 21 dosya /
  103 test; `tsc --noEmit` temiz.
- **Bilinen kısıt:** Play rozeti Google'ın sunucusundan indirilemedi (ağ politikası); MIT lisanslı
  bir paketten ayıklandı, görünüm aynı, dosya birebir Google'ınki değil.


## Tarih düzeltmesinde elle giriş ve düzeltme ekranı (2026-09-01)

- **Root cause:** ADR-139 kararı doğruydu ama iki ucu açık kalmıştı: (1) operatör yalnızca
  parser'ın önerdiği tarihleri seçebiliyordu, aday yoksa ekran var olmayan bir sayfaya
  yönlendiriyordu; `RecordDateOutsideAcademicYear` için hiç eylem yoktu. (2) Kaydedilmiş
  düzeltmeleri gösteren hiçbir ekran yoktu — `listSourceDateCorrections` / `retire…` istemcide
  duruyor, kimse çağırmıyordu. Ayrıntı: ADR-139 eki.
- **Changed:** `IScheduleSourceDateCorrectionStore.ListAllAsync` + store, yeni okuma ucu
  `GET /api/admin/sources/date-corrections`. Web'de `DateCorrectionAction` genelleştirildi ve her
  durumda elle tarih girişi sunuyor; `RecordDateOutsideAcademicYear` bulgusu ayrık tarih başına
  düzeltme öneriyor; yeni `SourceDateCorrections` bileşeni ve revizyonlar modülünde "Sıradışı
  tarihler" sekmesi (listele / değiştir / kaldır). Yeni tablo, yeni migration, yeni denetim
  kategorisi yok.
- **Tests executed:** .NET: `dotnet build` 0 uyarı; Infrastructure 820, Api 11, Contracts 6 test
  geçti. Web: typecheck temiz, 107 test geçti (yeni: aday yokken elle tarih, akademik yıl dışı
  tarih için ayrık tarih başına düzeltme, sekmede listeleme/değiştirme, kaldırma onayı).
- **Not done / not verified:** `Sirkadiyen.Persistence.Tests` bu makinede koşmuyor — Docker kapalı,
  `127.0.0.1:15432` yok; 237 testin tamamı bağlantı hatası (CI'da koşacak). `ListAllAsync` için
  ayrı bir persistence testi eklenmedi; mevcut store testleri kaynak bazlı okumayı kapsıyor.
  `next lint` bu depoda etkileşimli kurulum istediği için çalıştırılamadı.
- **Parked:** `feat/adr-128-unusual-dates` dalı aynı ihtiyacı kayıt bazlı override olarak çözen
  paralel bir tasarım taşıyor; merge edilmemeli (ADR-017'yi bozar), referans olarak duruyor.


## 2026-09-01 — Aynı şeyi söyleyen parse artık revizyon üretmiyor (ADR-141)

- **Teşhis:** Canlı veritabanında 173 diff'in 106'sı hiçbir şey değiştirmiyor ve hepsi
  `Dispatched`; 131 parse değişmemiş belge üzerinde yapılmış; incelemede 24 revizyon aynı üç tarih
  yüzünden kilitli. Kök neden ikili: dedup yalnızca bayt düzeyinde, ve companion `ContentHash`'i run
  kimliğinin parçası olduğu için `SHARED-AMPHI`'nin 19 değişimi yedi yıllık kaynağı yeniden parse
  ettirmiş.
- **Yapılan:** `CanonicalRecordSetHash` — her kaydın `StableIdentity` + `ContentHash`'i üzerinden tek
  digest, semantik differ'ın karşılaştırdığı şeyin aynısı. `ScheduleRevision.RecordSetHash` (yeni,
  nullable kolon; `AddRevisionRecordSetHash` migration'ı yalnızca kolon ekliyor). `CompleteAsync`
  artık `ParseCompletion` döndürüyor (`RevisionCreated` / `ParserRejected` / `UnchangedRecordSet`);
  hash kaynağın en son revizyonununkiyle aynıysa revizyon yaratılmıyor, parse run tamamlanıp
  parser yanıtını saklıyor. Poller bunu yeni `ParsedUnchanged` sonucu olarak raporluyor.
- **Log seviyesi gerçekten uygulanıyor:** Worker'a `appsettings.json` eklendi (Web SDK olmadığı için
  `None Update ... CopyToOutputDirectory`), EF `Database.Command` kategorisi `Warning`; aynı satır
  Api'nin `appsettings.json`'ına da kondu. `.env.example`'daki noktalı satır kaldırıldı —
  **systemd adında nokta olan değişkeni hatasızca atıyor**, üretimde EF her SQL cümlesini
  `Information` seviyesinde yazıyordu (günde ~420 bin satır, 4 GB journal).
- **Dokümantasyon borcu kapatıldı:** `systemPatterns.md` §4 artık parse run kimliğinin
  `(snapshot, profil, sürüm, companion fingerprint)` olduğunu ve değişmemiş snapshot'ın parse
  edilebileceğini söylüyor; `activeContext.md`'deki "SHARED-AMPHI için HTTP alımı yok" cümlesi
  ADR-133 ile eskimişti, düzeltildi; `docs/schedule-ingestion.md` boru hattı şemasına karşılaştırma
  adımı eklendi.
- **Tests executed:** `dotnet build` 0 uyarı 0 hata; Infrastructure 828, Api 11, Contracts 6 test
  geçti (yeni: `CanonicalRecordSetHashTests` 7 test, poller'ın `ParsedUnchanged` raporu).
- **Not done / not verified:** `Sirkadiyen.Persistence.Tests` bu makinede yine koşmuyor — Docker
  kapalı, `127.0.0.1:15432` yok. Yeni iki store testi (`AReParseSayingTheSameThingCreatesNoSecond
  Revision`, `AReParseWhoseRecordsMovedStillCreatesARevision`) ve migration bu yüzden gerçek
  PostgreSQL'de doğrulanmadı; CI'da ya da Docker açıkken koşturulmalı.
- **Bilerek ertelendi:** Companion fingerprint'i daraltmak (bir amfi değişimi yalnızca gerçekten
  dokunduğu bağımlıları geçersiz kılsın). Geriye kalan maliyet 131 gereksiz parse; parse ucuz,
  değişiklik ADR-102/126/139'un üçüne birden dokunuyor.

## 2026-09-01 — İnceleme kuyruğu insanın karar verebileceği şeylere indi; boru hattı artık neyi beklediğini söylüyor (ADR-142, ADR-143)

- **`ParseCollapse` (ADR-142).** Yayımdaki revizyonla **tek bir kaydı bile** ortak olmayan revizyon
  artık `ReviewRequired` değil `Rejected`. Üretimdeki 17 `MassDeletion` kilidinin onu "935 of 935
  (100.0 %) absent" diyordu — bu bir silme değil, yanlış belgenin okunması. Sınır tam olarak sıfır
  kesişim: bir tek ders hayatta kaldıysa bu bir düzenlemedir, `MassDeletion` olarak eskisi gibi
  incelemeye gider. Kaynak eşiği (`MinimumDeletionCount`) korunuyor, küçük kaynaklar etkilenmiyor.
  Panelde kendi açıklaması var; `MassDeletion` metni de artık kesişimsiz durumun ayrı raporlandığını
  söylüyor.
- **Durak gözcüsü (ADR-143).** `PipelineStallWatch` her poll döngüsünün sonunda beş koşulu ölçüyor:
  incelemede bekleyen revizyonlar (48s), hiç doğrulanmamış revizyonlar (2s), tutulan diff'ler (24s),
  pes etmiş dispatch'ler (anında), ve **eskiden alınırken artık alınmayan** kaynaklar (12s). Sadece
  takılan varsa yazıyor; sayı, en eskisinin yaşı ve bakılacak kaynak tek satırda. Hiçbir şeyi
  iptal etmiyor, onaylamıyor, süresini doldurmuyor — tek eksik olan şey söylemekti.
  Eşikler `SIRKADIYEN_STALL__*` ile ayarlanıyor.
- **Yanında çıkan gerçek kusur:** `ValidatePendingAsync` en eskiden başlayan kuyruğunu hata
  yalıtımı olmadan işliyordu. Doğrulaması istisna atan **tek** bir revizyon, arkasındaki her
  revizyonu kalıcı olarak `Parsed`'da bırakıyordu — kuyruk her döngüde aynı uçtan okunduğu için hata
  tekrarlanmakla kalmıyor, kalıcı hâle geliyordu. Artık revizyon başına raporlanıyor ve parti devam
  ediyor (diff aşaması bunu zaten yapıyordu; doğrulama yapmıyordu ama dokümantasyonu yaptığını
  söylüyordu). Üretimdeki 20 `Parsed` SHARED-AMPHI revizyonunun en olası açıklaması bu.
- **Tests executed:** `dotnet build` 0 uyarı 0 hata; Infrastructure 835 test geçti (yeni:
  `ParseCollapse` için 3 test, `PipelineStallWatch` için 4 test). Web: `tsc --noEmit` temiz,
  107 test geçti.
- **Not done / not verified:** Persistence testleri yine koşmadı (Docker kapalı). Yeni
  `PipelineStallReadStoreTests` tam da bunun için yazıldı — beş sorgunun **çevrilebildiğini** boş
  şemada kanıtlıyor; değer nesnesi `SourceId` projeksiyonu ve kaynak başına "en son snapshot" alt
  sorgusu çalışmadan doğrulanmadı, CI'da koşmalı.
- **Yapılmadı — ayrı bir karar gerekiyor:** `Superseded` revizyonların canonical kayıtlarının
  silinmesi (veritabanının %76'sı). `schedule_diff_entries` bu kayıtlara **RESTRICT** yabancı
  anahtarla bağlı ve diff'ler ADR-060 replay'inin silme yetkisi; dolayısıyla saklama politikası
  "kayıtları sil" değil, "diff'i de sil" demek ve en eski replay imlecinden daha yeni hiçbir diff
  silinemez. Bu, kendi ADR'sini hak eden bir tasarım kararı; bu oturumda yapılmadı.