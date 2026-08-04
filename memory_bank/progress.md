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
- [ ] Add third-year source fixtures
- [ ] Add weekly amphitheatre fixtures
- [ ] Document every source
- [x] Add confirmed mixed-transport source catalog
- [x] Implement Google Sheets client
- [x] Implement Google Drive client (Drive v3 REST download, verified, DOCX only — ADR-083)
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
- [x] Admit newly queued Calendar work independently of the adaptive source-polling
  delay, including an initial-sync request created while the worker is idle (ADR-082)
- [x] Add the 45-department faculty catalog, admin color defaults, per-user color
  overrides, audited persistence and inventory-driven recoloring (ADR-086)

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
- [ ] Revision diff viewer
- [ ] Manual publish and reject
- [x] User sync status backend (`GET /api/admin/users(+detail)`: profile, licenses,
  managed-event count, onboarding state, recent sign-ins, ADR-089)
- [ ] Retry failed jobs
- [x] Audit log viewer backend (`GET /api/admin/audit`, `GET /api/admin/access-logs` with
  masked IP + audited unmask, ADR-089)
- [x] Health checks (API and internal Worker `/health/live` + `/health/ready`, parser `/health` probe, ADR-089/091)
- [x] Metrics (`GET /api/admin/metrics` JSON operational-count snapshot, ADR-089)
- [~] Structured logs (correlation-id middleware stamps every request/log line; a full
  structured-logging/OpenTelemetry stack is still pending)
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
- [~] Admin/operator interfaces (freeze including class/program scopes, source warning
  evidence, user/license administration, audit/access logs and worker/parser health are
  wired; held-diff release and endpointless product domains remain)
- [x] Administrative document upload UI, driven by `GET /api/sources/uploadable` (ADR-081)
- [x] Component system / design system (ported the Wise-inspired prototype design
  system into `web/src/app/globals.css` + shared `web/src/components/ui.tsx`; light
  theme, tokens, two densities; Tailwind deliberately not added)
- [x] Public + legal surface (landing `/`, `/gizlilik`, `/kosullar`, `/iletisim`)
  and re-skinned onboarding/dashboard/admin against the design system
- [x] Automated frontend tests (Vitest + React Testing Library, ADR-090)
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
signal; the converted content hash is. A poll now separates `UnsupportedTransport` from
`UnsupportedDocumentFormat`, which is what the Drive-published Grade 3 workbooks report.

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
(ADR-083). An HTTP acquisition adapter, a workbook converter for the Drive-published
Grade 3 sources, and the remaining source fixtures/parser profiles are still required.
DOCX conversion, administrative DOCX acquisition and Drive DOCX acquisition are
implemented.

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
