# Active Context

## Current phase

The identity and activation foundation is implemented: Google ID credentials are
verified server-side, users are persisted, browser sessions use secure cookies
with CSRF protection, single-use licenses activate accounts transactionally, and
administration is authorized by the explicit `SuperAdmin` role. The validated
student profile is implemented (ADR-055): an activated account now derives
`ProfileRequired`, then `CalendarAuthorizationRequired` once a profile exists.
Calendar authorization and the dedicated managed calendar are the next user
module. Semantic diff remains complete through persistence; nothing dispatches it
yet.

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

Nothing consumes a diff yet. Affected-user resolution, Drive and HTTP
acquisition, DOCX conversion, student profiles, Calendar authorization/sync, and
the frontend do not exist yet.

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

## Working assumptions

- schedule interpretation timezone is `Europe/Istanbul`
- Python receives snapshots from .NET
- routine sync is one-way from Sirkadiyen to Google Calendar
- user edits to managed events are not authoritative
- all managed events are traceable through extended properties
- parser profiles are versioned
- raw snapshots are immutable
