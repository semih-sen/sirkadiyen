# Active Context

## Current phase

The identity and activation foundation is implemented: Google ID credentials are
verified server-side, users are persisted, browser sessions use secure cookies
with CSRF protection, single-use licenses activate accounts transactionally, and
administration is authorized by the explicit `SuperAdmin` role. The validated
student profile is implemented (ADR-055): an activated account now derives
`ProfileRequired`, then `CalendarAuthorizationRequired` once a profile exists. The
profile also carries the university student number, validated for its ten-digit
format and cross-validated so its faculty and program-language digits match the
selected program (ADR-056). Calendar authorization is implemented (ADR-057): a
separate, minimally scoped offline consent exchanged server-side for a refresh token
that is encrypted at rest, advancing an authorized account to `ReadyForInitialSync`.
Per-user initial sync is now implemented (ADR-058): the user starts it, the worker creates
their dedicated calendar (ADR-024), writes every currently-published event that applies to
their profile with idempotent, resumable Calendar writes, and onboarding reaches `Active`.
Diff-driven incremental sync is now implemented too (ADR-059): a worker stage dispatches every
`Ready`/`Released` diff into per-user insert/patch/delete operations, resumable and idempotent
via the same deterministic-id + ledger machinery, so a diff finally reaches a calendar.
Re-authorization catch-up is now implemented (ADR-060): a completed-sync user whose
credential dies retains the earliest missed-diff boundary through re-authorization, and
the freeze-gated worker replays only globally `Dispatched` `Ready`/`Released` semantic
diffs after that cursor. Calendar reconciliation is now complete (ADR-062 through
ADR-064): a periodic three-way inventory repairs missing/stale managed events and ledger
rows without inferring deletions, marker-based initial-sync recovery reattaches a single
orphaned app-created calendar, and a PostgreSQL advisory fence serializes global dispatch,
semantic replay and inventory across worker instances. Intra-diff quota-aware batching is
implemented too (ADR-065): a large dispatchable diff yields after a configured number of
per-user mutations and resumes from the converged ledger without treating quota yield as a
failure. Calendar job admission is now independent from adaptive source polling
(ADR-082): newly queued initial sync, diff dispatch and reconciliation work is found
within the five-second idle-check interval even when the next source poll is an hour away.

The Google Sheets path now runs end to end from catalog seeding to a stored
diff: adaptive polling, immutable snapshot storage, strict parser HTTP calls,
parse-run persistence, transactional candidate revision creation, validation,
publication, and semantic diff calculation. A healthy revision reaches
`Published` with no human involved; a quarantined one waits for a named
approver.

Publishing a revision now leaves a durable record of what it changed. The diff
is calculated after publication in its own transaction, driven by revision state
rather than by whoever published (ADR-039), and a unique index on the current
revision makes a retried calculation idempotent. A diff is created `Ready` or
`Held`: any ambiguous entry, or a mass deletion over the configured thresholds,
holds it and no calendar operation may be derived from it (ADR-040). A held diff
reaches dispatch only through a named operator releasing it over the internal
API, which moves it to `Released` and keeps the reason it was held; an ambiguity
hold is never releasable and is corrected at the source (ADR-042).

The incremental dispatcher now consumes every dispatchable diff and resolves the
affected completed-sync users before applying idempotent Calendar operations.
The Grade 2 annual program now parses too: one profile, `grade2_yearly_v1`, serves
the Turkish and the English source, deferring the dissection group rotation to the
anatomy sources (ADR-073). The Grade 2 Turkish and English practice tables parse
through the shared slot-column reader, `grade2_practice_v1` 1.2.0 (ADR-074,
ADR-084). **A Word document now converts onto
the same normalized snapshot contract as a workbook** (ADR-076), and the vertical-corridor
skill-practice calendar is parsed from it by `grade2_vertical_corridor_v1` (ADR-077), as
are the anatomy group lists by `grade2_anatomy_{autumn,spring}_v1` (ADR-078). **Every
Grade 2 source now has a parser profile.** Every Grade 2 source is now catalogued too:
the anatomy documents are handed out with no URL, so they are declared under the
`administrativeUpload` transport and name themselves with a URN instead of claiming a
location (ADR-079). **A Grade 2 Turkish student can now onboard**, with `practiceGroup`,
`practiceSubgroup` and the independent `anatomyGroup`. **Administrative acquisition is
implemented** (ADR-080): an administrator uploads a handed-out document over the API, one
upload serves every source that document serves, and the worker parses it under the same
rules as a polled source — so the anatomy pair can now produce real revisions for both
programs. **Google Drive acquisition is implemented too** (ADR-083): the two
vertical-corridor calendars are downloaded over the Drive v3 REST API with the shared
read-only source credential, checked against what Drive states about the file, and
converted onto the same normalized snapshot a sheet produces — so **every Grade 2 Turkish
source can now be acquired**. HTTP acquisition for `SHARED-AMPHI`, a workbook converter for
the Drive-published Grade 3 sources, Grade 2 English onboarding/audience completion,
and the remaining operational surfaces still do not exist. The English practice
source itself is implemented and declares `İ1`/`İ2`; onboarding stays closed until
group-labelled annual rows and the shared vertical-corridor source have safe English
audience handling (ADR-084). The **consumer frontend now
has a runnable foundation** (`web/`, ADR-066): Google sign-in, license redemption,
academic profile, Calendar authorization, and initial-sync progress are wired to
the existing APIs and gated by authoritative backend onboarding state. The admin
panel now covers the operational freeze, the revision review queue and
administrative document upload (ADR-081); the remaining operator surfaces — source
status, diff release, license administration, audit inspection — and a real
component system are still absent, as are automated frontend tests.

The source credential is resolved: a Google service account is configured and the
worker can reach the real Sheets API.

Product decisions that previously blocked identity and synchronization design
are now accepted: single-use licenses with sync suspension on revocation
(ADR-022), HTTP-only secure-cookie sessions (ADR-023), one dedicated managed
calendar per user (ADR-024), mandatory review thresholds (ADR-025), adaptive
Istanbul-time polling (ADR-026), validated JSONB profile selectors (ADR-027),
validation severity and thresholds (ADR-029), PDÖ exclusion (ADR-030), subgroup
widening at synchronization time (ADR-031), automated publication with a named
approver for held revisions (ADR-032), forward-fix without rollback (ADR-033),
a global freeze (ADR-034), secondary matching (ADR-035), Next.js (ADR-036),
Hangfire (ADR-037), and recurring-undated-row exclusion (ADR-038).

## Latest implementation session

- **Migrated the `web-design/` HTML/CSS/JS prototype into the Next.js app (`web/`).**
  The prototype's Wise-inspired design system is now production CSS in
  `web/src/app/globals.css` (light theme, brand tokens, four OKLch semantic roles,
  two densities: student 22px radius / admin 8-16px), replacing the earlier dark
  skeleton. Shared building blocks live in `web/src/components/ui.tsx`
  (`Brand`, `SiteNav`, `SiteFooter`, `StudentTopbar`, `AuthShell`, `Stepper`,
  `Banner`, `ImplNote`) and `web/src/components/{AdminShell,LegalDocument}.tsx`.
- **Wired flows were re-skinned, not rewritten:** sign-in, the four onboarding
  steps, suspended, dashboard and `/admin` keep their existing authoritative-backend
  integration (onboarding state, CSRF client, freeze, revision review, document
  upload, self-activation). New public/legal pages added at `/` (landing),
  `/gizlilik`, `/kosullar`, `/iletisim`. Turkish app-route slugs from the plan
  (`/kurulum`, `/panel`, `/yonetim/*`) were **not** adopted — renaming live routes
  is out of scope for a re-skin, so `src/lib/onboarding.ts` `ROUTES` is unchanged.
- **No fabricated data.** Screens with no backend (finance, users, bulk-event,
  user-warning, server, access-logs, source dashboard; dashboard's upcoming-lessons
  /changes/history/notifications; contact submit) are shown as explicit "Yakında"
  placeholders or omitted, never with invented metrics (plan constraint K8). The
  admin sidebar renders those IA entries as disabled "Yakında" items (no dead
  links). The sync screen still shows %100 only on a backend `Completed`.
- **Every unconnected route/module is inventoried in `web/GAPS.md`**, split into
  "no endpoint", "endpoint exists / UI not built" (held-diff release, license
  create/revoke — cheapest next steps), and "contract gap" (sync per-stage
  counters, license expiry/trial detail).
- **Verified:** `npm run typecheck` clean and `npm run build` succeeds (13 app
  routes). No backend, `.NET` or Python code changed. Automated frontend tests are
  still absent (no runner configured). Fonts (Manrope/Inter) load via a runtime
  stylesheet link, so the build has no font-fetch step.

## Previous Grade 2 English practice session

- **Implemented the Grade 2 English practice source (ADR-084).** The fixture was
  classified as 2024-2025 from its filename, but its 39 dated practice slots run from
  17 September 2025 through 22 May 2026. The only `2024` cell is inside an anatomy row
  the practice profile already excludes in favour of the anatomy source, so it is not
  a practice-program date defect.
- `grade2_practice_v1` is now 1.2.0 and serves both programme languages without
  mixing their cohorts: Turkish accepts the bounded `A`-`H` grammar, English accepts
  the independent `İ1`/`İ2` values written as `i1`, `i2`, and `i1+i2`.
- The slot reader now handles the two complete but compact spellings in the real
  workbook — `23Aralık 2025` and one date/time written on a single line — by
  separating stated components, never correcting or inferring a date.
- The real normalized fixture and golden parse are committed evidence. The English
  parse publishes 49 candidates (45 practice, four examination), spans
  2025-09-17 through 2026-05-22, and contains 24 selectors for each of `İ1` and
  `İ2`. Thirty-five `*` cells remain explicitly deferred, five anatomy rows and
  two PDÖ rows remain out of scope, and one audience-free `TELAFİ` is refused.
- **Open risk.** This closes the current-year practice-fixture blocker, not Grade 2
  English onboarding. The annual workbook embeds `İ1`-`İ5` in lesson titles without
  canonical audiences, and the shared vertical-corridor documents have only Turkish
  catalog/parser audience handling. Grade 2 English remains outside schema 1.1 until
  those paths are made complete; the English anatomy revisions therefore still have
  no users.
- 417 Python tests and 546 .NET tests pass with no skips. Ruff format/check, MyPy,
  `dotnet format --verify-no-changes`, and `git diff --check` are clean. The first
  targeted .NET run could not build because the running Debug worker held its copied
  application DLL; the worker was left running and the Release test run passed.

## Previous Drive acquisition session

- **Implemented Google Drive acquisition for the vertical-corridor documents
  (ADR-083).** They were the last Grade 2 Turkish sources with no way to be read: ADR-076
  converted them and ADR-077 wrote the profile that parses them, but the poller answered
  `UnsupportedTransport` for anything that was not Google Sheets, so a working profile ran
  against nothing. Unlike the anatomy documents, these are edited by Student Affairs during
  the year and have a published location, which is why they are fetched rather than
  uploaded.
- **A typed `HttpClient` against the Drive v3 REST API, not the Drive client library.**
  Two calls are needed; the library would add a package, a service object and a second way
  of holding the credential to reach them. `Google.Apis.Auth` is kept for minting and
  refreshing the token, which is the part worth not writing.
- **One credential for every fetched source**, read-only over Sheets and Drive
  (`GoogleSourceCredentialFactory`). The access token is attached by a delegating handler,
  so it is never held by the client that builds the request and cannot reach a log line.
- **Metadata first, then content, and the download is trusted only after it is checked.**
  A trashed file, a document converted into a Google Doc, a payload over 8 MB, bytes that
  do not match the stated length or digest, and anything that is not an Office container
  are each refused with a message naming what a person has to do. Everything else stays an
  ordinary HTTP error the next poll retries. Google's error body is never repeated into a
  message: it can name the file, its owner and the principal.
- **The snapshot states that it was downloaded and nothing else about the file.**
  Acquisition diagnostics are part of the content hash, so recording a name, a
  modification time or a digest would make an unedited re-save look like a change and
  produce a revision that changes nothing every poll. For the same reason Drive metadata is
  not used as a change signal at all — an open question since the inventory, now closed:
  the converted content hash is better, because it ignores edits that alter no text.
- **A missing transport and a missing reader are now separate poll outcomes.** The Grade 3
  workbooks share this transport and are downloadable, but nothing converts a workbook from
  bytes, so they report `UnsupportedDocumentFormat`; `SHARED-AMPHI` still reports
  `UnsupportedTransport`. Following ADR-079's precedent, both mean "nothing was read" and
  they need different work.
- Covered by 24 new tests: the client's happy path and each refusal, the acquirer against
  the real autumn document, the guarantee that a re-download of an unchanged document
  hashes the same, and the poller's Drive branch including the freeze and a source with no
  file identifier.
- **Open risks.** The two sources are polling-enabled, so **they go live on the next worker
  restart** and their revisions have an audience now that Grade 2 Turkish can onboard;
  validation thresholds are the only gate. The configured service account needs the Drive
  API enabled on its Cloud project and needs both documents shared with it, or every
  acquisition is refused as access denied. The first real acquisition stores a snapshot
  whose hash differs from the committed fixture's, because the origin diagnostic differs —
  correct, and harmless to the golden parse tests, which parse the fixture.

## Previous independent Calendar-admission session

- **Separated idle Calendar admission from the adaptive source-polling delay
  (ADR-082).** The worker previously used the short Calendar-only interval only when
  the immediately preceding pass returned `InProgress`/`PartiallyDispatched`. If that
  pass was empty and a student requested initial sync during the following weekend
  delay, the request waited up to one hour or required a worker restart.
- The worker now retains the next source-poll deadline while checking for newly queued
  initial sync, diff dispatch and reconciliation work every
  `SIRKADIYEN_SYNC__CALENDAR_IDLE_CHECK_INTERVAL` (five seconds by default). These
  checks do not poll sources, publish revisions, calculate diffs or prune snapshots.
  A source deadline that arrives sooner shortens the sleep, so adaptive polling does
  not drift.
- Scheduler regressions cover an idle worker discovering new Calendar work promptly,
  preserving a nearer source deadline, an overdue source deadline, and rejecting an
  invalid idle interval.
- Documentation debt was reconciled with the implemented system: README no longer lists
  profiles, Calendar sync, DOCX conversion or the frontend as absent; the ingestion guide
  now documents the independent Calendar cadence and only actual remaining integrations;
  `progress.md` and `techContext.md` distinguish the current PostgreSQL-scanning worker
  from the still-planned Hangfire/outbox phase.

## Previous administrative upload UI session

- **Gave administrative acquisition a UI, so the anatomy documents can be uploaded from
  the browser (ADR-081).** `/admin` now has a document-upload module: pick a source, pick
  a `.docx`, upload. The session cookie and the antiforgery token the browser already
  holds are what authorize it, which is the answer to "the API needs a cookie and a CSRF
  token I cannot reproduce by hand" — the alternative was weakening the cookie rules
  ADR-052 deliberately set.
- **The catalog says which sources accept an upload, not the frontend.** New
  `GET /api/sources/uploadable` projects the entries whose transport is
  `AdministrativeUpload`, ordered by identifier. It carries the display name, academic
  year, class year, program language, expected document format and `sharedDocumentGroup`,
  so the panel can name the other sources one upload will serve **before** the fan-out.
  It omits the poll timestamps on purpose: an upload source is never polled, so they are
  permanently null and would read as "never acquired".
- **The panel reports acquisition, never publication.** Success renders one line per
  target with its `Stored`/`Unchanged` outcome and says the worker parses it on its next
  cycle under the same rules as a polled source. An upload whose every target is
  `Unchanged` says so explicitly, because otherwise "no revision followed" is
  indistinguishable from a failure.
- Client-side checks mirror the server's without replacing them: `.docx` only, non-empty,
  8 MB. The endpoint still decides; the UI renders its problem detail and maps 409 to the
  freeze and 403 to a missing SuperAdmin role. The audit trail is reloaded after a failed
  upload too, so an interrupted fan-out shows which targets landed.
- The typed client now sends a `FormData` body as-is (no JSON `Content-Type`, so the
  browser writes the boundary), and the CSRF header plus its stale-token retry apply to
  the upload like any other mutating call. **Tailwind was not added**: the panel reuses
  the existing hand-written `globals.css` classes.
- **Fixed the antiforgery failure the first real upload hit (ADR-081 amendment).** An
  antiforgery request token is bound to the claims-based user it was issued to, and the
  client cached the one minted for `POST /api/auth/google` — issued while still
  anonymous. Every JSON endpoint hid this, because its 400 is the failure the client
  already retries once with a fresh token; the upload cannot, since its token is
  validated while binding `IFormFile`, which throws (a 500 in Development, a bare 400 in
  Production). Sign-in now discards the cached token as logout already did, and a
  multipart request always takes a freshly issued one.
- **The Grade 2 anatomy program is shared between the Turkish and English tracks**
  (confirmed with the faculty owner), with the same `1`/`2`/`3` groups. The four catalog
  entries differ only in program language and exist solely because
  `CalendarAudienceResolver` matches on `record.ProgramLanguage == profile.ProgramLanguage`.
  Decision: **the entries stay and the operator surface stops showing them** — the panel
  groups by `sharedDocumentGroup`, so one document is one option naming every program it
  covers, and it merges every member's audit trail, which is what makes ADR-080's
  interrupted fan-out visible.
- **Open risks.** The API must be restarted before the new route exists; until then the
  panel reports that no source accepts an upload. There is still no frontend test runner,
  so the component is unverified automatically — only the projection and its ordering are
  unit-tested. Grade 2 English remains outside the supported-profile schema, so an
  English anatomy revision still publishes to an empty audience (ADR-079). **The shared
  anatomy program is still modelled as two per-program sources**; the faithful model is
  one source whose records apply to both programs, which needs a shared-audience concept
  in the resolver, the read store, the overlap rule and a `ProgramLanguage` migration.
  Deferred deliberately, and it must be revisited when Grade 2 English enters the schema
  (ADR-048) — that is when a dormant duplication becomes a real one.
- 5 API tests (up from 2), 375 Infrastructure and 6 Contracts .NET tests pass. Release
  build has no warnings, `dotnet format --verify-no-changes` is clean, and the frontend
  typechecks and builds (10 routes). PostgreSQL and Python tests were not run: no
  persistence, migration or parser code changed. `npm run lint` remains unusable — the
  project has no ESLint configuration and the script prompts to create one.

## Previous administrative acquisition session

- **Built administrative acquisition, so a handed-out document can finally become a
  snapshot (ADR-080).** `POST /api/sources/{sourceId}/document` converts an uploaded
  DOCX and stores it as immutable evidence; `GET .../document/uploads` returns the
  audit trail. SuperAdmin only, antiforgery-protected, 8 MB bound, refused while the
  pipeline is frozen because an upload is an acquisition.
- **The endpoint acquires and nothing else.** The worker's next cycle finds the stored
  snapshot and runs the same parse run, validation thresholds and publication rules as
  a polled source, so an uploaded document is not a privileged path into the schedule.
  Re-entering each cycle is safe: a parse run is keyed by snapshot, profile and version.
- **One upload serves every source the document serves.** Sources whose document is
  literally the same file declare a `sharedDocumentGroup`; an upload to any member
  becomes a separate snapshot for every member. The anatomy document is handed to both
  programs and each needs its own revision, because a canonical record reaches a
  student only when its program language matches theirs — so `G2-ANATOMY-AUTUMN-EN`
  and `G2-ANATOMY-SPRING-EN` now exist beside the Turkish pair.
- The catalog refuses the three ways to get a group wrong: a group on a fetched source,
  a group of one (what a mistyped group name looks like), and two members serving the
  same class year and language, which would publish every lesson to those students
  twice.
- **Every upload is audited per target** in `source_document_uploads`: who, file name,
  byte count, SHA-256 of the bytes, resulting snapshot, and whether the content was
  new. A row is written for an unchanged re-upload too, because that is what explains
  an absent revision. The file digest is not the snapshot content hash; one identifies
  the delivered bytes, the other the normalized content.
- An uploaded snapshot carries `snapshot.administrative_upload` rather than the
  local-fixture diagnostic, and that diagnostic is hashed, so it can never be confused
  with the committed development conversion. `LocalDocxSnapshotConverter` is renamed
  `DocxSnapshotConverter`, since it is no longer local-only.
- **Open risks.** The English anatomy revisions will publish to an empty audience until
  Grade 2 English enters the supported-profile schema. ADR-084 later verified the
  English practice fixture as current-year content; annual and vertical-corridor
  audience handling are now the remaining blockers. There is no admin UI for the
  endpoint yet. A fan-out interrupted between its targets leaves one source with the
  document and the other without; repeating the upload completes it, and the per-target
  audit rows say which landed.
- Two adjacent defects were fixed on the way: the poller refactor briefly parsed the
  freshly acquired document instead of the stored one when content was unchanged (an
  existing test caught it), and `ScheduleSourceStore` never copied
  `SupportedAudienceSelectors` when reseeding, so an edited cohort allowlist applied
  only to a fresh database.
- 375 Infrastructure (up from 355), 136 Persistence (up from 133), 6 Contracts and 2
  API .NET tests pass. Release build has no warnings and `dotnet format
  --verify-no-changes` is clean. Migration `AddSourceDocumentUploads` adds the shared
  group column and the audit table. No Python change.

## Previous Grade 2 onboarding session

- **Opened Grade 2 Turkish onboarding, and catalogued the sources that were blocking it
  (ADR-079).** The two blockers were each other's: the anatomy documents could not be
  catalogued without an HTTPS URI, and Grade 2 could not enter the supported-profile
  schema without a catalogued source declaring `anatomyGroup`.
- **A handed-out document names itself.** A fourth transport, `AdministrativeUpload`,
  covers a source an administrator uploads each semester. Its URI is
  `urn:sirkadiyen:upload:{sourceId}` and the catalog refuses anything else for it,
  including an HTTPS URL — a plausible Drive link was tried and reverted during ADR-076
  precisely because it is a false provenance claim. The URN must spell out its own source
  ID, so a copied entry cannot silently attach one document's evidence to another.
- The absolute-HTTPS rule is now chosen by transport instead of applied to every source.
  The poller answers `AwaitingAdministrativeUpload` rather than `UnsupportedTransport`:
  the transport is not missing an implementation, the document is missing an upload.
  Nothing polls such a source, so cataloguing one starts no traffic and stores no snapshot.
- **Grade 2 Turkish is in the supported-profile schema** with three required selectors:
  `practiceGroup` `A`-`H` and `practiceSubgroup` `A1`-`H2` from the practice table and the
  vertical-corridor calendar, plus the independent `anatomyGroup` `1`/`2`/`3` from the
  newly catalogued anatomy sources. The anatomy group is not a subdivision of the practice
  group, so it is a third dimension, not a deeper chain. Schema version is now `1.1`.
- **Grade 2 English deliberately stays out.** Its only current-year source is the annual
  program, which states no cohorts, and its practice fixture is from 2024-2025 (ADR-048).
  Admitting it with no selectors would hand an English student a calendar missing every
  practice and dissection session, which reads as complete.
  **Historical note:** ADR-084 later proved the filename misleading and implemented
  the current-year practice source; annual and vertical-corridor audience handling now
  keep onboarding closed.
- The onboarding form needed no structural change — it renders class years and dimensions
  from `GET /api/profile/options` — but it had been rendering the raw selector keys, so it
  now carries Turkish labels for them.
- The four DOCX snapshot fixtures were regenerated through the catalog. Their cells are
  byte-identical; two `INFORMATION` diagnostics per fixture carried wording the converter
  had stopped emitting, so the committed evidence had drifted from what the committed
  document produces.
- **Open risks.** No anatomy snapshot can be produced outside the tool until the upload
  endpoint and its authorization exist; the catalog entry declares what the source is, not
  that it has been read. A profile stored under schema `1.0` is still not re-validated
  when the schema changes (ADR-055), and there are now two versions in the field.
- 408 Python tests pass, unchanged and unaffected. 355 Infrastructure (up from 344), 133
  Persistence, 6 Contracts and 2 API .NET tests pass; the Release build has no warnings,
  `dotnet format --verify-no-changes` is clean, and the frontend typechecks.

## Previous Grade 2 source sessions

- **Implemented the anatomy profiles (ADR-078), which close the last Grade 2 source.**
  `grade2_anatomy_autumn_v1` and `grade2_anatomy_spring_v1` publish 156 dissection
  sessions — 90 over 30 autumn teaching days, 66 over 22 spring ones. Each of the three
  anatomy groups gets exactly one session per day, which is the rotation ADR-073 predicted
  from the annual program and could not prove from it.
- **A day is a run of hours, not a row with a date.** The same document states a day two
  ways: a vertical merge over its three hours in the later rows, and the date simply typed
  into the middle of three rows in the earlier ones — 30 of the autumn document's 90 rows.
  A day is therefore a run of consecutive rows whose hours advance, stating exactly one
  date between them; a run stating none or several publishes nothing.
- **The day is the unit of refusal.** Publishing the hour that states the date and
  dropping the two beside it would give two groups no session and the third one that may
  not be theirs — worse than nothing, because it looks complete. A date attributed from a
  neighbouring row scores 0.8 and says so in a confidence indicator; a date reached
  through a merge does not, because a merge is the document saying it.
- The lesson title is the profile's declared annual marker `Diseksiyon`, so this source
  and the annual program agree on identity. A profile declaring no marker rejects the
  snapshot rather than inventing a name.
- The spring document states `9 Nisan 2025` where it means 2026, contradicting its own
  weekday; that day is refused whole. It is the fourth Grade 2 document with a year that
  is a year out.
- **Neither anatomy source is catalogued**, so neither profile can run outside a fixture
  and `anatomyGroup` has no declared selector list to validate against. Both wait on the
  same missing piece: an administrative upload path for a document with no URL.

- **Implemented the vertical-corridor profile (ADR-077), which unblocks the sessions the
  other two Grade 2 profiles defer.** `grade2_vertical_corridor_v1` 1.0.0 publishes 42
  sessions — 12 from the autumn document, 30 from the spring one — that previously
  reached no calendar at all. Both revisions are predicted to validate with no findings.
- It **selects students by the practice group they already have**. The profile used to
  declare a `verticalCorridorGroup`; the document states the same `A`-`H` cohorts as the
  practice table (its `*` cells are what this document answers) and halves them into
  `A1`-`H2`, so a third grouping would make every student declare a group the faculty
  never asks for.
- `parsers/vertical_corridor.py` walks the practice table's axis transposed back — a row
  is a dated slot, a column is one of five skill practices — but the slot is written as
  lines of one cell, the way the practice table writes its column headers. The two Grade 2
  rotation profiles now share their cell-level rules in `parsers/cohort_rotation.py`: the
  eight-letter cohort alphabet and the weekday-contradiction refusal, one definition each.
  The document proves why: `Telafi` expands to T, E, L, A, F, I and only the bound refuses
  it.
- **The document is filled in over the year**, so a dated row with no groups publishes
  nothing and raises nothing, while a row whose groups cannot be dated is a warning naming
  the cell. It also carries the English `İ1`-`İ3` and the separately published `EK-n`
  lists, which are counted under their own reasons and never published under a Turkish
  source.
- **Nine dated rows contradict their own weekday**, four naming a year that is a year out
  — the same four the practice table gets wrong, which is evidence the two are maintained
  together. Three carry groups, so four cohort-sessions wait on a correction.
- Known limit: this is not the whole programme. The practice table marks 95 slots and the
  faculty has scheduled a fraction of them; re-acquisition, not a parser change, publishes
  the rest.

- **Declared the numeric date order of the Grade 2 practice source (ADR-075).**
  `grade2_practice_v1` 1.1.0 declares `dayFirst`, read off a second source rather than
  off the Turkish writing convention: the Grade 2 annual workbook schedules the same
  session as a serial, 2025-10-08 08:30-10:20 `FİZYOLOJİ 1. UYGULAMASI (TÜM GRUPLAR
  Amfide yapılacak)`, and the month-first reading falls outside both the academic year
  and the block's own 3-16 October range. The profile now publishes 164 candidates; one
  whole-cohort session was recovered and nothing else in the golden file moved.
- The version bump is required even a day after 1.0.0: a parse run is keyed by
  (snapshot, profile, version), so a stored 1.0.0 run would otherwise stand while the
  parser reads that cell differently. Both catalog entries naming the profile moved with
  it. A numeric text with no year is still refused, so a slot label such as `2/6` is not
  completed into 2 June.
- **A Word document is converted onto the normalized snapshot contract (ADR-076).**
  `LocalDocxSnapshotConverter` maps a body-level table to a worksheet and a run of
  paragraphs between tables to a single-column worksheet, keeps line structure (a
  vertical-corridor slot cell writes a label, date and time range as three lines),
  and maps `gridSpan`/`vMerge` onto merged ranges. Titles are `Table n`/`Text n` and the
  snapshot says so in its own diagnostics. A nested table is an Error diagnostic naming
  its cell rather than a flattened value.
- Four Grade 2 documents are converted and committed. The anatomy pair **confirms
  ADR-073 directly**: the three dissection hours of one date carry anatomy groups 1, 2
  and 3 in rotation. The vertical-corridor pair is where the practice sheet's 95 `*`
  cells are answered. Every converted cell is text with no number format, so those
  profiles will resolve dates from text alone.
- The Grade 2 anatomy sources are deliberately **not** in `config/schedule-sources.json`:
  the catalog requires an absolute HTTPS URI and these documents are handed out, not
  published. The snapshot tool gained `--document` instead of the catalog gaining a
  fabricated URL. They are handed out once a semester and unchanged afterwards, so an
  administrative upload fits them; Student Affairs edits the vertical-corridor documents
  during the year, so those need re-acquisition. Both transports remain unbuilt.
- 408 Python tests (up from 349) and 344 Infrastructure unit tests (up from 337) pass;
  Ruff, Ruff format, Mypy and the Release build are clean.

- **Implemented the Grade 2 Turkish practice profile (ADR-074).** `grade2_practice_v1`
  1.0.0 publishes 163 candidates for the eight `A`-`H` cohorts from `G2-TR-PRACTICE`.
  The table is the **transpose** of the Grade 1 rotation matrix — a column is a dated
  slot whose header holds a label, a date and a time range on separate lines, and a row
  is a practice subject naming its room — so it has its own reader,
  `parsers/practice_slots.py`, rather than a parameterized version of the Grade 1 one.
- **Every row is classified exactly once** (block heading, slot header, subject, or
  none), so `rows.scanned` equals the worksheet's row count and the topic lists between
  the tables are counted rather than skipped by a precomputed range.
- **A slot header whose weekday contradicts its own date is refused.** Unlike the annual
  sources, these dates are typed by hand and the weekday is their only corroboration.
  The workbook contains four whose year is a year out; publishing them would have put
  practices in February 2025 on calendars and quarantined the revision.
- Also handled from the source's own statements: a whole-cohort `TÜM GRUPLAR` session
  written into the table with its own date and time (read from the cell, published once
  at its merge anchor), concatenated cohort letters `ABCD 1/1` (the trailing `n/m` is a
  session number), a bare `*` whose groups the source says are announced elsewhere, and
  an `Anatomi (13)` row of dissection dates deferred to the anatomy sources (ADR-073).
- **A bounded letter run.** `parse_group_expression` gained `max_letter_run` (default
  two, so no existing caller changes), and the profile pairs it with the eight cohorts
  the source states: without that bound the same rule reads `SINAV` as five cohorts, one
  of which is real. The refusal is counted and named.
- **ADR-051 fired on a real source for the first time.** `TÜM GRUPLAR 8.10.2025` means 8
  October day-first and 10 August month-first, so that one whole-cohort session is
  refused and its cell named. The Turkish annual program states the same session on 8
  October, which is evidence for declaring `dayFirst` — left as a deliberate decision.
- The revision is predicted to validate with **no findings at all**. 95 skill-practice
  sessions still reach no calendar: the source states no audience for them, and
  `grade2_vertical_corridor_v1` owns them.
- 349 Python tests pass, up from 317; Ruff, Ruff format and Mypy are clean. No .NET,
  catalog or database change was needed — `G2-TR-PRACTICE` already pointed at this
  profile with `practiceGroup` `A`-`H`.

- **Implemented the Grade 2 annual parser profile for both languages (ADR-073).**
  `grade2_yearly_v1` 1.0.0 is registered against the existing row-oriented annual
  implementation and covers `G2-TR-ANNUAL` (790 candidates from 1156 rows) and
  `G2-EN-ANNUAL` (935 from 1252). The workbooks are the Grade 1 layout with different
  header wording and term text (`Dönem 2` / `Time Table 2`); the class year comes from
  the request context, so no per-language parser exists.
- **A declared group rotation is no longer published to the whole class.** Each Grade 2
  workbook writes one dissection session as three consecutive daily slots carrying the
  same session number, and the separate `SALON GRUP SAATLERİ` source assigns anatomy
  groups 1/2/3 to those hours in rotation. Publishing all three would have booked every
  student into two sessions they must not attend, and the slots do not overlap, so
  revision validation would not have caught it. `ParserProfileDefinition` now carries
  `group_rotation_subjects` (reported by `GET /v1/profiles`); Grade 2 declares
  `diseksiyon`/`dissection` and 159 rows per workbook are counted as
  `rows.ignored.outOfScopeGroupRotation`. Grade 1 declares none and is unchanged.
- **Fixed a shared-primitive misreading that would have quarantined every English
  revision.** A bare `9` in an `hh:mm` cell was reduced to `00:00`, publishing a
  free-study block of 780 minutes, which validation rejects as an impossible duration.
  A numeric time cell must now hold a day fraction in `[0, 1)` unless its format
  declares a full timestamp. Parser engine 0.2.0 and `grade1_yearly_v1` 1.5.0 (both
  catalog entries) follow from that; the Grade 1 goldens moved only in their version and
  digest lines, and `grade1_practice_v1` reads only textual ranges and was untouched.
- The 119 bare Turkish `UYGULAMA` rows are excluded under the existing ADR-071 rule, and
  the source agrees: their location cell reads "see the Dönem 2 practice program".
- **Open risks.** Both Grade 2 revisions are predicted to validate, but each sits exactly
  at the overlap tolerance with one same-course overlap the source wrote twice; a second
  such typo holds the revision for review. The English workbook writes practice groups
  inside titles (`… İ2`, `Team Work İ1`-`İ5`) and those are published verbatim to the
  whole program — no audience is inferred from them. No Grade 2 student can onboard yet:
  the supported-profile schema still covers class year 1 only (ADR-055), so these
  revisions publish to an empty audience until it is extended.
- 317 Python tests pass, up from 301; Ruff, Ruff format and Mypy are clean. 337
  Infrastructure, 6 Contracts and 2 API .NET tests pass and the Release solution build
  has no warnings. PostgreSQL integration tests were not run this session; no .NET or
  database code changed.

## Previous free-study session

- **Fixed annual revisions being quarantined by source-authored free-study overlaps
  (ADR-069).** The parser had faithfully retained separate `SERBEST ÇALIŞMA` /
  `FREE TIME` rows, but classified them as generic `Other`; the overlap validator
  then mistook repeated availability blocks for duplicated teaching. The real
  Turkish fixture contains two such intersections (2025-10-23 and 2026-05-18).
- Added `FreeStudy` to the parser contract and canonical event type. Annual title
  classification emits it before exam/practice keyword checks, so parenthetical
  text such as another cohort's exam cannot reclassify free study.
- `AudienceOverlap` now reports `FreeStudy`/`FreeStudy` intersections as a
  non-blocking warning. Repeated overlaps of a real same-course lesson still
  quarantine, and different-course parallel offerings retain their existing
  warning behavior.
- Bumped `grade1_yearly_v1` 1.1.0 → 1.2.0 in the parser registry and both catalog
  sources. Restarting parser + worker re-seeds the catalog and re-parses the
  existing snapshot; the resulting annual revision validates and publishes
  without manually approving the obsolete held revision.
- 295 Python tests pass; Ruff, Ruff format, and Mypy are clean. 323 Infrastructure,
  6 Contracts, and 2 API unit tests pass; the Release solution build has zero
  warnings/errors.

- **Fixed the empty-theoretical-calendar cause: the held `G1-TR-ANNUAL` revision (ADR-068).** It
  was quarantined by `AudienceOverlap` (33 whole-class collisions). The fixture showed the annual
  parser assigned every row `ALL_STUDENTS_IN_PROGRAM`, so a group's long PDÖ block ran parallel to
  the cohort's lecture and collided, along with lunch breaks and genuine parallel offerings.
- **Part 1 — parser.** Extended ADR-030 PDÖ/PBL exclusion to `grade1_yearly_v1` and excluded
  lunch/interval breaks; SERBEST ÇALIŞMA (free study) is kept. Counted via `rows.ignored.<reason>`
  (no silent drops). TR 923→855 candidates, EN 964→893, every removal verified PDÖ/PBL or break —
  no lecture removed. Golden files regenerated; two regression tests added.
- **Part 2 — validator.** `AudienceOverlap` now distinguishes a same-course duplicate (a parsing
  fault → still quarantines over the tolerated count) from a different-course parallel offering
  (electives, a make-up exam → non-blocking `Warning`). Sameness is `NormalizedCourseIdentity`,
  falling back to display title. So the annual revision auto-publishes once its overlaps are all
  parallel offerings, and the theoretical program reaches calendars via incremental diff dispatch.
- **Re-parse trigger shipped.** A parse run is keyed by `(snapshot, profile, ParserProfileVersion)`
  (`FindRunAsync`), so `grade1_yearly_v1` was bumped 1.0.0 → 1.1.0 (profile definition, parser
  registry, catalog for both annual sources, golden). Restarting the worker re-seeds the catalog
  and re-parses the stored annual snapshots with the new code, producing a fresh revision that now
  publishes. Profiles are versioned independently now; the golden test reads each profile's version
  from the registry.
- 295 Python tests pass (ruff/format/mypy clean); 322 Infrastructure + 2 API + 5 Contracts .NET
  unit tests pass; Release build clean. PostgreSQL integration tests were not run this session.

## Previous SuperAdmin-routing session

- **Routed SuperAdmin to an admin panel instead of the license page (ADR-067).** Onboarding state
  is student-only and stays unchanged, so a SuperAdmin (no license) computed to `LicenseRequired`
  and was stuck on the redemption page. The frontend now picks the landing route from the
  backend-authoritative session `role` (`routeForUser`): SuperAdmin → `/admin`, everyone else →
  their onboarding route. Navigation only; admin APIs remain enforced by the `SuperAdmin` policy.
- **Minimal `/admin` panel** surfaces the operational freeze (`GET`/`POST /api/operations/freeze`)
  with an audited reason, lists the pending Phase 10 surfaces, and offers audited SuperAdmin
  self-activation (`POST /api/admin/users/{id}/activate`) so the operator can also walk the
  student flow to test sync. A role guard redirects non-admins off `/admin`.
- No backend change; the role claim and endpoints already existed. `tsc --noEmit` and
  `npm run build` are clean (10 routes).

## Previous forwarded-scheme fix session

- **Fixed the local-dev antiforgery SSL failure (ADR-066 amendment).** The HTTPS proxy edge left
  Kestrel receiving the proxied request over plain HTTP, so `Request.IsHttps` was false and the
  antiforgery guard (`SecurePolicy.Always`) threw on the first `GET /api/auth/csrf`. The earlier
  "no backend change needed" claim was wrong and is corrected in the ADR.
- **Two-part fix, matching the production TLS-termination shape.** `web/src/middleware.ts` sets
  `X-Forwarded-Proto: https` on `/api/*` (Next's rewrite proxy forwards `X-Forwarded-Host` but not
  the proto — verified against an echo upstream and Next's `proxy-request.js`). `Program.cs` now
  calls `UseForwardedHeaders` first in the pipeline (`XForwardedFor | XForwardedProto`), clearing
  `KnownIPNetworks`/`KnownProxies` in Development to trust the loopback peer. Production must pin
  its reverse proxy through those lists instead (ADR-052).
- Verified end to end: `GET /api/auth/csrf` returns `500` without the header and `200` with a
  valid token when `X-Forwarded-Proto: https` is present; the middleware injects it upstream. API
  Release build is clean (0 warnings) and the API unit tests pass. `.NET 10` deprecated
  `KnownNetworks` → used `KnownIPNetworks`.

## Previous frontend scaffold session

- **Scaffolded the consumer frontend foundation (ADR-066).** New `web/` Next.js App Router +
  TypeScript project. It consumes the same-site cookie session and treats backend onboarding
  state as authoritative, mapping each `OnboardingState` to a route rather than deciding
  activation client-side.
- **Local dev is same-origin behind an HTTPS proxy edge, so no backend CORS/SameSite/cookie
  change was needed.** `next.config.mjs` proxies `/api/*` to Kestrel server-side; the browser
  talks only to `https://localhost:3000`. The HTTPS edge satisfies the `Secure`/`__Host-`
  cookies (Kestrel can stay on plain HTTP, `BACKEND_ORIGIN=http://localhost:5080`), exactly the
  production TLS-termination topology. This is the faithful alternative to weakening cookies that
  ADR-052 warned against.
- **Both Google flows are popup/`postMessage`.** Sign-in uses GIS returning an ID token to
  `POST /api/auth/google`; Calendar uses `initCodeClient` returning a one-time code to
  `POST /api/calendar/authorization` (client id + scope read from
  `GET /api/calendar/authorization/options`). Only `https://localhost:3000` needs adding to the
  OAuth client's Authorized JavaScript origins; no localhost redirect URI.
- A CSRF-aware typed API client fetches `/api/auth/csrf`, sends the token in `X-CSRF-TOKEN` on
  mutating verbs, and retries once on a stale-token 400. Enums are typed as their string names
  because the API uses `JsonStringEnumConverter`.
- **Not yet built:** admin/operator UI, real component system, richer dashboard, automated
  frontend tests. The worker still performs the actual calendar writes.
- Pinned Next.js to the patched `15.5.21` (CVE-2025-66478). `npm run build` and `tsc --noEmit`
  are clean (11 routes). A residual transitive `sharp`/`postcss` advisory has no non-breaking fix
  and affects only Next's unused image-optimization path. No .NET or Python code changed.

## Previous intra-diff batching session

- **Implemented ledger-resumable intra-diff batching (ADR-065).** Incremental dispatch attempts
  at most `SIRKADIYEN_SYNC__CALENDAR_OPERATIONS_PER_DIFF_BATCH` per-user semantic mutations for
  each admitted diff in one worker cycle.
- A pass that discovers more work returns `PartiallyDispatched`; the diff remains `Pending`,
  `DispatchAttempts` and `NextAttemptAtUtc` are untouched, and the next cycle replans the same
  immutable diff. Completed insert/patch/delete operations have already converged the durable
  mapping ledger, so they fall out of that plan without a new cursor table or migration.
- The budget covers inserts, patches, audience-narrowing deletes, semantic `Deleted` fan-out and
  secondary-identity moves. A dead credential consumes one attempted mutation and is moved into
  the existing re-authorization/replay workflow.
- Structured worker logs now distinguish quota yield from transient deferral and report mutation
  attempts plus insert/patch/delete/reauthorization counts.
- 461 .NET tests pass, up from 458, nothing skipped. Release build has no warnings, formatting and
  EF model checks are clean, and no migration was required.

## Previous Calendar inventory/recovery/fence session

- **Completed safe Calendar reconciliation (ADR-062).** A freeze-gated periodic inventory
  compares current published/applicable records, the durable event ledger, and actual marked
  Google events. It recreates missing expected events, adopts unledgered exact matches, patches
  visible/private-property drift, and refreshes stale ledger state.
- **Inventory is deliberately non-destructive.** Duplicate or unexpected Google events,
  source-identity conflicts, and ledger rows with no expected current record are reported and
  preserved. Only replay or dispatch of a valid `Ready`/`Released` semantic diff may authorize a
  Calendar delete.
- **Closed the calendar-creation crash window (ADR-063).** Created calendars carry an exact,
  per-user description marker. Before creating a calendar, initial sync lists marker candidates:
  one is reattached, none causes creation, and multiple candidates fail safely without guessing.
  OAuth now requests `calendar.app.created` plus the read-only CalendarList scope required for
  that lookup; an older incomplete grant is moved to re-authorization when the lookup is needed.
- **Added a multi-worker dispatch/reconciliation fence (ADR-064).** A non-blocking PostgreSQL
  session advisory lock is held continuously across global diff dispatch, per-user semantic
  replay including its empty-scan completion, and periodic inventory. A second worker yields the
  maintenance section instead of racing the proof that catch-up is complete.
- A missing/inaccessible managed calendar is recorded on the connection and surfaces as
  onboarding `ActionRequired`; ordinary Calendar writers and inventory stop targeting it.
  Explicit calendar recreation is intentionally not automatic.
- Migration `AddCalendarInventoryReconciliation` adds the unavailable/inventory timestamps,
  their invariant and the due-work index. Inventory cadence and batch size are configurable
  through `SIRKADIYEN_SYNC__INVENTORY_INTERVAL` and
  `SIRKADIYEN_SYNC__INVENTORY_CONNECTION_BATCH_SIZE`.
- 458 .NET tests pass, nothing skipped. Release build has no warnings, EF reports no pending
  model changes, the migration was verified Down -> Up on PostgreSQL, and the advisory-lock
  exclusion is covered by a PostgreSQL integration test.

## Previous semantic-diff replay session

- **Implemented the freeze-gated semantic-diff replay worker (ADR-060).** For each admitted
  re-authorized connection it reads only `Ready`/`Released` diffs whose global
  `CalendarDispatchState` is `Dispatched`, strictly after the durable
  `(DispatchedAtUtc, DiffId)` cursor, and applies them to that one user in order.
- **Cursor advancement is per complete diff.** A crash, transient Calendar failure, dead
  credential, missing immutable reference, or ledger conflict leaves the current diff before
  the cursor, so the next pass repeats it through the deterministic event ID and durable ledger.
  An empty scan clears the request through the original required-since workflow token.
- **Deletion remains edge-triggered.** The replay service calls Calendar delete only from a
  replayed `Deleted` entry or an `Updated` entry that ceased to apply. A mapping that merely has
  no counterpart in current published state is untouched; a regression test completes an empty
  replay while deliberately leaving such a stale mapping in place.
- **Secondary-matched time moves now preserve the existing Google event.** The dispatcher and
  replay path patch the ledger's stored `GoogleEventId`, then atomically move the mapping from
  the previous stable identity to the current one (ADR-061). The old implementation re-derived
  the event ID from the new start-time identity and could leave the old event behind.
- The worker bounds both connections and diffs per connection through
  `SIRKADIYEN_SYNC__RECONCILIATION_CONNECTION_BATCH_SIZE` and
  `SIRKADIYEN_SYNC__RECONCILIATION_DIFFS_PER_CONNECTION`; it runs after global dispatch and
  initial sync in the ordinary cycle.
- 438 .NET tests pass, up from 429, nothing skipped. Release build has no warnings,
  `dotnet format --verify-no-changes` is clean, EF reports no pending model changes, and the new
  ordered replay/mapping transitions pass against real PostgreSQL. No migration or Python code
  changed.

## Previous reconciliation cursor foundation session

- **Started safe Calendar reconciliation with a durable missed-diff cursor (ADR-060).**
  The first credential failure of an initial-sync-completed connection now records
  `ReconciliationRequiredSinceUtc` plus the ordered
  `(ReconciliationCursorDispatchedAtUtc, ReconciliationCursorDiffId)` checkpoint.
  Re-authorization preserves this state, so globally dispatched diffs are no longer
  forgotten merely because one user's credential was unavailable.
- **Admission is deliberately narrower than authorization.** Only an `Authorized`,
  initial-sync-`Completed` connection with its managed calendar and complete cursor tuple
  appears in `ListPendingReconciliationAsync`; a pre-initial-sync credential failure is
  recovered by initial sync instead of diff replay.
- **The required-since timestamp is an optimistic workflow token.** Cursor advancement and
  completion must present the same value, preventing a stale worker from advancing or
  clearing a newer reconciliation request. The cursor is monotonic and starts at the
  failure timestamp with an empty diff id, so the diff that discovered the dead credential
  remains eligible at the same timestamp.
- Migration `AddCalendarReconciliationCursor` adds the three nullable columns, a named
  pending-work index, and a database constraint requiring the tuple to be all-null or
  complete, ordered, initial-sync-completed, and attached to a managed calendar.
- This is the **admission/cursor foundation only**. The next implementation slice must list
  `Ready`/`Released` diffs already marked `Dispatched` after the cursor and replay them for
  one user in order. Deletion must remain authorized by those semantic diffs; reconciliation
  must never infer deletion merely from current-state absence. Calendar inventory/drift and
  orphan-calendar lookup follow that catch-up path.
- 429 .NET tests pass, up from 425, nothing skipped. Release build has no warnings,
  `dotnet format --verify-no-changes` is clean, EF reports no pending model changes, and
  migration `AddCalendarReconciliationCursor` was verified Up → Down → Up on the real
  PostgreSQL. No Python changed.

## Previous diff-driven incremental sync session

- **Implemented diff-driven incremental calendar sync (ADR-059).** A new worker stage,
  `DispatchPendingDiffsAsync`, runs after diff calculation and fans every dispatchable
  (`Ready`/`Released`) `ScheduleDiff` out onto the calendars it affects: inserting newly
  applicable lessons, patching changed ones, and deleting those that no longer apply. This is
  the first time a diff reaches a calendar. Scope is core + failure taxonomy; a reconciliation
  sweep and intra-diff quota batching are deferred.
- **The diff carries its own dispatch lifecycle; no separate sync-job aggregate.** The
  `ScheduleDiff` gains `CalendarDispatchState` (`Pending → Dispatched`, terminal `Failed`) plus
  `DispatchAttempts`/`NextAttemptAtUtc`/`DispatchedAtUtc`/`DispatchFailureReason`. A diff is
  eligible when dispatchable, still pending, and past its back-off — mirroring how the
  connection carries its initial-sync state (ADR-058).
- **Coarse per-diff tracking, fine-grained ledger idempotency.** A worker killed mid-fan-out
  re-runs the whole diff; each per-user op converges (insert 409, patch skipped when the ledger
  hash matches, delete of an absent event a no-op), so the diff is marked `Dispatched` only
  after a clean fan-out. No per-`(diff,user)` table is needed.
- **The mapping ledger is the authority for who holds a lesson; audience decides only
  insertions.** The pure `IncrementalSyncPlanner` decides per user: delete for a removed or
  no-longer-applying record (audience narrowed, or `Cancelled`), patch when content moved,
  insert for a cohort user the record now applies to. The initial/incremental boundary needs no
  special handling — re-dispatching a diff that predates a user's completion is a no-op because
  initial sync already recorded the current content hash.
- **Failure taxonomy at two levels.** The real client retries transient failures (429/5xx/network)
  with bounded exponential back-off, then raises a typed transient exception; the service defers
  the diff with a growing back-off and gives up to `Failed` after `MaxDispatchAttempts`. A dead
  credential (`invalid_grant`/401/403) flags the connection `NeedsReauthorization` (its first
  producer), skips that user, and leaves their events — one dead user never blocks the diff.
- **New surface:** `ICalendarSyncTargetReadStore` (completed-sync cohort/by-id targets joined to
  their profile), mapping-store reverse-lookup/update/remove, `ListRecordsByIds`,
  `IUserCalendarClient` patch/delete, and a distinct `GoogleCalendarCredentialException` (to avoid
  clashing with ADR-057's authorization-code `GoogleCalendarAuthorizationException`). No new NuGet
  package. Config: `SIRKADIYEN_SYNC__DIFF_DISPATCH_BATCH_SIZE/MAX_DISPATCH_ATTEMPTS/DISPATCH_RETRY_BASE_DELAY_SECONDS`.
- Migration `AddCalendarDispatch` adds the dispatch columns, the
  `ck_schedule_diffs_calendar_dispatch_state` constraint, and the dispatch index;
  `CalendarDispatchState` defaults to `Pending` (not EF's empty string) so existing diffs become
  dispatch-eligible, which is correct and idempotent since no completed-sync user exists yet.
- 425 .NET tests pass, up from 388, nothing skipped: diff dispatch transitions, the
  `MarkNeedsReauthorization` guard, the planner (insert/patch/delete/narrow/widen/cancelled), the
  service against fakes (created fan-out, idempotent replay, updated patch+widen, narrow delete,
  deleted removal, freeze no-op, transient defer-then-fail, dead-credential flag without blocking,
  no-longer-pending skip), and PostgreSQL tests (mapping reverse/update/remove, `ListRecordsByIds`,
  the cohort/by-id target store, and the diff-store dispatch queries). Release build has no
  warnings, `dotnet format --verify-no-changes` is clean, EF reports no pending model changes, and
  migration `AddCalendarDispatch` was verified Up → Down → Up on the real PostgreSQL. No Python changed.

## Previous Calendar initial sync session

- **Implemented per-user initial calendar sync (ADR-058).** A student at
  `ReadyForInitialSync` starts synchronization; the worker creates their dedicated
  Sirkadiyen calendar (ADR-024) and writes every currently-published event that applies to
  their profile, then onboarding reaches `Active`. The scope is authorization-to-populated
  only; diff-driven incremental sync (patch/delete, reconciliation) is a later slice, but the
  mapping and idempotency are built for it.
- **Worker-driven and state-machine-resumable, not request-scoped.** The API only records
  intent: `POST /api/calendar/sync` moves the connection's `InitialSyncState`
  `Pending → InProgress`, and the worker does the slow, quota-bound work across cycles,
  mirroring the existing publication/diff stages. A killed worker resumes from what is not yet
  mapped. `InitialSyncState` lives on the connection (orthogonal to authorization `Status`);
  re-authorizing preserves both the calendar and the sync progress.
- **Idempotent by a deterministic event id plus a durable ledger.** Each event is inserted
  with a client-chosen id `base32hex(sha256(userId ‖ stableIdentity))` — always a valid
  Calendar id — so a re-insert returns 409, treated as success. A `UserCalendarEventMapping`
  row, unique on `(UserId, StableIdentity)`, records what was written; the worker inserts only
  records with no mapping yet. Together they survive a crash between the Google write and the
  local commit without duplicating.
- **Affected-events resolution is a pure function.** A published record applies when
  academic year, class year and program language match and either it is program-wide or a
  cohort-scoped record whose `{Dimension,Value}` selectors intersect the profile's selectors;
  a cancelled record never applies. Volume per class year is bounded, so filtering is in
  memory over a "current published records for this program" query (records of each source's
  `Published` revision).
- **Gated by the operational freeze**, like every other calendar-touching job, and the
  refresh token is decrypted only in memory for the duration of a sync call.
- **The worker now shares the API's Data Protection key ring.** Both hosts call
  `AddSirkadiyenDataProtection`, which pins one application name (`Sirkadiyen`) and a
  file-system key location, because Data Protection otherwise isolates key rings by content
  root and a generic host has none configured. Without this the worker could not decrypt the
  token the API encrypted.
- Added `Google.Apis.Calendar.v3` 1.75.0.4206 (audit-clean). The real Calendar client is
  abstracted behind `IUserCalendarClient`; **it is not covered by tests** (no live Google), so
  only the fake is exercised.
- Migration `AddInitialCalendarSync` adds the `InitialSyncState` column (defaulting to
  `Pending`, not EF's empty string, so the check constraint holds) and the
  `user_calendar_event_mappings` table with the unique `(UserId, StableIdentity)` index.
- 388 .NET tests pass, up from 337, nothing skipped: domain transitions, mapping invariants,
  the audience resolver, the event factory (all-day exclusive end, deterministic id), the sync
  service against fakes (calendar creation, unmapped-only writes, idempotent 409, freeze
  no-op, budget deferral and resume, missing profile, isolated failure), the new onboarding
  cases, and PostgreSQL mapping-store, canonical-read-store and connection sync-state tests.
  Release build has no warnings, `dotnet format --verify-no-changes` is clean, EF reports no
  pending model changes. No Python changed.
- Removed `IGoogleCalendarConnectionStore.IsAuthorizedForUserAsync`: onboarding now reads the
  connection view and reasons over `Status` plus `InitialSyncState`, so the boolean query was
  redundant surface.

## Previous Calendar authorization session

- **Implemented Calendar authorization (ADR-057).** ADR-052 had deliberately left
  sign-in free of any Calendar scope, so this is the second, separate consent. A
  `GoogleCalendarConnection` aggregate holds the grant, one row per user.
- The flow mirrors ADR-052's shape: the frontend obtains a one-time authorization
  code with offline access and posts it to a CSRF-protected
  `POST /api/calendar/authorization`; the backend performs the exchange because it
  carries the client secret. No redirect handler and no server-side redirect state.
  The exchange `redirect_uri` defaults to `postmessage`, which Google requires for the
  browser popup code flow.
- **Only `calendar.app.created` is requested** — access solely to calendars this app
  creates, which is ADR-024's model and structurally cannot reach the primary calendar.
  Google reports what was actually granted, and a user can clear the permission while
  still completing consent, so a grant missing the scope is refused rather than stored
  as an authorization that could never synchronize.
- **The refresh token is encrypted at rest** with Data Protection under a dedicated
  purpose. The domain never sees plaintext: the application layer protects it through
  `ICalendarTokenProtector` before the aggregate exists, so the aggregate holds opaque
  ciphertext. The read projection omits the credential entirely, so no API response can
  carry it by accident.
- Authorization requires an active license **and** a profile; the code is not even sent
  to Google when the account may not connect. Onboarding now derives
  `ReadyForInitialSync`, and a connection needing re-authorization does not count as
  authorized, so a revoked grant returns the user to consent.
- Creating the dedicated calendar stays in initial sync (ADR-024). The aggregate
  reserves a nullable `ManagedCalendarId`, and re-authorizing preserves it so
  re-granting never orphans the calendar the user's events live in.
- `Microsoft.AspNetCore.DataProtection` had to be pinned to the patched servicing
  release: 10.0.0 carries a critical advisory and the build audits packages as errors.
- Migration `AddGoogleCalendarConnections` verified Up → Down → Up against the real
  PostgreSQL, with the unique `UserId` index and status check constraint confirmed; the
  local database is left at the latest migration.
- 337 .NET tests pass, up from 311, nothing skipped: domain invariants, service
  behaviour against a fake exchange client (scope refusal, rejected code, prerequisites,
  and that the credential is protected before it reaches the store), Data Protection
  round-trip and purpose isolation, the new onboarding case, and 5 PostgreSQL
  connection-store tests. Release build has no warnings, `dotnet format
  --verify-no-changes` is clean, EF reports no pending model changes. No Python changed.
- **Not covered by tests:** the real Google exchange client. It needs a registered Web
  OAuth client and a live consent, so it is abstracted behind an interface and only the
  fake is exercised.

## Previous student number session

- **Added the university student number to the profile (ADR-056).** The
  `StudentProfile` aggregate now carries a `StudentNumber`, stored as text so its
  significant leading zeros are never truncated.
- Validation is layered by ownership. The **domain** guards only the structural
  invariant — a trimmed, exactly-ten-digit, all-ASCII-digit string — so the
  aggregate can never hold a corrupt number. The **application** validator owns the
  semantic cross-validation, because those rules depend on scope and on the row's
  own program language: digits 1-2 must be `01` (Istanbul Medical Faculty) and
  digits 3-4 must match the selected program (`01` Turkish, `02` English). Format is
  re-checked in the validator so a bad number is a clean 400, not a domain throw;
  the digit-slice checks are skipped once a number is known malformed. New error
  codes: `InvalidStudentNumber`, `StudentNumberFacultyMismatch`,
  `StudentNumberProgramMismatch`.
- The **database** pins the same ten-digit structural rule as a check constraint
  (`"StudentNumber" ~ '^[0-9]{10}$'`); the semantic rules stay in the application
  layer where the program language is in scope.
- `SubmittedStudentProfile`, `StudentProfileView` and the `PUT`/`GET` DTOs gained
  the field; the store threads it through the upsert.
- Migration `AddStudentNumberToProfile` adds the column `NOT NULL` with **no**
  server default (the table is empty at this migration and an empty string could
  never satisfy the constraint). Verified Up → Down → Up against real PostgreSQL on
  the empty `sirkadiyen` database, which is left at the latest migration; the
  deployed constraint predicate was confirmed.
- 311 .NET tests pass, up from 297, with nothing skipped: new domain tests (trim
  keeps leading zeros, non-ten-digit rejected) and validator tests (non-numeric,
  wrong length, missing, faculty mismatch, both language mismatches, a matching
  English number accepted); the PostgreSQL store tests now round-trip the number.
  Release build has no warnings, `dotnet format --verify-no-changes` is clean, EF
  reports no pending model changes. No Python file changed.

## Previous student profile session

- **Implemented the validated student profile (ADR-055).** A `StudentProfile`
  aggregate keeps academic year, class year and program language relational and
  the variable cohort selectors as a schema-versioned JSONB document keyed by
  dimension, each holding one value. The aggregate enforces only structural bounds
  (class year 1-6, bounded/non-blank keys and values, a selector cap); the
  allowlist lives elsewhere so the domain never carries what changes at rollover.
- The supported-profile schema is **server-owned code**, not a config file or a
  projection of the source catalog, covering one current academic year. Each
  `(classYear, programLanguage)` program lists dimensions that are either
  independent (explicit values) or dependent (a parent plus child values per
  parent value). Only fixture-confirmed cohorts appear: Grade 1 Turkish
  `practiceGroup` A-H with `practiceSubgroup` A1-H2, and Grade 1 English `İ` with
  `İ1`-`İ3`. Grade 1 anatomy, Grade 2 and Grade 3 are deliberately absent (ADR-048).
- A single validator serves the profile write and, later, audience matching. A
  unit test cross-checks every schema value against the source catalog's declared
  `supportedAudienceSelectors`, so the code-defined schema cannot silently drift
  from the evidence.
- The write path requires an active license first (`ActivationRequired`
  otherwise), enforcing the onboarding order in the backend. Onboarding now
  derives `CalendarAuthorizationRequired` when a profile row exists and
  `ProfileRequired` when it does not, so an interrupted onboarding resumes
  correctly.
- Persistence is a transactional `UserId`-unique upsert; a concurrent first-time
  save reruns once as an update. Added `GET /api/profile`, `GET
  /api/profile/options` and a CSRF-protected `PUT /api/profile` returning the
  stored profile and the fresh onboarding snapshot.
- Migration `AddStudentProfiles` (new `student_profiles` table, JSONB selectors,
  `UserId` unique index, class-year and program-language check constraints,
  cascade FK to `users`, `xmin`) was verified Up, Down and Up again against the
  real PostgreSQL and applied incrementally to the local `sirkadiyen` database.
- 297 .NET tests pass, up from 272, with nothing skipped: 4 new PostgreSQL
  profile-store tests (insert, upsert-replaces-row, concurrent convergence,
  absent) and new domain, validator, schema-well-formedness and catalog
  cross-check unit tests. Release build has no warnings, `dotnet format
  --verify-no-changes` is clean, and EF reports no pending model changes. No
  Python file changed, so its 292 tests were not re-run.

## Previous single-use licensing session

- Replaced new license generation with the WhatsApp-friendly
  `SRK-XXXXX-XXXXX` format (ADR-054). It omits ambiguous characters, retains 50
  random bits, and keeps already-issued long `SIRK-...` codes redeemable.
- Added explicit manual activation for the future admin panel. A SuperAdmin can
  activate a known Google user with a required reason; the resulting `Manual`
  license contains no code/hash, starts `Redeemed`, and writes a
  `ManuallyActivated` audit.
- Manual activation is idempotent and shares the same partial unique index as
  code redemption. Real PostgreSQL tests prove a manual request and a code
  redemption racing for one user still produce exactly one current activation.
- Migration `AddManualLicenseActivation` backfills existing rows as `Code` and
  was applied from scratch to `sirkadiyen_test` and incrementally to the local
  `sirkadiyen` database. Its rollback refuses while manual licenses exist rather
  than inventing code hashes.
- Implemented single-use licenses (ADR-053). A high-entropy plaintext code is
  returned once; PostgreSQL stores only a keyed HMAC-SHA256 hash. Creation,
  redemption, expiration and revocation append audit rows transactionally.
- Redemption locks the code row. A partial unique index also admits only one
  current redeemed license per user, so both same-code/two-user and
  two-code/same-user races have one winner. Repeating the winning code by its
  user is idempotent.
- Added CSRF-protected user redemption and SuperAdmin creation/revocation APIs.
  Redemption is limited to five attempts per authenticated user and remote
  address per minute; unavailable codes share one response.
- Added backend-derived onboarding. No license is `LicenseRequired`, redemption
  advances to `ProfileRequired`, and later revocation is `Suspended`. Profile,
  Calendar permission and sync states remain future authoritative inputs.
- Added the authenticated global freeze/unfreeze endpoint. It derives the actor
  from the verified SuperAdmin session and calls the existing atomic
  control-plus-audit store with the HTTP trace correlation ID.
- Migration `AddSingleUseLicensing` was applied from scratch to
  `sirkadiyen_test` and incrementally to the local `sirkadiyen` database.
  PostgreSQL concurrency tests run without skips.
- All 272 .NET tests pass: 173 unit/API/contract tests and 99 real PostgreSQL
  tests. Release build has no warnings, EF reports no pending model changes, and
  HTTPS smoke checks return `200` for health/CSRF and `401` for anonymous
  onboarding, freeze and license-administration requests.

## Previous identity implementation session

- Added the `User` aggregate and the `users` migration with independent unique
  constraints for immutable Google subject and normalized verified email,
  explicit string-backed `role`, UTC sign-in timestamps, and PostgreSQL `xmin`
  optimistic concurrency.
- Added the Google Identity Services sign-in boundary (ADR-052). The API validates
  signature, issuer, audience, expiry and `email_verified`, never persists the ID
  credential, and refuses automatic linking when one email is already owned by a
  different Google subject.
- Added transaction-safe, retry-safe local user creation. Concurrent first
  callbacks for one Google subject converge on the winning row; a different
  subject colliding on email remains a conflict.
- Added `__Host-Sirkadiyen.Session`: HTTP-only, secure, `SameSite=Lax`, eight-hour
  sliding expiry. Session claims are reloaded from the user row on every request;
  missing users are signed out and changed claims rotate the cookie.
- Added a same-site CSRF bootstrap and mandatory anti-forgery validation on Google
  sign-in, logout, revision approval and diff release.
- Google sign-in is limited to ten attempts per remote address per minute; license
  redemption still needs its own limiter when that endpoint exists.
- Replaced the shared admin API key with the `SuperAdmin` authorization policy.
  The ADR-045 email grants the explicit persisted role; approval/release actors
  are now derived from the verified session and no longer accepted from JSON.
- Added API, application/domain and EF-model tests. 156 unit tests and two
  database-free persistence model tests pass; the solution builds with no
  warning, formatting is clean, and an HTTPS smoke test proves health/CSRF,
  secure cookie flags, and `401` for anonymous user/admin reads. The destructive
  PostgreSQL fixture was not run because the configured target could not be
  confirmed as the dedicated `sirkadiyen_tests` database.

## Previous implementation session

- **Holidays and semester breaks publish (ADR-046).** A canonical record is now
  either timed or all-day. 22 Turkish and 11 English rows that were dropped for
  having no times are canonical records, and
  `rows.ignored.noScheduledTimeAndNoClosure` is zero for both sources, so every
  untimed dated row they state is accounted for rather than merely unexplained.
- **The shape decides a closure, not the title.** A row becomes all-day only when
  it states a date, a title naming a closure and no times at all. The sources prove
  why the title alone is not enough: `CUMHURİYET BAYRAMI AREFESİ` is a timed
  three-hour session, and the English workbook writes its own semester break as
  eleven timed 08:30–16:20 rows. A dated row with no times whose title names no
  closure is still refused, with a warning naming the cell, because a lesson whose
  times the faculty forgot must not become an all-day block on every calendar.
- The closure vocabulary is `tatil`, `bayram`, `holiday` and the phrase
  `labor day` — the last included because the English workbook puts it on 1 May,
  the date the Turkish workbook calls `İŞÇİ BAYRAMI`, so the sources identify it
  between them. Any other wording is refused and reported.
- **No span field.** ADR-046 anticipated an inclusive start and exclusive end date,
  but the sources write one row per closed day and the ten `YARIYIL TATİL` rows
  skip the weekend. A stored span would have to invent those days. An all-day
  record covers exactly one local date, and `LocalDate + 1` is the calendar
  adapter's conversion. Consecutive rows are deliberately not merged.
- `IsAllDay` plus two nullable times is one invariant, enforced in the Pydantic
  contract, the domain constructor and a database check constraint whose every
  branch tests nullness explicitly — a check constraint passes on NULL, so a bare
  `"EndLocalTime" > "StartLocalTime"` would have let a timed record with no times
  through the one gate meant to catch it.
- Revision validation now excludes all-day records from the duration and overlap
  rules, and secondary matching can never pair an all-day record with a timed one.
- Every content hash moved again, because `isAllDay` is part of it for both
  profiles. A shape-dependent hash schema would have avoided the churn and is
  exactly how a field silently stops being covered.
- 292 Python tests and 231 .NET tests pass, up from 284 and 214. Migration
  `AddAllDayScheduleItems` was verified Up, Down and Up again against the real
  PostgreSQL, and its rollback guard was proved to refuse while an all-day record
  exists.

## Previous numeric-date-order session

- **Removed the last silent parser assumption.** The shared date resolver read
  every numeric date as day-first. Nothing declared it, and it is the one reading
  rule that can be wrong without leaving evidence: a month-first source would have
  published lessons months from where they belong on every date whose components
  are both twelve or lower, and refused nothing.
- Each `ParserProfileDefinition` now carries a required `numeric_date_order` with
  no default, one of `dayFirst`, `monthFirst` or `undeclared` (ADR-051). A declared
  order is enforced, and a cell only the other order could explain is refused as
  `numericDateImpossibleUnderDeclaredOrder` rather than quietly reordered.
- An undeclared profile still publishes a numeric date when the order cannot change
  the answer — one valid reading, or equal components. That is arithmetic, not
  inference. A date that means two different things is refused as
  `numericDateOrderNotDeclaredByProfile` with the row unpublished and the cell
  cited.
- Every profile declares `undeclared`, because that is what the fixtures support.
  No committed fixture writes a numeric date at all: the new `dates.rule.<rule>`
  metric counts 896 serial and 5 month-name dates in Grade 1 Turkish annual, 953
  serial in Grade 1 English annual, and 60 serial and 100 month-name rotation rows
  in Grade 1 Turkish practice. The day-first branch was dead against every real
  source, which is why nothing had broken and why this was cheap to fix now.
- `GET /v1/profiles` reports the declaration, so an operator looking at a source
  that writes `12/11/2026` can see what the profile will do without reading code.
- Golden output for all three fixtures changed only by the added metrics. No
  candidate, date or content hash moved, so this change would touch no calendar
  event.
- 284 Python tests pass, up from 270, with Ruff check/format and MyPy clean. No
  .NET file changed; its 214 tests were re-run unchanged.

## Previous department and recovery session

- **Fixed a latent defect that made ADR-035 secondary matching unreachable.**
  `Department` was a precondition for it and nothing could ever populate it: the
  parser contract had no such field, so every record had it null. Stable identity
  includes the start time, so every lesson whose time moved was classified as a
  delete plus a create — and through ADR-040 would have held the diff on the
  deletion count. The unit tests never caught it because they construct records
  directly.
- Split the annual `DİLİM ADI / ANABİLİM DALI` cell into canonical
  `CurriculumBlock` and a `Departments` list (ADR-047, ADR-049). A segment becomes
  a department only when it carries an explicit `AD.` / `A.D.` /
  `ANABİLİM DALI` marker; a stated sub-department is kept with it. 419 of 901
  Turkish and 417 of 953 English annual candidates now state a department, 611 and
  547 state a block.
- Integrated sessions ("entegre oturum") keep every department they name, in
  source order — eleven candidates per annual source, up to four departments each.
  An unmarked member of a marked dashed list is kept at 0.9 confidence with an
  indicator; an unmarked segment on its own never becomes a department and is
  reported once per distinct wording with the address of its first cell.
- Amended ADR-035 to a two-tier rule. Both sides naming exactly one department
  score all three attributes; otherwise title and instructor are renormalized
  against a higher composite bar of 0.94. Two disagreeing single departments are
  never re-scored without the attribute that ruled them out. `DepartmentScore` is
  null exactly when the weaker basis was used.
- Implemented stale parse-run recovery (ADR-050). A run left `Running` by a killed
  worker used to wedge that snapshot until the source content changed, silently
  delaying a real schedule change. It is now reopened in place after 30 minutes
  with the attempt count incremented and `LastStaleRecoveryAtUtc` recorded.
- Migration `AddCanonicalDepartmentsAndParseRunRecovery` replaces the never-written
  `Department` column with a required `Departments` JSONB list, adds
  `CurriculumBlock` and `parse_runs.LastStaleRecoveryAtUtc`. The scaffolded version
  renamed `Department` to `CurriculumBlock`, which would have reinterpreted one
  fact as another; it was rewritten as an explicit data migration. Up, Down and Up
  again were verified against the real PostgreSQL.
- Recorded the SuperAdmin address `halil.semih.sen@gmail.com` and the decision
  that the `users` table carries an explicit `role` column from the start, so this
  is not a permanently single-operator system (ADR-045 amendment).
- Every content hash changed, because the raw block cell was replaced in it by the
  split fields. Done deliberately now: after launch the same change would patch
  every managed event for every user.
- 214 .NET tests pass against the real PostgreSQL with nothing skipped, up from
  195. Release build and `dotnet format --verify-no-changes` are clean. 270 Python
  tests pass, up from 239, with Ruff check/format and MyPy clean.

## Previous snapshot-retention session

- Implemented ADR-044 snapshot payload retention. The worker keeps the first
  snapshot captured under each source's currently configured academic year, the
  latest snapshot, every changed snapshot from the last ten days, and every
  snapshot still needed by an absent, running or failed parse run.
- Retention removes only the large normalized JSON payload. Snapshot metadata,
  hashes, parse responses, revisions, canonical records and semantic diffs
  remain, so pruning cannot discard an undispatched calendar change.
- Snapshots now copy the source's configured academic year at acquisition and
  record `PayloadPrunedAtUtc`. Migration `AddSnapshotPayloadRetention` backfills
  existing rows before making the year required.
- Retention is batched, retry-safe, explicitly logged and gated by the global
  operational freeze. The default window is ten days and the default batch is
  fifty payloads.
- Derived selector evidence from the committed fixtures (ADR-048). The catalog
  now declares Grade 1 English `İ1`-`İ3` and Grade 2 Turkish `A`-`H`, in addition
  to the existing Grade 1 Turkish matrix. Grade 2 English remains provisional
  because its fixture is from 2024-2025.
- Rechecked the dated amphitheatre URL with a browser-like GET on 2026-07-23. It
  returned HTTP 200, the XLSX MIME type and 345,269 bytes. The old 403 claim was
  wrong; only discovery of each next dated URL remains open.
- Accepted one Google-verified SuperAdmin bootstrap identity (ADR-045), all-day
  holidays and breaks (ADR-046), and canonical curriculum block (ADR-047).
  ADR-047 is now implemented; ADR-045 and ADR-046 remain follow-up work.
- 195 .NET tests pass against the real PostgreSQL with nothing skipped. The
  Release build and `dotnet format --verify-no-changes` are clean. All 239
  Python tests, Ruff check/format and MyPy also pass.

## Previous operational-freeze session

- Implemented the runtime-readable global operational freeze from ADR-034. One
  singleton PostgreSQL row is the authoritative current state and every actual
  freeze/unfreeze transition is appended with actor, reason, UTC timestamp and
  correlation ID in the same transaction (ADR-043).
- The worker checks the switch before every source. The poller checks again
  immediately before acquisition and after immutable evidence storage, so a
  freeze enabled during an external read keeps the snapshot but never starts or
  resumes a parse run.
- Publication checks immediately before each revision. Frozen publication is an
  explicit outcome; a failed state read throws before the store is called, which
  makes the boundary fail closed.
- Added the operator-key-protected read-only
  `GET /api/operations/freeze`. A freeze/unfreeze write endpoint remains
  intentionally absent until real operator authentication exists.
- Added migration `AddOperationalFreeze`, model tests, domain/application gate
  tests and PostgreSQL transaction/audit tests. The migration applies cleanly to
  the real test database.
- 188 .NET tests pass against the real PostgreSQL with nothing skipped. Release
  build and `dotnet format --verify-no-changes` are clean.

## Previous held-diff release session

- Added the operator path for a held diff (ADR-042), the last unimplemented part
  of ADR-040. A `Held` diff now reaches dispatch through a new `Released` state,
  reached by `POST /api/diffs/{id}/release` behind the existing operator key,
  which records who took responsibility, why, and when.
- An ambiguity hold is deliberately not releasable, enforced in the domain and
  not only at the endpoint: an operator can confirm a large deletion by reading
  the source but cannot decide which of several candidates a record became.
- `Released` is a separate state from `Ready` on purpose, so a consumer can tell
  an automatically safe diff from one a human vouched for. The hold reason
  survives the release.
- Added the hold queue and a detail view that names the lessons rather than
  record identifiers, lists deletions first and excludes unchanged entries.
- Release is guarded by the diff's row version (PostgreSQL `xmin`, no new
  column), so two operators acting at once get a refusal rather than a silent
  overwrite. Migration `AddScheduleDiffRelease` adds three nullable columns.
- Verified end to end against the real database: 401 without the key, the queue,
  the detail, a release, 409 on a second release, 400 on missing fields, and 409
  refusing an ambiguous hold.
- 176 .NET tests pass against a real PostgreSQL with nothing skipped, up from
  163. Release build and `dotnet format --verify-no-changes` are clean.

## Previous configuration session

- Fixed `dotnet run` failing with a missing `SIRKADIYEN_DATABASE:CONNECTION_STRING`
  even though `.env` declared it: nothing in the .NET solution ever read that
  file, which only Docker Compose consumed.
- Added `DotEnvFile` in Infrastructure. It searches upward from the assembly's
  output directory for the nearest `.env` and applies only variables the process
  environment does not already define, so an exported or injected value stays
  authoritative (ADR-041). No new package: the parser is about eighty lines and
  the precedence rule is specific to us.
- Both hosts call it before creating their builder, because the
  environment-variable configuration provider reads the environment as it is
  added. The design-time context factory and the PostgreSQL test fixture call it
  too, so `dotnet-ef` and `dotnet test` also need no manual export.
- A malformed line fails with the file and line number and never the value.
  There is no inline comment syntax on purpose: a password may contain `#`.
- Verified by starting the API from a clean shell — it reached `/health` with no
  variable exported.
- 163 .NET tests pass against a real PostgreSQL with nothing skipped, up from
  145. Release build and `dotnet format --verify-no-changes` are clean.

## Previous diff persistence session

- Added the `ScheduleDiff` aggregate: the stored difference between a published
  revision and the one it superseded, with its counts, its per-record entries,
  and the state that says whether it may be acted on.
- Added the dispatch gate (ADR-040). Any `Ambiguous` entry holds the diff, as
  does a deletion count that both reaches the minimum and exceeds the tolerated
  share of the previous revision. The reason is stored in full and written
  invariantly, with a regression test under `tr-TR`.
- Diff calculation runs after publication in its own transaction and is driven
  by revision state (ADR-039), so a worker killed between the two steps
  recovers, and a revision superseded before it was diffed is still diffed.
- A unique index on the current revision makes calculation idempotent: the
  losing pass reports the existing diff instead of writing a second set of
  future calendar operations.
- Added migration `AddScheduleDiff` with `schedule_diffs` and
  `schedule_diff_entries`. Both tables are new; nothing existing is altered.
  Diff rows restrict deletion of the revisions and canonical records they cite,
  because they are the audit trail for what was written to a calendar.
- Added `SIRKADIYEN_DIFF__*` configuration for the gate. The ADR-035 matching
  thresholds stay on their defaults on purpose: loosening them from
  configuration would let an operator turn two different lessons into one update
  without a decision record.
- 145 .NET tests pass against a real PostgreSQL with nothing skipped, up from
  116. Release build and `dotnet format --verify-no-changes` are clean.

## Previous semantic diff session

- Added the semantic diff domain model with `Created`, `Updated`, `Deleted`,
  `Unchanged` and `Ambiguous` outcomes and explicit match evidence.
- Implemented exact stable-identity/content-hash matching and deterministic
  secondary matching for time shifts. Secondary matching requires the same
  source context, date, type and audience plus lesson title, instructor and
  academic department over normalized Levenshtein thresholds (ADR-035).
- One-to-many and many-to-one candidates remain `Ambiguous`; they do not also
  become destructive create/delete entries.
- Added nullable `Department` to canonical storage with additive migration
  `AddCanonicalDepartment`. Existing records remain null and are never inferred.
- Added a semantic diff regression matrix and model/migration tests.
- Recorded forward-fix, global freeze, Next.js, Hangfire and recurring-row
  exclusion decisions as ADR-033 through ADR-038.

## Previous publication session

- Implemented transactional publication. `Validated → Published` and the
  previous live revision's `→ Superseded` commit together; the worker publishes
  every validated revision at the end of each cycle, driven by state rather than
  by what that cycle parsed, so a crash between validation and publication costs
  nothing.
- Superseding and publishing are two `SaveChanges` calls inside one transaction:
  "one published revision per source" is a partial unique index, which cannot be
  deferred, so the outgoing revision must vacate the slot first.
- Publishing an older revision over a newer live one is refused, because it would
  move a source's schedule backwards and the diff would read that as a mass
  deletion.
- Added the approval audit trail: `ApprovedBy`, `ApprovalReason`, `ApprovedAtUtc`
  on `schedule_revisions`, migration `AddRevisionPublicationApproval` (additive).
  Approval only reaches `Validated`, so an approved revision publishes through
  exactly the same transaction as one that was never held.
- Added the internal administration API behind a required `SIRKADIYEN_ADMIN__API_KEY`:
  the review queue, one revision with its findings, and
  `POST /api/revisions/{id}/approve`. Verified end to end against the real
  database, including 401/409/400 paths.
- **Fixed a production-only bug that predates this session.** Every store that
  opened its own transaction — snapshot storage, parse completion, validation —
  threw under the hosts' `EnableRetryOnFailure` configuration: the worker would
  have failed on its first real poll. The test fixture did not enable retry, so
  116 passing tests never touched it. All four transactional stores now go
  through `RetriableTransaction`, and `RetriableTransactionTests` exercises them
  against a host-configured context.
- Fixed culture-dependent finding messages: a Turkish host wrote the confidence
  threshold as `0,50` into stored evidence. Thresholds and shares are now
  invariant, with a regression test that runs under `tr-TR`.
- 116 .NET tests pass against a real PostgreSQL with nothing skipped, up from 88.

## Previous validation session

- Implemented record and revision validation. `ScheduleRevisionValidator` is a
  pure function; `ScheduleRevisionValidationStore` transitions the revision and
  writes findings in one transaction; the poller validates the revision it just
  created, and `ValidatePendingAsync` recovers anything an interrupted cycle
  left in `Parsed`.
- Rules: empty revision, date outside the academic year, impossible duration, low
  confidence, unknown audience selector, duplicate stable identity, audience
  overlap, and mass deletion. Any `Error` quarantines; only an empty revision is
  rejected (ADR-029).
- The deletion rule requires both `> 20 percent` and `>= 10` records, so a small
  source cannot trip on the share alone.
- Added `supportedAudienceSelectors` to the source catalog, the domain source and
  a JSONB column. Null means "not declared" and leaves the unknown-selector rule
  unenforced; a declared but empty dimension asserts the dimension may not appear.
- Excluded PDÖ from `grade1_practice_v1` (ADR-030), accounting for every dropped
  cell through `cells.ignored.outOfScopeSubject` rather than discarding silently.
  Grade 1 Turkish practice now yields 402 candidates instead of 426, and its
  cohorts are a clean `A`-`H` with `1`/`2` subgroups.
- Added migration `AddRevisionValidation`: a nullable JSONB column on
  `schedule_sources` and the `revision_validation_findings` table. Both additive.
- Fixed two pre-existing persistence tests that queried whole tables with
  `SingleAsync`, so they only passed when they happened to run first.
- All 88 .NET tests pass against a real PostgreSQL with nothing skipped, up from
  42 passing and 16 skipped. Python: 239 pass, ruff/format/mypy clean. Release
  build and `dotnet format --verify-no-changes` are clean.

## Previous polling and parser-transport session

- Added `ScheduleSourcePoller` and wired the Worker to seed the 18-source
  catalog, list polling-enabled sources, process them sequentially, and avoid
  overlapping cycles.
- Added ADR-026's configurable Istanbul-time schedule: 15 minutes in weekday
  daytime, 25 minutes in late afternoon, 45 minutes at night, and 60 minutes on
  weekends.
- Added the strict `ParserHttpClient`; it rejects non-success responses and any
  successful response that does not echo contract, correlation, source,
  snapshot, and profile identifiers exactly.
- Added parse-run start/resume and result persistence. A failed parser transport
  attempt increments `AttemptCount` on the same deterministic run, including
  when the next source acquisition is unchanged.
- Candidate revisions and canonical records are now created in one transaction.
  Candidate ID and scheduled/cancelled status are retained in canonical storage
  rather than discarded.
- Added migration `PreserveParserCandidateStatus` with a safe data migration for
  existing rows.
- Recorded the accepted license, session, calendar, anomaly, polling, and JSONB
  profile decisions as ADR-022 through ADR-027.
- Added polling-boundary, orchestration, parser HTTP, model, migration, and
  PostgreSQL integration tests. The current environment passed 42 .NET tests;
  16 PostgreSQL tests were explicitly skipped because Docker/PostgreSQL was not
  available. Release build and formatting verification pass with zero warnings.

## Previous persistence and Grade 1 practice session

- Implemented `grade1_practice_v1` for the rotation-matrix practice program:
  426 candidates from the Grade 1 Turkish source, with 20 refused cells that are
  all makeup markers naming no group.
- A candidate there is a cell, not a row: the group comes from the cell, the
  subject from the column header, and the date and time from the row.
- Added the lettered-cohort model to the shared group resolver (ADR-020) after
  the real source showed that reading `G` as an abbreviation silently dropped
  group G and turned subgroup `G2` into group 2.
- Added `record_ignored_cell` so matrix sources account for every unpublished
  cell the way row sources account for every unpublished row.
- Added the PostgreSQL schema for sources, snapshots, parse runs, revisions and
  canonical records (ADR-021), with EF Core 10, Npgsql, one migration and a
  design-time factory.
- Implemented the unchanged-source short circuit inside a transaction that locks
  the source row, and proved it against a real database.
- Added `academicYear`, `classYear`, `programLanguage` and `timeZoneId` to the
  source catalog, so the source context ADR-017 requires has a configured home,
  and the catalog now seeds `schedule_sources`.
- Added Docker Compose for PostgreSQL and Redis.
- Ran ruff, ruff format, mypy strict and pytest (237 passing) plus the .NET
  Release build with zero warnings and all 40 .NET tests, 24 of them against a
  real PostgreSQL.

## Previous parser-profile session

- Added `sourceContext` to the parse request in both the C# and Pydantic
  contracts, carrying academic year, class year, program language and timezone
  (ADR-017), and updated the shared contract fixture and both test suites.
- Implemented `grade1_yearly_v1` in `src/parser/sirkadiyen_parser/parsers/`,
  with a parser registry that separates a described profile from an implemented
  one; `/v1/parse` now runs it and `/v1/profiles` reports implementation status.
- Columns are selected by Turkish and English header aliases, so one profile
  serves `G1-TR-ANNUAL` and `G1-EN-ANNUAL`; worksheets without a header row are
  skipped with a recorded reason, and a snapshot with no parsable worksheet is
  rejected rather than reported as an empty success.
- Added stable identity and content hashing (ADR-018) and the rule that a second
  row claiming a published identity is refused, informationally when the rows
  are identical and as a warning when they disagree.
- Confirmed on real data that the sources contain time cells the spreadsheet
  software converted into dates; format-driven resolution refuses them instead
  of publishing midnight lessons.
- Extended the shared primitives: instructor titles written without spaces
  (`Prof.Dr.`), trailing-instructor splitting that never truncates a title, and
  ordinal stripping for `1-` style lecture numbers.
- Added parse golden files as digest projections (ADR-019) and committed the
  Grade 1 English annual snapshot fixture.
- Ran ruff, ruff format, mypy strict and pytest (204 passing) plus the .NET
  Release build with zero warnings and all 16 .NET tests.

## Previous catalog and fixture session

- Added `config/schedule-sources.json` with all 18 supplied source IDs, URLs,
  transports, document formats, parser profiles, and fixture mappings.
- Verified representative Google Sheets and Drive exports against collected
  fixture bytes. A generic amphitheatre probe returned HTTP 403 in that session;
  this was later shown not to be a valid availability test because a
  browser-like GET returns the XLSX with HTTP 200.
- Added a deterministic Open XML fixture converter and snapshot CLI with
  semantic used-range trimming.
- Generated and contract-validated the Grade 1 Turkish annual and practice
  normalized snapshots.
- Added read-only Google credential composition for either an offline refresh
  token or a service account; client ID/secret alone remains insufficient.
- Added six .NET regression tests and two Python real-snapshot contract tests;
  all 15 .NET tests and all 139 Python tests pass.

## Previous ingestion implementation session

- Added the application-layer `ISpreadsheetSnapshotAcquirer` port with explicit
  source, snapshot, spreadsheet, acquisition-time, and range inputs.
- Added the Google Sheets v4 production adapter and pinned
  `Google.Apis.Sheets.v4` 1.75.0.4178.
- Added deterministic normalization of typed values, formulas, notes, effective
  formatting, merges, hidden dimensions, sparse cells, requested ranges, and A1
  evidence addresses.
- Added overlap-conflict diagnostics and SHA-256 content hashing over normalized
  content plus acquisition diagnostics (ADR-014).
- Added a dedicated infrastructure test project with six mapper/hash regression
  tests; the Release build and all nine .NET tests pass.

## Previous parser implementation session

- Added the shared parser normalization primitives under
  `src/parser/sirkadiyen_parser/normalization/`: text folding and identity keys,
  merge-aware grid access with evidence construction, date, time, group, course
  title and instructor resolvers.
- Established the no-inference rule: every resolver reports its rule, confidence
  and a reason when unresolved, and serial dates, missing years and compact
  times are opt-in per parser profile (ADR-011).
- Added `ParseDiagnostics`, which accounts for every ignored row by reason and
  derives the parser result status from what was recorded.
- Added `PARSER_ENGINE_VERSION` covering the shared primitives, separate from
  the transport contract version and the parser-profile versions.
- Added the golden-file harness with explicit regeneration and a direct
  determinism assertion (ADR-012), plus a labelled synthetic snapshot fixture.
- Split the Pydantic contract bases so inbound models stay camel-case-only while
  the parser can construct outbound response models by field name (ADR-013).
- Ran ruff, ruff format, mypy strict and pytest (137 passing) plus the .NET
  Release test run (3 passing).

## Earlier sessions

- Added the root `Sirkadiyen.slnx` solution.
- Added Domain, Application, Contracts, Infrastructure, API, and Worker projects.
- Enforced nullable reference types, latest analysis, warnings-as-errors, and
  deterministic builds through `Directory.Build.props`.
- Added repository formatting, ignore, environment placeholder, and SDK pinning files.
- Added a minimal API health endpoint and cancellable worker host.
- Verified a Release build with zero warnings and zero errors.
- Verified formatting with `dotnet format --verify-no-changes`.
- Reconciled the source manifest with all currently identifiable fixtures.
- Inspected all 17 XLSX fixtures and documented the annual, practice, and weekly
  amphitheatre structural families and known fixture gaps.
- Added the v1 normalized spreadsheet snapshot and parser request/response
  contracts.
- Added camel-case JSON serialization with camel-case string enums.
- Added the first .NET unit test project and contract serialization tests.
- Confirmed the Grade 2 anatomy and vertical-corridor DOCX source families and
  recorded their cross-program and annual-program matching rules.
- Added the Python 3.13 FastAPI parser service foundation and strict Pydantic v1
  transport models mirroring the C# contracts.
- Added the versioned parser profile registry, including independent
  `anatomyGroup` selectors and annual `Diseksiyon`/`Uygulama` markers.
- Added a shared JSON fixture validated by both .NET and Python tests.
- Added Ruff, Mypy, pytest, and HTTP endpoint quality gates.

## Current confirmed requirements

- Google-only registration and login
- administrator-issued license code activation
- user profile collection after activation
- user-triggered initial synchronization
- support for first, second, and third years
- support for Turkish and English programs where sources exist
- Python is parser-only
- source schedules mix Google Sheets, Drive XLSX/DOCX files, and HTTP XLSX files
- sources may change daily
- polling and change detection are required
- only changed calendar events should be modified
- source formats are irregular and require specialized parser profiles
- raw source fixtures will be placed under `sheets/`
- first- and second-year anatomy groups use `1`, `2`, and `3`
- anatomy group is independent from the normal practice group
- second-year anatomy and vertical-corridor schedules are shared by Turkish and English programs
- annual programs label anatomy lessons as `Diseksiyon`
- annual programs label vertical-corridor and other practice lessons as `Uygulama`

## Immediate objectives

1. Model Calendar authorization and the dedicated managed calendar, extending
   derived onboarding beyond `CalendarAuthorizationRequired` toward
   `ReadyForInitialSync`. The validated student profile is now implemented
   (ADR-055).
2. Implement `grade2_yearly_v1`, which should reuse the annual implementation
   with its own header aliases. Its block/department cell is expected to follow
   the ADR-049 convention, which the fixture must confirm, and its date column
   must be checked for a numeric form before its profile declares an order
   (ADR-051).
3. Widen the group resolver for the confirmed Grade 1 English practice cohorts
   `İ1`, `İ2` and `İ3`; the source also lays dates out differently.
4. Establish .NET architecture tests.
5. Acquire the missing Grade 1 anatomy, current Grade 2 English and Grade 3
   English fixtures.
6. Add Google Drive/HTTP acquisition and DOCX conversion for the confirmed
   special-program sources.
7. Add CI quality gates, including a PostgreSQL service for the integration
   tests.
8. Model Calendar authorization, initial sync, and the dedicated managed calendar
   from ADR-023 through ADR-027.

## Product gaps

Lessons the sources state that no profile publishes yet. These are tracked as
product work, not as parser defects; a student's calendar is knowingly
incomplete until they are closed.

- thirteen rows whose time cells the spreadsheet software converted into dates
  (seven Grade 1 Turkish, six Grade 1 English); these need a source-side fix
- twenty Grade 1 Turkish practice cells reading only `TELAFİ`, naming no group

PDÖ and recurring undated rows are **not** on this list. They are deliberately
out of scope (ADR-030, ADR-038).

## Grade 1 practice source structure

Implemented by `grade1_practice_v1`. Unlike the annual sources it is not
row-per-lesson:

- The worksheet holds several blocks, one per curriculum block (`TIBBA MERHABA
  DİLİMİ`, `YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ`, `HÜCRE DİLİMİ`, …), separated
  by blank rows and introduced by a merged heading row.
- Each block has its own header row: `Uygulama Tarihi`, `Saat`, then one column
  per practice subject. Later blocks add a `Dikey Koridor` heading spanning
  several subject columns, and subject headers there carry the instructor on a
  second line.
- A data cell holds the group letter or letters attending that practice in that
  slot: `A`, `AB`, `C1`, `E2`, or the words `Telafi` / `TELAFİ` for a makeup.
- Dates appear both as serials and as Turkish text with a weekday. Times are
  ranges in one cell, written with `:` or `.` separators.
- Blocks end with an `Uygulama Sayısı` totals row, followed by
  `UYGULAMA KONU BAŞLIKLARI` free-text topic lists per department, and notes.
- Rows 1 to 23 are location and skill-laboratory lookup tables, not schedule.

The `HAREKET DİLİMİ` block contains a second schedule table nested inside
columns E to G, listing 21 anatomy practice dates with no group column. The
profile detects it, reports it, and reads none of its columns. Those anatomy
sessions cannot be published until the missing Grade 1 anatomy source supplies
the group assignment.

The `HAYATIN EVRELERİ DİLİMİ` block has three dated rows but no subject header,
so it carries no rotation to publish.

## Important unresolved decisions

### Source acquisition operations

- polling interval and retry policy per transport
- whether Google Drive metadata is used for a preliminary change signal
- discovery strategy for each next dated amphitheatre CDN URL. The current
  dated URL is reachable with a browser-like GET; a generic HEAD probe is not an
  availability contract

### Publication governance

Resolved (ADR-029, ADR-032). Publication is automated and gated by the validation
safety nets. A `ReviewRequired` revision reaches publication only through an
approval that names its approver and states a reason, over the internal API.

Forward-fix without rollback and the global freeze are resolved and implemented
by ADR-033, ADR-034 and ADR-043, including the authenticated audited write
surface.

The initial operator model is implemented from ADR-045 as amended: one
Google-verified SuperAdmin, `halil.semih.sen@gmail.com`, grants the explicit
`role` on `users`. Administrative reads, revision approvals and diff releases
use that policy and derive actors from the authenticated session, including
freeze/unfreeze and license administration.

### Profile schema

The derivation rule and currently confirmed values are accepted in ADR-048 and
now implemented as the server-owned code schema in ADR-055 (Grade 1 Turkish and
English only). Still missing: a current Grade 2 English fixture and Grade 3
English fixtures, plus the Grade 1 anatomy source before an `anatomyGroup`
dimension can be added with evidence.

## Known risks

- spreadsheet formats may change without warning
- merge and formatting metadata may carry semantic meaning
- source deletion may be temporary or accidental
- course titles may not be stable enough for identity
- users may revoke Google authorization
- concurrent sync jobs may duplicate events without strong idempotency
- initial sync may hit Google API quotas
- proxy deployments must configure trusted forwarded headers before the
  authentication and license rate limiters can treat the remote address as the
  internet client
- rotating `SIRKADIYEN_LICENSING__HASH_KEY` makes every unredeemed license
  impossible to look up; rotation needs an explicit invalidation/reissue plan
- a profile change may require removing and adding many events safely
- weekly amphitheatre data may conflict with annual schedules
- the shared resolvers are calibrated against synthetic fixtures only, so real
  sources will contain date, time and group forms they refuse; each refusal must
  be reviewed as evidence before the resolver is widened
- a source that writes numeric dates will publish nothing from its ambiguous ones
  until its profile declares an order (ADR-051). This is the intended direction —
  refusing beats misdating — but it means the first such source arrives partially
  parsed, and the operator has to read the refused cells and declare the order
  before that source is usable
- group values are capped at two digits, which is correct for every confirmed
  cohort but would refuse a three-digit group if one is ever introduced
- the annual sources contain time cells that the spreadsheet software converted
  into dates; the parser refuses them, so seven Grade 1 Turkish rows and six
  Grade 1 English rows are currently unpublished and need a source-side fix
- an all-day closure reaches Google Calendar as one event per closed day, since
  the sources state one row per day and consecutive rows are deliberately not
  merged. A student sees ten `YARIYIL TATİL` entries rather than one span, which
  is faithful to the source and may still read as noise; merging is a product
  decision, not a parser one
- the closure vocabulary is `tatil`, `bayram`, `holiday` and the phrase
  `labor day`. A closure worded differently — an administrative closure, a snow
  day, an election — is refused rather than published, and the refusal names the
  cell so the list grows from evidence
- the two annual sources disagree about the semester break: Turkish states it as
  untimed rows, which now publish as all-day items, and English states the same
  break as eleven timed rows labelled `theory`. Both are published as stated, so
  the two programs' calendars show that break differently
- the annual event type is keyword-classified, so a lecture whose title mentions
  a practice is labelled `practice`; four Grade 1 Turkish lessons are affected
  and only the label is wrong
- a department the source states without a marker, such as `TIBBİ EKOLOJİ VE
  HİDROKLİMATOLOJİ`, is deliberately not published (ADR-049). Each such wording is
  reported once with its cell address, and widening the rule must start from that
  evidence rather than from knowing Turkish faculty structure
- matching without a comparable department (ADR-035 as amended) rests on title and
  instructor alone. It demands a composite of 0.94, but two genuinely different
  lessons on the same date with the same instructor and nearly the same title
  would match. The uniqueness rule turns that into `Ambiguous` rather than a
  destructive pair whenever both are plausible candidates
- an integrated session's departments are kept for display and excluded from
  matching. If a session's title and instructor are also unstable, it has no
  strong attribute left and falls back to delete-and-create
- two workers recovering the same stale parse run at once may both call the
  parser; only one response can complete the run, but `parse_runs` has no row
  version, so this relies on the worker running one instance with
  non-overlapping cycles
- the Grade 1 English practice source labels cohorts `İ1`, `İ2`, `İ3` and lays
  its dates out differently, so `grade1_practice_v1` publishes almost nothing
  from it; its fixture is deliberately not committed until the source has been
  reviewed
- reading `AB` as groups A and B follows from the cohort model rather than from
  the cell, so those candidates carry reduced confidence
- snapshot payload retention keeps the active-year anchor, latest content, last
  ten days and parser-recovery inputs. Full parse/revision/diff lineage cleanup
  remains intentionally absent until diff dispatch has a durable completion
  marker
- an audience selector that a source has not declared cannot be detected, because
  the unknown-selector rule is unenforced wherever `supportedAudienceSelectors`
  is absent. Only `G1-TR-PRACTICE` declares its cohorts today
- overlap detection compares exact selector sets, so a lesson for group `A` and
  one for subgroup `A1` at the same time are not seen as overlapping
- Google sign-in currently supports the same-site HTTPS browser topology only.
  A separately hosted frontend must not be enabled until credentialed CORS and
  the cookie `SameSite` policy are explicitly designed and tested
- local sessions reload the user row on every authenticated request. This makes
  role changes immediate and safe, but it adds a database read to the hot path;
  any later cache must preserve revocation semantics rather than trading them
  away silently
- ASP.NET Core Data Protection still uses its host default. A container or
  multi-instance production deployment needs a shared persistent key ring before
  sessions can be expected to survive restarts or move between instances
- the diff gate's deletion share is computed against the previous revision as
  the diff accounted for it. A source that legitimately shrinks — an ended
  semester block — is held on every revision that shrinks it, and each one needs
  its own release; the release is recorded but is not remembered for next time
- diff rows restrict deletion of the revisions and canonical records they cite,
  which is deliberate but makes snapshot and revision retention harder: a
  retention policy must retire diffs alongside what they reference
- the `.env` loader mutates process-global environment state. It is called once
  at the top of a host's composition and from no library code reached at request
  time; a call added anywhere else would make configuration order-dependent
- pointing `SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING` at a working database
  in `.env` now destroys it, because the fixture drops and re-migrates whatever
  it is given and no longer needs a deliberate export to find it
- a student profile stored under one supported-schema version is not re-validated
  when the schema changes at academic-year rollover (ADR-055). A cohort that
  disappears from the new fixture leaves existing profiles pointing at it until an
  explicit re-validation pass is built; audience resolution must not assume a
  stored selector is still a published cohort
- the supported-profile schema models a one-level parent dependency only
  (subgroup under group). A future requirement such as a rotation within a
  subgroup would need to extend the model; a test asserts the current depth so the
  limit is visible rather than silently violated
- the profile write path enforces the onboarding order by requiring an active
  license, but a license revoked after a profile is saved leaves the stored
  profile in place; onboarding correctly reports `Suspended` from the license
  state, and the profile row is retained deliberately for later reactivation
- the student number's entry-year digits (5-6) are not cross-checked against the
  academic year, and the number is not enforced unique across profiles (ADR-056).
  Both are deliberate: neither rule is confirmed against real registrar data, and a
  repeat student or a legitimately shared prefix must not be rejected. A future
  integration that needs uniqueness or an entry-year rule would add it with evidence

- the stored Google refresh tokens are only decryptable by the Data Protection key ring
  that encrypted them (ADR-057). The API and the worker now share one ring via
  `AddSirkadiyenDataProtection` (fixed application name plus a configurable file-system key
  path), which is correct for a single host; a containerized or multi-instance deployment
  still needs genuinely shared, backed-up key storage or every stored token is lost on
  restart, silently forcing all users to authorize again
- the real Google authorization-code exchange and the real Calendar client (calendar
  creation, event insert/patch/delete, and the transient/credential exception classification and
  back-off) have no automated coverage. They require a registered Web OAuth client and a live
  consent, so only the fakes are tested; the first live grant and the first live sync are the
  first real exercise of those paths
- a Calendar grant is never re-verified against Google after it is stored. Access revoked from
  the Google account side is discovered only when synchronization tries to use the token;
  incremental dispatch now flags the connection `NeedsReauthorization` when that happens (ADR-059),
  but initial sync does not yet, and no proactive re-verification exists
- calendar creation is recovered by an exact per-user description marker before initial sync
  creates another calendar (ADR-063). Existing grants issued before the CalendarList read-only
  scope was added can still use an already-attached calendar, but require a new consent if
  marker lookup is needed
- a user's **initial** sync that keeps failing — a bad or revoked token, a persistent Calendar
  error — is logged and retried every worker cycle indefinitely. The incremental dispatcher now
  has the failure taxonomy (transient back-off then `Failed`, dead credential → `NeedsReauthorization`),
  but initial sync does not yet share it
- initial sync writes at most a configured number of events per user per cycle to respect
  Calendar quota, so a very large first load completes over several cycles rather than at once;
  onboarding stays `InitialSyncInProgress` until the last applicable event is written
- periodic inventory now repairs expected event/ledger drift, including manually deleted or
  visibly edited managed events. Duplicate/unexpected events and mappings are deliberately
  reported but retained: automatic cleanup would manufacture deletion authority outside a
  published dispatchable semantic diff
- reconciliation has no persisted failure/back-off state of its own. The Google client exhausts
  bounded in-call back-off, then a transient replay or inventory failure retries on the next
  worker cycle; a persistently failing user can therefore be attempted every cycle
- a deleted or inaccessible managed calendar is detected and surfaced as `ActionRequired`, but
  no automatic recreate/migrate workflow exists. Recreating a whole calendar is an explicit,
  operator-visible product action because it changes the user's external container
- the real CalendarList marker lookup and managed-event inventory adapter have no live automated
  coverage; fakes cover application behavior and PostgreSQL covers scheduling/fencing
- incremental dispatch now bounds each admitted diff by per-user semantic mutation units
  (ADR-065). This is not an exact Google HTTP-request ceiling: a patch that discovers a missing
  event issues a recovery insert as part of the same mutation unit, so a heavily drifted batch may
  use up to two write calls per patch. Persistently transient-failing diffs still back off and,
  after `MaxDispatchAttempts`, move to `Failed` for an operator
- partial passes re-enumerate the diff and affected users on the next cycle; Calendar writes are
  bounded and completed work falls out through the ledger, but cohort read/planning volume is not
  paged inside one entry

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable

## Latest implementation session (2026-07-25, Calendar backlog catch-up)

- The Grade 1 annual forward-fix produced a `Ready` diff with 862 creates. Incremental
  dispatch correctly stopped after its configured 100 Calendar mutation units, leaving
  the diff `Pending`; the user's ledger therefore held the original 51 practice events
  plus 100 annual events.
- The apparent stall was scheduler coupling, not revoked Google access: quota yielding
  could resume only on the next source-poll cycle, which is one hour on weekends.
- ADR-070 separates ordinary quota-yield catch-up timing from source polling. A partial
  incremental dispatch, in-progress initial sync, or in-progress reconciliation now
  schedules another Calendar-only pass after five seconds. Source polling, publication,
  diff calculation, and snapshot retention are skipped during those catch-up passes.
- ADR-082 later closes the remaining admission gap: a Calendar request created after
  an empty pass is found by the independent idle check instead of waiting for the next
  adaptive source poll.
- The 100-mutation safety bound and durable-ledger replanning remain unchanged.
- Verification: 325 Infrastructure unit tests passed; the full Release solution build,
  `dotnet format --verify-no-changes`, and `git diff --check` passed.

## Latest parser session (2026-07-25, annual practice placeholders)

- The published `G1-TR-ANNUAL` revision contained 134 records whose complete title
  was `UYGULAMA`. They were whole-program slot placeholders, while
  `G1-TR-PRACTICE` published the authoritative group-specific lesson in the same
  slot. A Group B student therefore received both `UYGULAMA` and, for example,
  `Temel Biyofizik`.
- ADR-071 excludes only a normalized one-token `UYGULAMA` or `PRACTICE` annual
  title. Named lessons such as `Anatomi Uygulama 14/21`, `FİZYOLOJİ UYGULAMA`,
  laboratory skills, and theory titles containing the word remain.
- `grade1_yearly_v1` is now 1.3.0, forcing retained snapshots to re-parse. The
  resulting forward-fix revision deletes the placeholders through a published
  semantic diff; no direct Calendar cleanup is used.
- Real fixture results: TR candidates 855 → 721 with exactly 134
  `rows.ignored.outOfScopePracticePlaceholder`; EN remains 893.
- Verification: 301 Python tests, Ruff lint/format, Mypy, 325 Infrastructure tests,
  full Release build, .NET format, and diff checks passed. The live parser reload
  advertises 1.3.0.

## Latest implementation session (2026-07-25, Calendar presentation)

- ADR-072 centralizes Google-visible event formatting in
  `CalendarEventPresentationPolicy`, so the same rules automatically apply when later
  class-year parsers populate the canonical department fields.
- Calendar-scoped event labels avoid the legacy eleven-color limit. The requested
  core department/exam/free-study colors are fixed; every other department receives a
  deterministic custom RGB color and UUID label.
- Calendar summaries preserve source-authored lecture sequence numbers. Descriptions
  label instructor, curriculum block, and department(s).
- Annual profile 1.4.0 counts but omits "consult the amphitheatre program" location
  instructions. Calendar output also suppresses previously stored copies.
- Inventory equivalence includes `EventLabelId`, so existing events are patched to the
  new presentation during normal reconciliation.

## Latest implementation session (2026-08-03, dynamic department colors)

- Parser work remains paused while Grade 2 Team Work and Grade 3 audience rules await
  authoritative information. No parser behavior was guessed in this session.
- ADR-086 replaces code-only department colors with a three-level policy: personal
  override, administrator default, then system default. Exams and free-study keep their
  product-owned colors.
- The code-owned identity catalog contains the faculty's 45 departments (10 basic,
  21 internal, 14 surgical) plus reviewed Turkish/English aliases. Aliases affect only
  presentation identity; unknown source values remain distinct and deterministic.
- PostgreSQL stores admin defaults, user overrides and append-only audit entries. Admin
  changes require a reason. A change marks completed calendars due for non-destructive
  inventory so existing label definitions and events converge on the next worker pass.
- Authenticated users manage their own colors at `/api/calendar/colors`; SuperAdmin
  manages defaults at `/api/admin/calendar-colors`. Both dashboard surfaces are wired.
- Migration `AddDepartmentColorPreferences` adds the new tables and constraints.
- Verification: solution build, 409 Infrastructure + 6 Contracts + 5 API unit tests,
  frontend TypeScript typecheck and production build, EF migration SQL generation,
  `dotnet format --verify-no-changes`, and `git diff --check` pass. PostgreSQL-backed
  integration tests could not run because Docker Desktop/PostgreSQL was unavailable at
  `127.0.0.1:15432`; the test fixture failed during initialization, not in an assertion.

## Latest frontend session (2026-08-03, admin workspace split)

- ADR-087 turns `/admin` into an orientation-only overview and gives every admin
  navigation area a real route. Source upload, revision review, department colors,
  operational freeze, license creation/revocation and self-activation now live in
  their own workspaces.
- Areas whose backend contracts do not exist use explicit empty states; the UI does
  not invent operational records or metrics.
- The department-color surface is now a searchable and division-filtered palette
  studio with aggregate counts, calendar-style previews, explicit save/reset actions,
  override provenance and a persistent audit-reason field for admin mutations.
- Verification: frontend TypeScript typecheck and Next.js production build passed.
