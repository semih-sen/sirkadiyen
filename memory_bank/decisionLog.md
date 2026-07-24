# Decision Log

This file is append-only. Do not erase historical decisions. Mark decisions as superseded when necessary.

---

## ADR-001: Use a .NET primary backend

**Status:** Accepted
**Date:** 2026-07-21

### Context

The rewrite requires authentication, licensing, user profiles, background jobs, transactional state changes, Google API integration, incremental synchronization, and administrative workflows.

### Decision

Use .NET 10 and ASP.NET Core for the primary backend, with a separate .NET Worker Service for background operations.

### Consequences

- Strong typing and mature background-service support.
- One primary application ecosystem for business logic.
- Python remains isolated to parsing.
- Domain boundaries must prevent infrastructure leakage.

---

## ADR-002: Keep Python parser-only

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Spreadsheet structures are irregular and Python is suitable for specialized parsing and fixture-driven experimentation.

### Decision

Python will only parse normalized source snapshots and return canonical candidate records, evidence, warnings, metrics, and confidence information.

Python will not manage users, licenses, OAuth, database publication, affected-user resolution, or Google Calendar writes.

### Consequences

- Clear ownership boundaries.
- Parser can be tested deterministically.
- .NET remains authoritative for business state.
- A versioned HTTP contract is required.

---

## ADR-003: Use Google-only authentication

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The product is tightly integrated with Google Calendar and does not require password-based accounts.

### Decision

Registration and login will use Google authentication only.

### Consequences

- No password storage or recovery system.
- Google identity does not imply product activation.
- Local user and onboarding state remain necessary.
- Google Calendar authorization scopes must be managed separately or incrementally as appropriate.

---

## ADR-004: Require administrator-issued license activation

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Access is controlled through codes distributed by administrators.

### Decision

A Google-authenticated user must redeem a valid license code before completing activation and synchronization.

### Consequences

- License redemption must be transactional.
- Codes must be stored as secure hashes.
- Redemption attempts require rate limiting and audit logs.
- Exact expiration, revocation, and reuse policy remains open.

---

## ADR-005: Use canonical schedule records between parsing and calendar sync

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Source spreadsheet formats differ and change. Direct mapping from cells to Google events would tightly couple parsing with synchronization.

### Decision

All parser outputs must be converted into a canonical schedule model and published as versioned revisions before calendar synchronization.

### Consequences

- Parsing, validation, diffing, and synchronization are independently testable.
- Every event can retain provenance.
- Publication and rollback workflows are required.

---

## ADR-006: Use incremental event synchronization

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The old system deleted and recreated a broad two-week event range.

### Decision

Routine synchronization will update only events affected by a published semantic schedule diff.

### Consequences

- Durable user-to-Google-event mapping is required.
- Stable lesson identity and content hashing are required.
- Calendar jobs must be idempotent.
- Full rebuild is reserved for explicit repair operations.

---

## ADR-007: Preserve immutable raw source snapshots

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Parser behavior must be explainable and reproducible.

### Decision

Every changed source acquisition is stored as an immutable snapshot before parsing.

### Consequences

- Historical parser runs can be reproduced.
- Storage use increases.
- Snapshot retention policy may be added later.
- Source evidence can be shown to administrators.

---

## ADR-008: Use parser profiles instead of one universal parser

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The source inventory contains several structurally distinct spreadsheet families.

### Decision

Implement shared parsing primitives and multiple named, versioned parser profiles.

### Consequences

- Changes to one schedule family are less likely to break others.
- Each profile requires fixtures and regression tests.
- Source configuration must map each source to a parser profile.

---

## Open decisions

The following require future ADRs:

- frontend framework and component system
- exact browser session strategy
- managed Google Calendar strategy
- license reuse, expiration, and revocation policy
- background queue implementation
- source polling interval policy
- automatic publication thresholds
- profile schema and supported group combinations
- snapshot retention policy
- whether the canonical record needs a curriculum-block field, which the annual
  sources state and the parser currently keeps only as evidence
- how recurring rows such as `HER HAFTA PAZARTESİ` become dated lessons
- whether holidays and semester breaks become all-day calendar entries

---

## ADR-009: Use a versioned normalized snapshot and parser JSON contract

**Status:** Accepted
**Date:** 2026-07-21

### Context

The inspected spreadsheet families contain Excel date serials, fractional time
values, merged headings, formatting-dependent structure, multiple meaningful
worksheets, and separate lookup tables. Sending only displayed cell text would
lose evidence and make deterministic parsing unreliable.

### Decision

Use an explicit versioned JSON contract between .NET ingestion and the Python
parser.

- JSON properties use camel case and enums use camel-case strings.
- Worksheet coordinates are zero-based and range ends are exclusive.
- Snapshots preserve user-entered, effective typed, formula, formatted, note,
  merge, hidden-dimension, and relevant formatting data.
- Parser responses echo correlation and source identifiers and include canonical
  candidates, evidence, warnings, metrics, and confidence indicators.
- Transport contract versions and parser-profile versions evolve independently.

### Consequences

- C# and Pydantic models must remain contract-compatible.
- Contract serialization requires regression tests.
- Snapshot payloads are larger than value-only grids.
- Acquisition may use sparse cells but must retain structurally meaningful blank
  cells.
- Breaking wire changes require a new contract version.

---

## ADR-010: Model anatomy group independently from general practice group

**Status:** Accepted
**Date:** 2026-07-21

### Context

First- and second-year students have an anatomy assignment that does not follow
their normal practice-group assignment. The confirmed Grade 2 autumn and spring
source lists rotate anatomy groups `1`, `2`, and `3`. The Grade 1 anatomy source
uses the same or a very similar grouping model.

### Decision

Represent anatomy group as an independent student-profile and schedule-audience
dimension with supported values `1`, `2`, and `3` for the confirmed Grade 1 and
Grade 2 model.

Grade 2 anatomy and vertical-corridor sources apply to both Turkish and English
programs. During source joining, `Diseksiyon` identifies anatomy entries in the
annual program, while `Uygulama` identifies vertical-corridor and other practice
entries that require source-specific disambiguation.

### Consequences

- General practice group must never be reused as anatomy group.
- Profile validation must request and validate anatomy group separately.
- Audience selectors need an `anatomyGroup` dimension.
- Annual and special-program sources must enrich the same logical lesson rather
  than publish duplicates.
- `Uygulama` alone is insufficient to identify vertical-corridor lessons; parser
  profile context and joined evidence are required.

---

## ADR-011: Shared normalization primitives never infer missing values

**Status:** Accepted
**Date:** 2026-07-21

### Context

Source cells hold dates as serials or as Turkish and English text, times as day
fractions or as text with inconsistent separators, and group labels in several
shorthand forms. A parser that guesses is convenient in the short term, but a
wrong guess silently invents or misroutes lessons, and the mistake reaches
student calendars without anything to review.

### Decision

Every shared resolver returns an explicit result carrying the rule that produced
it, a confidence score, and a reason code when it did not resolve. Three
behaviours that would otherwise be guesses are opt-in per parser profile:

- reading a numeric cell as a date serial when no number format declares a date
- completing a date written without a year
- reading a bare four-digit block such as `0900` as a time

Additionally:

- a range whose end does not follow its start stays unresolved rather than being
  reordered
- a group expression that is only partly understood resolves to nothing, because
  keeping the understood half would silently drop a cohort
- a digit run longer than two characters is never a group value, so serials,
  years and room numbers cannot be mistaken for cohorts
- a weekday found beside a date is cross-checked and reported, never used to
  correct the date

### Consequences

- Parser profiles must declare their column rules explicitly.
- Unresolved values become warnings, so revision validation sees them.
- More source rows will initially fail to parse than with a permissive parser.
  That is the intended trade: an unparsed row is visible, a wrong row is not.
- Confidence scores are fixed constants per rule so output stays deterministic.

---

## ADR-012: Golden files are the parser regression net

**Status:** Accepted
**Date:** 2026-07-21

### Context

Determinism is a contract requirement, but shared primitives are used by every
profile, so a small change to normalization can silently alter many candidates.

### Decision

Fixture output is compared against committed golden files. Regeneration is
explicit through the `SIRKADIYEN_UPDATE_GOLDEN` environment variable and the
resulting diff must be reviewed and explained. Each golden document records the
fixture, the subject, and the parser engine version.

Determinism is asserted directly by running each producer twice and requiring
identical serialized output.

Until the first fixture-backed profile exists, the golden subject is a
normalization trace over synthetic fixtures. Synthetic fixtures are labelled as
such and never presented as captured faculty data.

### Consequences

- A changed golden file is a reviewable statement that parser output changed.
- Real fixtures require normalized snapshot JSON, which the .NET ingestion layer
  does not yet produce. This blocks fixture-backed profiles.
- The same harness will compare full parse responses once profiles exist.

---

## ADR-013: Inbound wire models accept camel case only

**Status:** Accepted
**Date:** 2026-07-21

### Context

ADR-009 fixed camel case as the JSON convention. Pydantic models that only
accept aliases cannot be constructed from Python by field name, which the parser
needs for the response models it builds itself.

### Decision

Split the Pydantic contract bases. `ContractModel` accepts camel-case aliases
only and is used for the snapshot models and the parse request. The parser
builds outbound models, so `OutboundContractModel` additionally accepts field
names while still serializing camel case.

### Consequences

- A snake-case inbound payload remains a validation failure, not a tolerance.
- The wire format is unchanged in both directions.
- Tests construct snapshot models through the same camel-case validation path a
  real producer would use.

---

## ADR-014: Normalize Google Sheets responses before hashing

**Status:** Accepted
**Date:** 2026-07-21

### Context

Parser profiles require normalized snapshot JSON, but the production acquisition
path did not exist. Google Sheets responses can return sparse and offset grid
segments, repeated cells from overlapping ranges, typed and formatted values,
merge metadata, and hidden dimensions. Hashing the raw response would couple
change detection to API response order and incidental acquisition metadata.

### Decision

The .NET infrastructure layer uses the official Google Sheets v4 client and maps
responses into the v1 normalized snapshot contract before calculating a hash.

- The application port receives snapshot identity and acquisition time from its
  caller; infrastructure does not invent them.
- Worksheets, merges, cells, dimensions, and requested ranges are put in a
  deterministic order.
- Identical repeated cells are deduplicated. Conflicting repeated cells are
  omitted and produce an error diagnostic, so API response order cannot choose
  an arbitrary value and the conflict is never silently accepted.
- Theme colors remain explicit as `theme:{name}` when no resolved RGB value is
  available.
- The lowercase `sha256:` hash covers contract version, normalized worksheets,
  and acquisition diagnostics, but excludes source ID, snapshot ID, and
  acquisition time.
- Authentication is supplied at composition time and must use the read-only
  spreadsheets scope; credentials never enter snapshot mapping.

### Consequences

- Reacquiring identical content produces the same content hash even when API
  collections or acquisition metadata differ.
- Structural, range-scope, cell-content, formatting, and diagnostic changes are
  detectable by the future polling workflow.
- Real snapshot capture still requires source configuration, authenticated
  worker composition, and immutable persistence.
- The local `.xlsx` fixtures remain unable to drive profiles until live snapshots
  are captured or a clearly separated development converter is added.

---

## ADR-015: Separate source transport from document format

**Status:** Accepted
**Date:** 2026-07-21

### Context

The confirmed inventory contains seven Google Sheets sources, Google Drive XLSX
and DOCX files, and a direct HTTP XLSX download. A parser profile should not need
to know how its evidence was transported.

### Decision

Maintain a versioned source catalog that separately records transport, document
format, parser profile, and fixture mapping. Transport adapters acquire bytes or
grid data; format converters produce the normalized snapshot contract. The
local XLSX converter is fixture-only, emits an explicit diagnostic, and trims
formatting-only worksheet tails using a semantic used range.

### Consequences

- New transports and formats can evolve independently.
- Parser profiles consume one stable snapshot contract.
- Drive/HTTP adapters and DOCX conversion remain implementation work.
- Real XLSX fixtures can unblock parser-profile development without presenting
  local conversion as a production acquisition path.

---

## ADR-016: Require an unattended source credential

**Status:** Accepted
**Date:** 2026-07-21

### Context

Worker polling has no interactive browser session. A Google OAuth client ID and
client secret identify the application but do not authorize unattended access.

### Decision

Configure exactly one source-access mode: OAuth with an offline refresh token,
or a service-account credential file. Request only the read-only spreadsheets
scope. Never commit or log credential material, and keep source ingestion
credentials separate from end-user Calendar grants.

### Consequences

- The current environment still needs a source refresh token or service account.
- A service account is preferred when the faculty can share sources with it.
- Revocation and invalid-grant failures must become explicit poll outcomes.

---

## ADR-017: Parse requests carry an explicit source context

**Status:** Accepted
**Date:** 2026-07-22

### Context

A canonical candidate must state its academic year, class year, program language
and interpretation timezone. No workbook states any of them. The Grade 1 Turkish
and Grade 1 English annual workbooks are also served by the same parser profile,
so the profile name cannot carry the language either. The alternatives were to
infer the academic year from the observed date range, infer the language from
the profile name, and infer the class year from a cell, all of which are guesses
that ADR-011 rules out.

### Decision

`ParseSnapshotRequest` carries a required `sourceContext` holding
`academicYear`, `classYear`, `programLanguage` and `timeZoneId`. The values come
from source configuration owned by .NET. The parser uses them directly and never
derives them.

Where a workbook does state something the context also states, the profile
validates rather than infers: a row whose term cell names a different class year
is excluded and counted, and a term cell that states nothing usable is reported
as an anomaly.

### Consequences

- The wire contract changed; the C# record, the Pydantic model and the shared
  contract fixture were updated together and both suites assert on it.
- The source catalog must eventually carry academic year and language per
  source; today they are supplied by the caller.
- One profile can serve several sources without branching on source ID, which
  ADR-008 requires.

---

## ADR-018: Stable identity includes the lesson start time

**Status:** Accepted
**Date:** 2026-07-22

### Context

The annual sources repeat one title several times on one day: free study, a
practice block and a break all recur. Identity built from academic year, class
year, language, date and course identity alone therefore collides, and the
colliding rows are genuinely different lessons. Adding an occurrence index would
make identity depend on row order, which ADR-005 and the identity pattern forbid.

### Decision

The annual stable identity is the ordered hash of academic year, class year,
program language, local date, local start time, and normalized course identity.
Instructor, location, curriculum block, end time and title formatting are
content, not identity, and live in the content hash instead.

A lesson moved to another time therefore changes identity. The semantic diff
engine's deterministic secondary matching, not the identity, is responsible for
recognizing a moved lesson so it is patched rather than deleted and recreated.

### Consequences

- Room, instructor and title reformatting update the existing calendar event.
- Time changes reach the diff engine as an unmatched pair, so Phase 8 must
  implement secondary matching before incremental sync goes live, otherwise a
  rescheduled lesson would be deleted and recreated.
- Course identity ignores a leading list number (`1.`, `1)`, `1-`) because the
  source renumbers those lists whenever the schedule shifts.

---

## ADR-019: Golden files project large parse responses

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-012 made golden files the parser regression net and anticipated comparing
whole parse responses once profiles existed. A real annual workbook produces
around 900 candidates and a 1.5 MB response. Committing every field of every
candidate produces a diff nobody reads, which would defeat the purpose.

### Decision

A parse golden file records:

- one digest line per candidate: candidate ID, date, start and end time, event
  type, truncated identity and content hashes, and a truncated display title
- the complete warnings, metrics and confidence indicators
- the first and last candidate in full, pinning the field shape
- a SHA-256 digest of the entire serialized response

### Consequences

- A moved, retimed, reclassified or reworded lesson is visible in the diff.
- A change to a field no digest line covers still fails, through the response
  digest, and is then investigated locally.
- Golden files stay around 140 KB per fixture instead of several megabytes.
- ADR-012 is amended, not superseded: regeneration remains explicit and every
  changed golden must be explained.

---

## ADR-020: Group expressions state their cohort model

**Status:** Accepted
**Date:** 2026-07-22

### Context

The Grade 1 practice rotation labels cohorts with letters: columns hold `A` to
`H`, `A2` names a subgroup of group A, and `AB` appears where two groups share
a session. The shared resolver was calibrated on numbered cohorts, where a
leading `G` abbreviates the word *grup* and `G2` is group two. Reading the
practice matrix with the numbered model silently turned group G into nothing and
subgroup G2 into group 2, which would have sent lessons to the wrong students.

One letter cannot mean two things at once, and no value inspection can settle
it: `G2` is a valid expression under both models.

### Decision

`parse_group_expression` takes an explicit `letter_groups` flag and the parser
profile states the model its source uses. With lettered cohorts:

- a bare letter is a group, and only the spelled-out words are stripped
- `<letter><digit>` is a subgroup of that letter's group
- a run of letters such as `AB` names the individual groups, scored below a
  value the source spelled out because the reading follows from the cohort model
  rather than from the cell

The practice profile reports a whole group under the `practiceGroup` dimension
and a subgroup under `practiceSubgroup`. A value of any other shape is refused
with its own warning.

### Consequences

- Audience resolution must treat `practiceSubgroup` as a refinement of
  `practiceGroup`: a student in group A with subgroup 2 matches both `A` and
  `A2`.
- A makeup cell that names no group (`TELAFİ`) publishes nothing and warns. That
  is deliberate: sending a makeup session to everyone is worse than omitting it.
- Sources with a third cohort model, such as the English practice program's
  `İ1`-style labels, still refuse and must be reviewed before the resolver is
  widened again.

---

## ADR-021: Persist the schedule pipeline before identity and licensing

**Status:** Accepted
**Date:** 2026-07-22

### Context

The pipeline can now acquire and parse real sources, but nothing survives a
process restart. Persistence is required for polling, change detection,
revisions and diffing. The full product also needs users, licenses, student
profiles and calendar event mappings.

License policy is an explicitly open decision: single-use or multi-use,
expiration, cohort restriction, revocation consequences. Modelling those tables
now would encode guesses in a migration, and an applied migration cannot be
edited.

### Decision

The first schema covers `schedule_sources`, `source_snapshots`, `parse_runs`,
`schedule_revisions` and `canonical_schedule_records`, using EF Core 10 with
Npgsql and version-controlled migrations. Identity, licensing, profiles and
calendar mappings wait for the ADRs that decide their behaviour.

Guarantees the schema enforces rather than the application:

- a source identifier is unique
- one source may have at most one published revision, through a partial unique
  index on the published state
- one revision may not hold the same stable identity twice
- a canonical record must end after it starts
- contested rows carry PostgreSQL's `xmin` as a concurrency token

The unchanged-source short circuit compares only the source's latest snapshot
and runs inside a transaction that locks the source row, so two concurrent polls
cannot both decide the source changed. Snapshot payloads, audience selectors and
parser evidence are stored as `jsonb`.

### Consequences

- Reverted content counts as a change, because only the latest snapshot decides.
  That is correct: the published schedule really did move back.
- Snapshot payloads are large, and the retention policy remains open.
- Identity and licensing migrations will come after their ADRs, not before.
- Database integration tests require a real PostgreSQL and report themselves as
  skipped when none is configured, so an unrun guarantee never looks like a
  passing one.

---

## ADR-022: Licenses are single-use and revocation suspends synchronization

**Status:** Accepted
**Date:** 2026-07-22

### Context

The licensing schema was deferred because reuse, expiration, and revocation
semantics were unresolved. Calendar deletion on revocation would be especially
risky because revocation can be administrative, temporary, or mistaken.

### Decision

Every license code is single-use and may activate at most one user. Redemption
is transactional and idempotent. Revoking the active license stops all future
synchronization for that user but preserves the user's dedicated Sirkadiyen
calendar and its existing events. Revocation never starts event deletion.

### Consequences

- The database needs a uniqueness guarantee preventing two redemptions.
- License status, redemption, revocation, and any later reactivation are audited.
- Sync job admission checks the authoritative license state server-side.
- Existing events may become stale after revocation; the UI must state that the
  calendar is no longer synchronized.

---

## ADR-023: Web sessions use backend-managed secure cookies

**Status:** Accepted
**Date:** 2026-07-22

### Context

Google sign-in establishes identity, but the browser still needs an application
session. Exposing bearer or Google refresh tokens to JavaScript would increase
the impact of XSS and complicate revocation.

### Decision

Use a backend-managed HTTP-only secure cookie for the web session. Configure an
explicit SameSite policy, expiry and rotation. State-changing endpoints require
anti-forgery protection. Authorization for admin role, license, profile, and
sync eligibility is always enforced by the backend.

### Consequences

- The frontend never receives Google refresh tokens or an application bearer
  token for normal browser use.
- Cross-origin deployment and callback URLs must be designed around cookie and
  SameSite rules.
- CSRF protection is a mandatory part of authentication implementation.

---

## ADR-024: Create one dedicated Sirkadiyen calendar per user

**Status:** Accepted
**Date:** 2026-07-22

### Context

Mixing managed events into a user's primary calendar makes ownership, repair,
and safe cleanup ambiguous. Asking users to select an existing calendar also
creates inconsistent deployment and support paths.

### Decision

Create one dedicated Sirkadiyen Google calendar for every activated user and
write all managed events to it. Persist its Google calendar ID and every event
mapping. Mark managed events with private extended properties. If the calendar
is deleted or inaccessible, stop normal sync and require an explicit audited
repair or recreation flow.

### Consequences

- Initial sync includes an idempotent calendar-creation step.
- Event insert, patch, delete, and reconciliation never target the primary
  calendar.
- License revocation preserves the dedicated calendar as required by ADR-022.

---

## ADR-025: Quarantine destructive or structurally unknown revisions

**Status:** Accepted
**Date:** 2026-07-22

### Context

A technically successful parse may still represent a broken or radically
changed source. Automatic publication would convert a source mistake into mass
calendar mutations.

### Decision

Move a revision and its semantic diff to `ReviewRequired` when any of these
conditions is true:

- more than 20 percent of the previously published records disappear
- an unknown group selector appears, such as a new `İ4` value not present in
  the supported profile schema
- multiple impossible overlaps occur for the same audience at the same local
  date and time

Manual approval is required before publication. Until approval, no affected-user
resolution or calendar deletion job may be created.

### Consequences

- Validation needs the prior published revision and the supported profile schema.
- Diff records need explicit anomaly evidence and review state.
- The 20 percent boundary means exactly 20 percent does not trigger this rule;
  any value greater than 20 percent does.

---

## ADR-026: Select polling intervals from an Istanbul-time policy

**Status:** Accepted
**Date:** 2026-07-22

### Context

Source change probability varies by day and hour. A single aggressive interval
wastes quota at night and weekends, while a single relaxed interval delays
weekday changes.

### Decision

Use a validated, configurable interval policy evaluated in `Europe/Istanbul`.
The initial schedule is 60 minutes on weekends; on weekdays, 45 minutes from
00:00-07:00 and 21:00-24:00, 15 minutes from 07:00-16:00, and 25 minutes from
16:00-21:00. Prevent overlapping poll cycles.

### Consequences

- Tests cover every boundary and weekend precedence.
- Operational changes alter configuration without changing polling code.
- A single worker cycle records per-source outcomes and schedules the next cycle
  only after the current cycle finishes.

---

## ADR-027: Store variable student group selectors as validated JSONB

**Status:** Accepted
**Date:** 2026-07-22

### Context

Practice cohorts (`İ1`, `İ2`, `İ3`), anatomy groups, third-year curriculum
groups, rotations, and future electives do not share one fixed set of columns.
An unconstrained EAV model would be flexible but would make validation, querying,
and referential rules harder to understand.

### Decision

Keep academic year, class year, and program language as relational fields. Store
the remaining group choices in a schema-versioned JSONB selector document. A
server-owned supported-profile schema defines valid dimensions, values, and
dependencies. Reject unknown or incompatible client values. Reuse the same
selector semantics for canonical audiences and affected-user resolution.

### Consequences

- New group dimensions can be introduced without adding nullable columns.
- JSONB documents and their schema versions must be migrated deliberately when
  selector semantics change.
- Intentional JSON indexes may be added only after real audience queries are
  measured; JSONB is not a substitute for core relational fields.

---

## ADR-028: Retry parser transport failures on one logical parse run

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-021 permits one parser-profile version to run once per immutable snapshot.
If the HTTP call fails after a changed snapshot is stored, the next acquisition
is unchanged. Stopping at the content short circuit would strand that snapshot
forever, while inserting another parse run would violate deterministic identity
and allow duplicate revisions.

### Decision

Keep one logical `ParseRun` for each snapshot/profile/version and count transport
attempts on it. A failed run may resume with a new correlation ID and incremented
`AttemptCount`. When acquisition is unchanged, retry against the normalized
snapshot document already stored as immutable evidence, not the newly acquired
but intentionally unstored duplicate. Completed, warning, and rejected runs do
not execute again.

### Consequences

- Transient parser transport failure no longer requires a changed source to
  recover.
- The unique parse-run index remains the concurrency and idempotency guard.
- The current row retains the latest failure and total attempt count rather than
  a full attempt history; structured logs must retain per-attempt diagnostics.
- Recovery of a run left `Running` by abrupt process termination remains a
  separate worker-maintenance task.

---

## ADR-029: Revision validation quarantines rather than rejects

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-025 named the anomalies that must stop publication but not the thresholds,
the severity model, or where validation runs. Implementation forced all three.
The accepted product direction is automated publication with safety nets, so the
nets carry the whole burden of preventing mass calendar damage.

### Decision

Validation is a pure function over a candidate revision, its source, and the
stable identities of the last published revision. It runs in its own transaction
after parse completion and moves the revision `Parsed → Validating → outcome`.

Severity determines the outcome:

- any `Error` finding moves the revision to `ReviewRequired`
- an empty revision is `Rejected`, the only terminal outcome
- otherwise the revision is `Validated`

`Rejected` is reserved for a revision no approval could rescue, because rejection
is unrecoverable. Everything a human could reasonably approve stays approvable.

The deletion rule requires **both** conditions to hold:

```text
deletedCount > previouslyPublishedCount * 0.20
  AND deletedCount >= 10
```

The share alone is meaningless on a small source, and the absolute count alone is
noisy on a large one. Exactly 20 percent does not trigger the rule (ADR-025).

Thresholds live in `RevisionValidationOptions` and are configurable per
deployment. Validation may never produce `Published`; the store rejects the
attempt.

### Consequences

- A source with no published revision cannot trip the deletion rule.
- One audience overlap is a warning; more than one quarantines the revision, per
  ADR-025's wording of "multiple" impossible overlaps.
- Overlap detection compares exact selector sets, so a group and its own subgroup
  do not register as overlapping. Detecting that needs the ADR-027 profile schema.
- The academic-year range is derived as 1 August to 31 July from a `YYYY-YYYY`
  label, widened by a configurable grace period. An unreadable label produces a
  warning rather than silently skipping the rule.
- The duplicate-stable-identity rule cannot fire through the normal pipeline,
  because the schema's unique index on `(ScheduleRevisionId, StableIdentity)`
  rejects the insert first. It is retained as defence in depth.
- Publication remains unimplemented and is the next step.

---

## ADR-030: Problem-based learning is out of scope

**Status:** Accepted
**Date:** 2026-07-22

### Context

The Grade 1 Turkish practice source states PDÖ sessions using a different group
partition from the rest of the workbook: `D3`, `F4`, `G4`, `H3` alongside the
regular `A1`/`A2` halves. The parser filed both partitions under the single
`practiceSubgroup` dimension, so a student profile holding one subgroup value
could not express which PDÖ group the student belongs to. Published PDÖ lessons
would therefore have reached the wrong students.

### Decision

Exclude PDÖ from the system entirely. It runs twice a year and its groups are
arranged between students out of band, so it is not schedule data Sirkadiyen can
own. `grade1_practice_v1` detects out-of-scope subject columns by whole-word key
and publishes none of their cells.

Exclusion is not silent. Each out-of-scope column produces an informational
warning with evidence, and every populated cell in it is counted through
`cells.ignored.outOfScopeSubject`, as AI_GUIDELINE section 9 requires.

### Consequences

- The Grade 1 Turkish practice source yields 402 candidates rather than 426.
- The declared cohorts collapse to a clean `A`-`H` with `1`/`2` subgroups, which
  is what `supportedAudienceSelectors` now states.
- This is a product decision, not a parser gap; it is not tracked as future work.
- A source that legitimately needs a finer partition later must introduce its own
  audience dimension rather than reusing `practiceSubgroup`.

---

## ADR-031: A student sees both subgroups of their practice group

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** not yet; recorded ahead of calendar synchronization

### Context

Vertical-corridor practices split a group into subgroups such as `A1` and `A2`.
Students frequently do not know which subgroup they are in at the start of the
academic year, so requiring the subgroup during onboarding would block or
mis-assign a large share of users.

### Decision

The parser continues to resolve and publish `A1`, `A2` and their siblings as
distinct audience selectors; canonical data stays precise.

At synchronization time, a student who has stated only their practice group
receives the events of **every** subgroup of that group, with the subgroup
prefixed in the event title:

```text
[A1] İletişim Becerileri
```

### Consequences

- Precision stays in the canonical model; the widening is a synchronization-time
  policy, so it can be narrowed later without reparsing anything.
- A student who does state their subgroup should receive only that subgroup's
  events. Affected-user resolution must handle both cases.
- Event titles become audience-dependent, so the title cannot be part of a
  content hash shared across users, or every user would appear to disagree.
- Duplicate-looking calendar entries are expected and intended; the prefix is
  what makes them readable.

---

## ADR-032: Healthy revisions publish themselves; held ones need a named approver

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** transactional publication, the approval audit trail, the internal
administration API

### Context

Validation leaves a revision in `Validated` or `ReviewRequired` (ADR-029) but
cannot publish either. Something had to decide when a revision becomes live, and
who is allowed to override a revision validation refused to clear. There is no
identity provider and no administration frontend, and both are deferred.

### Decision

A revision that reaches `Validated` is published without human intervention. The
transition to `Published` and the previous revision's transition to `Superseded`
commit in one transaction.

A `ReviewRequired` revision reaches publication only through approval, which
requires a named approver and a stated reason. Both are stored on the revision as
`ApprovedBy` and `ApprovalReason`, alongside `ApprovedAtUtc`. Approval moves the
revision to `Validated` and stops there, so an approved revision publishes
through exactly the same transaction as one that was never held.

While there is no frontend, approval runs over an internal API guarded by a
required shared key, `SIRKADIYEN_ADMIN__API_KEY`. The API refuses to start
without one.

Publication is driven by revision state rather than by the poll that produced the
revision. The worker publishes every `Validated` revision at the end of each
cycle.

### Consequences

- A crash between validation and publication costs nothing: the next cycle
  publishes from state. The same path recovers a revision approved through the
  API when that request failed after approving it.
- Superseding and publishing are two `SaveChanges` calls inside one transaction.
  The "one published revision per source" rule is a partial unique index, which
  cannot be deferred, so the outgoing revision must vacate the slot first.
- Publishing an older revision over a newer live one is refused. It would move a
  source's schedule backwards, which the semantic diff would read as a mass
  deletion.
- `ApprovedBy` is a claim, not a verified identity. The key proves the caller is
  an operator, not which operator. This is acceptable only until real
  authentication exists, and the field is designed to keep working unchanged when
  it does.
- Anyone holding the administrative key can push a quarantined schedule into
  student calendars. The API must not be publicly exposed while the key is the
  only gate.
- Publishing overwrites `StateReason`, so the approval reason survives in its own
  column rather than in the state text. The findings that held the revision are
  never deleted either way.

---

## ADR-033: Correct published schedules by forward-fix, never rollback

**Status:** Accepted
**Date:** 2026-07-22

### Context

A revision can be superseded by a newer revision, but restoring an older
revision would introduce a second publication path, complicate audit history,
and risk replaying schedule data that is no longer correct at its source.

### Decision

Sirkadiyen does not provide a rollback operation for published revisions. An
incorrect schedule is corrected in its authoritative source, such as Google
Sheets. Normal polling then acquires the correction, creates and validates a new
revision, publishes it, calculates the forward semantic diff, and updates the
affected calendars.

### Consequences

- A superseded revision remains immutable evidence and can never become live
  again.
- Operators repair the source of truth rather than application state.
- The UI and runbooks use the term `forward-fix`, not rollback.
- The absence of rollback makes an operational freeze and rapid source repair
  essential.

---

## ADR-034: A global freeze stops ingestion and publication

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** core persistence and pipeline gates; authenticated remote write
surface remains deferred

### Context

A source may change structure without warning or produce a large anomaly. The
normal validation barriers prevent suspicious publication, but operators also
need one global switch that stops new evidence and live-state changes while the
incident is understood.

### Decision

Add a dynamically readable global operational freeze. While it is enabled:

- no source acquisition starts
- no parse run starts or resumes
- no revision is published, including an approved or already validated one
- no semantic diff dispatch or calendar mutation starts

An operation already performing an external read may finish storing immutable
evidence, but it may not cross the publication boundary. Unfreezing resumes the
ordinary state-driven pipeline; it never skips validation and never creates a
special publication path. Freeze/unfreeze changes are audited with actor,
reason, timestamp and correlation ID.

A startup-only environment variable is insufficient because changing it needs a
restart. The implementation must use an authoritative runtime-readable control
and fail closed when its state cannot be read. The administration surface is
added after real operator authentication exists.

### Consequences

- The worker checks the freeze before every source and immediately before
  publication and downstream dispatch.
- The API exposes freeze state read-only until authenticated administration is
  implemented.
- Freeze preserves queued and validated work; it does not reject or delete it.
- Calendar jobs must check the same control so a queue backlog cannot bypass the
  switch.

---

## ADR-035: Secondary matching uses title, instructor and academic department

**Status:** Accepted, amended 2026-07-23 (see the amendment at the end of this
entry; the department is no longer a precondition)
**Date:** 2026-07-22
**Implements:** semantic diff model, pure matching engine, persistence and
post-publication orchestration

### Context

Start time is part of stable identity (ADR-018). A lesson moved to another time
therefore appears as an unmatched old and new record. Treating that pair as a
delete and create would lose the durable Google event mapping even when the
lesson itself did not change.

### Decision

After exact stable-identity matching, compare only structurally compatible
unmatched records: same source, academic context, local date, event type,
record status, audience and timezone.

Secondary matching requires all three explicitly sourced attributes on both
records:

1. normalized lesson title
2. instructor
3. academic department

Use deterministic normalized Levenshtein similarity. Initial thresholds and
weights are:

```text
lesson title       minimum 0.82, weight 0.50
instructor         minimum 0.85, weight 0.30
department         minimum 0.90, weight 0.20
composite score    minimum 0.88
```

Every component must cross its own threshold as well as the weighted composite.
If either side lacks any of the three fields, it is not eligible for secondary
matching; nothing is inferred from evidence. `Department` is therefore an
optional canonical field populated only by parser profiles whose source states
it.

A pair is accepted only when it is the sole eligible candidate for both the old
and new record. Any one-to-many or many-to-one candidate set is `Ambiguous` and
must never become delete-and-create automatically.

### Consequences

- Minor spelling, punctuation and Turkish-diacritic differences may still match.
- Similar lessons in the same slot stay quarantined when the data cannot choose
  uniquely.
- Scores and their three components are retained as evidence.
- Threshold changes are behavioral changes and require regression tests and an
  ADR amendment.
- Existing canonical records have null `Department` after the additive
  migration and remain safely ineligible until a future parsed revision states
  it.

### Amendment 2026-07-23: the department is evidence, not a precondition

**Status:** Accepted
**Supersedes:** the requirement above that all three attributes be present

The original rule was written expecting the department to be present on most
records. Fixture evidence says otherwise. Of the candidates the two committed
Grade 1 annual workbooks publish, 419 of 901 Turkish and 417 of 953 English name
a department at all — under half. Eleven candidates per source name *several*,
because an integrated session is taught by two to four departments, and a list is
not a comparable value: two integrated sessions that share one department out of
three are not the same lesson, and comparing the joined text would score them as
nearly identical.

Requiring the attribute therefore meant that most lessons whose time moved would
reach the calendar as a delete followed by a create — exactly the outcome this
ADR exists to prevent — and, through ADR-040, would hold the diff on the deletion
count.

The rule is now two-tier. Structural compatibility, the per-attribute title and
instructor minima, and the uniqueness requirement are unchanged.

1. **With a department.** When both records name exactly one department, score all
   three attributes against the thresholds above.
2. **Without a comparable department.** When either record names none, or names
   several, score title and instructor with their weights renormalized to sum to
   one, and require a composite of at least `0.94`.

A pair where both records name exactly one department but disagree about it is
*not* re-scored by the second rule. It was already offered to the stronger rule
and refused, and re-scoring it without the attribute that disagreed would turn a
rejection into a match.

`DepartmentScore` on the diff entry is null exactly when the second rule was
used, so a consumer and an operator reviewing a held diff can tell the weaker
basis from the stronger one. The composite threshold without a department is
configuration-validated to be at least the one with a department, so no operator
can make weaker evidence easier to satisfy.

**Consequences**

- Every lesson with a stable title and instructor survives a time change as an
  `Updated`, whether or not its source names a department.
- The weaker rule demands near-identical title and instructor: with an identical
  instructor, a title similarity below about `0.90` is refused.
- The department still strengthens a match and still blocks one when two single
  departments disagree.
- An integrated session is matched without its department list, and the list is
  kept for display (ADR-049).

---

## ADR-036: Use Next.js for the frontend

**Status:** Accepted
**Date:** 2026-07-22

### Context

The frontend stack was intentionally deferred while the schedule pipeline was
being established.

### Decision

Use Next.js with React and TypeScript. The browser consumes typed backend
contracts and uses the backend-managed secure-cookie session from ADR-023.

### Consequences

- Authorization, activation and synchronization truth stay server-side.
- A component system remains a later, narrower UI decision.
- Deployment includes a separate frontend container unless a later ADR changes
  the topology.

---

## ADR-037: Use Hangfire for durable background jobs

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** not yet

### Context

Calendar synchronization, affected-user resolution, reconciliation and
maintenance need durable retryable jobs. The technology baseline named Hangfire
as the initial preference but had not accepted it.

### Decision

Use Hangfire for durable background jobs, initially with PostgreSQL-backed
storage. Domain state and required job dispatch remain connected through the
transactional outbox; application logic does not call Hangfire directly.

### Consequences

- Jobs remain idempotent because queue delivery is at least once.
- Hangfire retries do not replace domain failure classification.
- Redis may still serve locks, caching and rate limiting, but is not the
  authoritative durable job store.
- The global freeze from ADR-034 gates Hangfire calendar and publication jobs.

---

## ADR-038: Recurring undated rows are out of scope

**Status:** Accepted
**Date:** 2026-07-22

### Context

Rows such as `HER HAFTA PAZARTESİ` name a recurrence but do not state the
individual dates or an unambiguous recurrence boundary. Expanding them would
invent schedule instances the source did not explicitly provide.

### Decision

Do not publish recurring undated rows. Parser profiles detect and account for
them with evidence, informational warnings and ignored-row metrics. They are not
future product work unless a source begins stating explicit dates or a later ADR
introduces a safe recurrence contract.

### Consequences

- The six currently known rows stay out of student calendars.
- Exclusion is visible and deterministic, never a silent discard.
- This decision does not apply to explicitly dated repeated lessons.

---

## ADR-039: Calculate and store the semantic diff after publication, not inside it

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

Publication makes a revision live in one transaction that also supersedes the
revision it replaces. The semantic diff describes what that publication changed
and is the only authority for a later calendar deletion. It could be calculated
inside the publication transaction, or afterwards as a separate step.

### Decision

Calculate and store the diff in a separate transaction after publication, driven
by revision state rather than by the caller that published: a revision that
reached `Published` or `Superseded` and has no diff row is diffed on the next
worker cycle. Exactly one diff per revision is enforced by a unique index on the
current revision, so a retried calculation reports the existing diff rather than
writing a second one.

### Consequences

- A revision is live the moment publication commits. A diff that fails to
  calculate cannot take back a schedule students are already entitled to see.
- A worker killed between the two steps recovers without operator action.
- A revision that was published and superseded before its diff ran is still
  diffed, so nothing it changed is lost.
- The diff is stored evidence and is never recalculated. Correcting a bad
  publication remains forward-fix only (ADR-033).

---

## ADR-040: Hold a diff at dispatch on ambiguity or mass deletion

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

Revision validation already applies a mass-deletion rule before publication, but
it compares stable-identity sets. It cannot know that a lesson whose time
changed will be recovered by secondary matching (ADR-035), nor that a candidate
set will refuse to resolve and stay `Ambiguous`. The number that decides how
many calendar events would actually be deleted only exists after the diff runs.

### Decision

A stored diff is created in `Ready` or `Held`. It is held when it contains any
`Ambiguous` entry, or when its deletions reach the configured minimum count and
exceed the configured share of the previous revision. A held diff may not be
turned into any calendar operation. The reason is stored in full on the diff.

### Consequences

- A single ambiguous pair holds the whole diff: acting on the rest would delete
  the previous record of that pair from a student's calendar.
- The minimum deletion count keeps a small source from being held on ordinary
  editing, which would train operators to approve without reading.
- This gate does not replace the validation rule; both run, at different stages,
  on different evidence.
- Releasing a held diff requires an operator path that does not exist yet. Until
  it does, a held diff simply stops there, which is the safe direction.

---

## ADR-041: Load the repository `.env` into the process environment at startup

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

`dotnet run --project src/Sirkadiyen.Api` failed with
`Required configuration 'SIRKADIYEN_DATABASE:CONNECTION_STRING' is missing`
even though the value was in the repository's `.env`. Nothing read that file:
`.env` was only ever consumed by Docker Compose, so a developer had to export
every variable by hand in each shell, and the EF Core design-time factory fell
back to a hard-coded local host instead.

Adding a package such as `DotNetEnv` would have solved it, but the parser is
about eighty lines, the behaviour we need is specific (existing variables must
win) and a dependency in the dependency-free Infrastructure composition path
buys nothing here.

### Decision

`Sirkadiyen.Infrastructure.Configuration.DotEnvFile` searches upward from the
assembly's output directory for the nearest `.env`, parses it, and writes each
declaration into the process environment **only when that variable is not
already set**. Both hosts call it before creating their builder, because the
environment-variable configuration provider reads the environment as it is
added. The design-time context factory and the PostgreSQL test fixture call it
too, so migrations and integration tests need no manual export either.

It is a development convenience, not a configuration source: deployed
environments inject real variables and ship no file, so the call is a no-op
there. A malformed line raises `InvalidDataException` naming the file and line
number, never the value.

### Consequences

- `dotnet run`, `dotnet-ef` and `dotnet test` work from a clean shell.
- An exported or container-injected variable stays authoritative, so a stale
  file cannot quietly redirect a host at the wrong database.
- The loader mutates process-global state. That is why it runs once, at the top
  of a host's composition, and never from library code reached at request time.
- Integration tests now run whenever `.env` declares
  `SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING`. That database is dropped and
  re-migrated on every run, so the file must never point it at a working one.
- There is deliberately no inline comment syntax: a password may contain `#`,
  and silently truncating a secret at one would be very hard to diagnose.

---

## ADR-042: Let an operator release a held diff, except an ambiguous one

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented
**Amends:** ADR-040

### Context

ADR-040 holds a diff on ambiguity or mass deletion and lets nothing act on it.
It left the release path unspecified, so a hold could only be cleared by
correcting the source and waiting for a newer revision to supersede the held
one. That is right when the hold reveals a parse fault and wrong when the source
really did drop a hundred lessons at the end of a semester: the schedule is
correct, nobody can say so, and every later cycle holds again.

ADR-040 also stated that a stored diff never mutates. That consequence is
superseded here, for the state and the release audit fields only. What the diff
says about the two revisions is still immutable and never recalculated.

### Decision

A `Held` diff gains a third state, `Released`, reached through
`POST /api/diffs/{id}/release` behind the existing operator key. The release
records `ReleasedBy`, `ReleaseReason` and `ReleasedAtUtc`, and keeps the hold
reason. `IsDispatchable` becomes true for `Ready` or `Released`.

A hold caused by ambiguity is **not** releasable, in the domain and not only in
the endpoint. The release is guarded by the diff's row version.

### Consequences

- A legitimate large deletion now has a way forward that is recorded, instead of
  depending on the source being edited into a shape the gate tolerates.
- An ambiguity can only be resolved at the source. An operator cannot decide
  which of several candidates a record became, and waving it through would leave
  the previous lesson in every affected calendar while its replacement is never
  written.
- `Released` is deliberately a separate state from `Ready`. A consumer, and
  anyone reading the table later, can tell an automatically safe diff from one a
  human vouched for.
- Two operators releasing at once get a refusal rather than a silent overwrite:
  the second must read the current state before vouching for it.
- `ReleasedBy` is a claimed identity, like `ApprovedBy` (ADR-032). It becomes
  verifiable only when real authentication replaces the shared key.
- Nothing dispatches diffs yet, so releasing one currently changes only its
  state. The value arrives with the calendar adapter.

---

## ADR-043: Persist the global freeze as a singleton with append-only transitions

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** ADR-034 core persistence and current pipeline gates

### Context

ADR-034 requires one runtime-readable switch, fail-closed reads, and an audit
trail for both freeze and unfreeze. A startup setting cannot change without a
restart, while a queue-local flag could disagree between the API, worker and
future Hangfire processes. Updating only one mutable row would retain the latest
reason but erase the incident history.

### Decision

Store the authoritative current state in exactly one PostgreSQL row,
`operational_freeze_control`, and append every actual transition to
`operational_freeze_audits`. A transition locks the singleton row, changes it,
and appends actor, reason, UTC timestamp and correlation ID in one transaction.
Repeating the current state is idempotent and creates no audit row.

Every pipeline boundary reads the state through `IOperationalFreezeStore` at
runtime. A missing row or database read failure throws and the mutation does not
start. The worker checks before every source; the source poller checks
immediately before acquisition and after immutable evidence storage; publication
checks before every revision.

The existing operator-key API exposes only `GET /api/operations/freeze`.
Freeze/unfreeze mutation is implemented behind the application port but receives
no remote endpoint until real operator authentication exists. Future semantic
diff dispatchers and Calendar jobs must use the same port.

### Consequences

- Every host and future queue consumer observes one durable state without a
  restart.
- A freeze enabled during an external source read permits its immutable snapshot
  to be stored but blocks the parse run that would follow.
- Work remains queued or validated and resumes through the ordinary state
  machine after unfreeze.
- Direct SQL changes are not a supported operational path because they would
  bypass the append-only audit.
- The authenticated write surface remains explicit follow-up work; the shared
  key is not extended with another safety-critical mutation.

---

## ADR-044: Retain the active-year anchor, latest source, and ten recent days

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** snapshot payload retention and worker maintenance
**Amends:** ADR-007

### Context

Changed polls store normalized snapshot documents that can be several megabytes.
The faculty schedule is continuously corrected, so keeping every old payload
online forever has little operational value. Operators need the main schedule as
the academic year began and a short recent history to answer "what changed?".

Deleting complete pipeline lineages is unsafe before diff dispatch has a durable
completion marker: parse runs, revisions, canonical records and diffs are still
audit and future synchronization evidence. The large snapshot JSON is the part
that drives unbounded storage.

### Decision

For each source, retain the normalized payload of:

- the first snapshot captured under the source's currently configured academic
  year
- the latest snapshot, even after a long quiet period
- every changed snapshot acquired in the last ten days
- every snapshot with no parse run or with a `Running` or `Failed` parse run

Snapshots are stored only when content changes, so the ten-day window contains
every change rather than one artificial copy per calendar day.

After the window, a maintenance batch removes only eligible normalized payloads.
The snapshot row, source and external identifiers, academic year at acquisition,
content hash, counts, acquisition time and prune time remain. Parse responses,
revisions, canonical records and semantic diffs are not deleted. The payload is
never overwritten with different evidence; it is either retained exactly or
explicitly pruned.

The maintenance boundary reads the global operational freeze and does nothing
while frozen. The initial window is ten days and the batch size is operational
configuration.

### Consequences

- Storage is bounded primarily by one annual anchor, one latest payload and the
  changed snapshots from the recent window per source.
- A parser-profile version can still reparse the latest unchanged source.
- Abrupt-shutdown and transport-failure recovery never loses its input.
- Historical metadata and the "what changed?" diff record remain queryable after
  raw JSON is pruned.
- Whole-lineage retention waits until diff dispatch records durable completion;
  this ADR does not silently delete an undispatched calendar change.

---

## ADR-045: Bootstrap administration with one Google SuperAdmin identity

**Status:** Accepted, amended 2026-07-23 with the address and the role column
**Date:** 2026-07-23
**Implements:** not yet; blocked only on Google sign-in existing
**Amends:** ADR-032, ADR-034 and ADR-042 operator identity follow-up

### Context

The first release has one operator. A general role-management UI and multi-role
RBAC model would add work that has no current user. The shared administrative
key is not an acceptable identity once Calendar dispatch exists.

### Decision

After Google sign-in is implemented, the backend will recognize exactly one
normalized, Google-verified email address as `SuperAdmin`. The address is a
backend-owned literal bootstrap allowlist, not a role claimed by the browser.
No role editor, delegated administration or general RBAC schema is added in the
first release.

Every approval, held-diff release and freeze/unfreeze transition derives its
actor from that authenticated Google identity. The existing shared key is then
removed.

### Consequences

- The first administration surface stays small while authorization remains
  server-side.
- The exact Google email must be supplied before implementation; it is not
  inferred from Git configuration, a service account or a client request.
- Adding a second operator or another role is a later explicit decision and data
  migration, not a comma-separated client setting.

### Amendment 2026-07-23: the address, and the users table carries a role

**Status:** Accepted

The bootstrap address is `halil.semih.sen@gmail.com`, normalized and required to
be Google-verified. It is a backend-owned literal, not configuration a client can
influence.

This is explicitly not a permanently single-operator system. The `users` table
will carry an explicit `role` column from the moment it is created, so the
bootstrap allowlist grants that role rather than standing in for one. The literal
is then the seed for the first row, not the authorization mechanism, and a second
operator becomes a data change instead of a code change.

What stays deferred is only the role *editor* and general RBAC: no delegated
administration, no permission matrix, no role-management UI in the first release.

**Consequences**

- `ApprovedBy`, `ReleasedBy` and every freeze transition can record a verified
  identity as soon as sign-in exists; until then they remain claims guarded by
  the shared key.
- The identity work is no longer blocked on a decision. It is blocked on Google
  sign-in, which is Phase 2 work.

---

## ADR-046: Publish holidays and semester breaks as all-day events

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** parser contract, both annual profiles, canonical domain model,
persistence, revision validation and semantic diff, 2026-07-23. See the
implementation note below for the four questions the sources answered.

### Context

Twenty-two annual-program rows state holidays or semester breaks with dates but
no times. Inventing a start or end time would violate ADR-011 and omitting them
leaves student calendars knowingly incomplete.

### Decision

The canonical schedule model will represent a schedule item as either timed or
all-day. A holiday or semester-break row with an explicit date becomes an
all-day canonical event; no synthetic time is assigned. Multi-day closures use
an inclusive local start date and an exclusive local end date when sent to
Google Calendar.

### Consequences

- Canonical contracts, persistence, validation, stable/content hashing and
  Calendar mapping must support all-day items before these rows publish.
- All-day/timed shape changes are content changes and remain deterministic.
- A row without an explicit date still does not publish.

### Implementation note 2026-07-23

Implemented through the parser contract, both annual profiles, the canonical
domain model, persistence, revision validation and the semantic diff. 22 Turkish
and 11 English rows now publish; `rows.ignored.noScheduledTimeAndNoClosure` is
zero for both sources, so every untimed dated row they contain is accounted for
as a closure.

Four decisions the ADR left open were settled by reading the sources:

1. **No span field.** The ADR anticipated an inclusive start and exclusive end
   date. The sources do not state a range: a closure is written as one row per
   closed day, and the ten `YARIYIL TATİL` rows skip 31 January and 1 February
   because the weekend is not a teaching day. A stored span would therefore have
   to either invent those two days or be populated from nothing. An all-day
   canonical record covers exactly one local date, and the exclusive end date
   Google Calendar wants is `LocalDate + 1`, computed by the calendar adapter.
   Consecutive rows are deliberately not merged: merging is an inference, and it
   would cover days the source excluded.
2. **The shape decides, not the title.** A row becomes all-day only when it states
   a date, a title naming a closure, and *no times at all*. The title alone is not
   enough, and the sources prove why: `CUMHURİYET BAYRAMI AREFESİ` is a timed
   three-hour session, and the English workbook writes its own semester break as
   eleven timed 08:30–16:20 rows. Those are published as the source states them.
   A dated row with no times whose title names no closure stays unpublished with a
   warning naming the cell, because a lesson whose times the faculty forgot must
   not become an all-day block on every student's calendar.
3. **The closure vocabulary is `tatil`, `bayram`, `holiday` and the phrase
   `labor day`.** The first three are matched as whole-word prefixes, which covers
   `TATİL`/`TATİLİ` and `BAYRAM`/`BAYRAMI`. `LABOR DAY` states no closure word at
   all; it is included because the English workbook puts it on 1 May, the same
   date the Turkish workbook calls `İŞÇİ BAYRAMI`, so the two sources identify it
   between them. Any other wording is refused and reported, and the list grows
   from that evidence rather than from knowing the Turkish calendar.
4. **A closure is `other`, decided by shape.** Its event type follows from being
   all-day rather than from its keywords, so the keyword classifier is not widened
   and no timed lesson is reclassified by this change.

Consequences that fell out of the implementation:

- `IsAllDay` and the two nullable times are one invariant, enforced in the Pydantic
  contract, the domain constructor and a database check constraint. Every branch of
  the constraint tests nullness explicitly, because a check constraint passes on
  NULL and a bare `"EndLocalTime" > "StartLocalTime"` would have let a timed record
  with no times through the one gate meant to catch it.
- Revision validation excludes all-day records from the lesson-duration and
  overlap rules. Measuring a closure as zero minutes would have quarantined every
  revision that published one, and treating it as booking its whole date would
  have reported the teaching around a half-day holiday as a clash.
- Secondary matching cannot pair an all-day record with a timed one: the shape
  joins the structural context, and matching also demands an instructor, which no
  closure states. A closure that gains times is a delete and a create, which is
  correct — the calendar cannot patch an all-day event into a timed one.
- The identity component that normally carries the start time holds the literal
  `allDay` for a closure, because an identity component may not be empty. Timed
  identities are byte-identical to before.
- Every content hash moved again, because `isAllDay` is now part of it for both
  profiles. Adding the key only to all-day records would have avoided the churn but
  would have made the hash schema depend on the record's shape, which is how a
  field silently stops being covered. Taken now for the same reason as ADR-047: no
  student calendar exists yet.
- The migration's `Down` refuses while all-day records exist. The old schema cannot
  represent one: making the time columns required again would have to invent
  midnight, and deleting the rows would discard published schedule data a diff may
  cite. The guard raises a message naming the count instead.
- Known asymmetry, left as the sources state it: the Turkish source publishes the
  semester break as ten all-day items and the English source publishes the same
  break as eleven timed lessons labelled `theory`. Correcting that belongs to the
  source or to a separate event-type decision, not to this one.

---

## ADR-047: Curriculum block is canonical when the source states it

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** parser contract, canonical domain model, persistence and both
implemented Grade 1 profiles, 2026-07-23. The annual profiles read it from the
same cell as the department, so it shipped together with ADR-049.

### Context

Annual and practice sources name curriculum blocks ("dilim"), such as `Hücre`,
`Doku`, `Solunum` and `Dolaşım`. The parser currently keeps that value only in
evidence and the content hash, so downstream product code cannot query or show
it as a first-class schedule attribute.

### Decision

Add nullable `CurriculumBlock` to the canonical parser contract, domain model
and persistence. Populate it only when the source explicitly states the block.
It is lesson content and provenance, not an audience selector and not inferred
from the course title.

### Consequences

- A corrected curriculum-block label updates the existing logical lesson rather
  than changing its stable identity.
- Profiles that do not state a block keep it null.
- Parser fixtures and migrations must add the field before Calendar
  synchronization consumes it.

### Implementation note 2026-07-23

The annual profiles read the block from the first slash-separated segment of the
`DİLİM ADI / ANABİLİM DALI` cell, verbatim, including compound values the source
writes such as `DOKU + HAREKET-1 DİLİMİ`. The practice profile reads it from the
merged block heading above each rotation table. 611 of 901 Turkish and 547 of 953
English annual candidates state one.

Replacing the raw block cell in the content hash with the split fields changed
every content hash. That was done deliberately now, while no student calendar
exists: after launch the same change would patch every managed event for every
user.

---

## ADR-048: Derive supported profile selectors from academic-year fixtures

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** partially in the source catalog

### Context

Exact class/language group combinations were left open even though the committed
faculty workbooks contain the values. Guessing them is unsafe, but fixture
inspection can make the server-owned selector schema evidence-based.

### Decision

For each academic year, derive the supported selector matrix from the committed
or captured source fixture and record it in the source catalog with a fixture
test. Values from an older academic year are evidence for parser design but do
not become the current year's validation allowlist without a current capture.

The currently confirmed matrix is:

- Grade 1 Turkish practice: groups `A`-`H`, subgroups `A1`-`H2`
- Grade 1 English practice: group `İ`, subgroups `İ1`, `İ2`, `İ3`
- Grade 2 Turkish practice: groups `A`-`H`, no subgroup values in the fixture
- Grade 1 and Grade 2 anatomy: independent groups `1`, `2`, `3`
- Grade 3 Turkish curriculum sources: curriculum groups `A` and `B`

The available Grade 2 English fixture states `İ1` and `İ2` but belongs to
2024-2025, so those values remain provisional until a current fixture is
captured.

### Consequences

- Unknown-selector validation can be enabled source by source as evidence is
  confirmed.
- A new cohort in a future workbook is held for review rather than silently
  accepted.
- Fixture refresh is part of each academic-year rollover.

---

## ADR-049: A department is read only from an explicit marker, and all of them are kept

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** shared parser primitive, parser contract, canonical domain model,
persistence and both implemented Grade 1 profiles
**Relates to:** ADR-035 as amended, ADR-047

### Context

`Department` existed on the canonical record and was a precondition for ADR-035
secondary matching, but nothing could ever populate it: the parser contract had
no such field, so every record ever produced had it null. The whole
secondary-matching path was therefore unreachable in production while passing its
own unit tests, which construct records directly.

The sources do state the department. The Grade 1 Turkish annual header is
`DİLİM ADI / ANABİLİM DALI` — block name, then owning department — and the
English workbook uses the same convention under a generic `Description` header
with Turkish department names.

Splitting that cell on the slash would fabricate departments. The sources also
write a second curriculum block, a nested block name or a whole faculty after it:
`YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ / DİKEY KORİDOR` names no department, and
`HAYATIN EVRELERİ DİLİMİ / DİŞ HEKİMLİĞİ FAKÜLTESİ` names a faculty. A naive
split would have invented a department for 29 Turkish and 10 English rows.

### Decision

A slash-separated segment becomes a department only when it carries an explicit
marker: `AD.`, `A.D.` or `ANABİLİM DALI`, compared on the folded comparison key,
optionally followed by a parenthesised sub-department. The sub-department is kept
verbatim — `İÇ HASTALIKLARI AD. (ENDOKRİNOLOJİ BD.)` is one department, because
that is what the cell states.

The first segment is the curriculum block (ADR-047), unless it carries a marker
itself, in which case the cell states a department and no block.

The canonical record carries **every** department the source names, in source
order, as a required list that is empty when none was named. An integrated
session ("entegre oturum") is taught by several and a student must see all of
them.

Within a segment that carries at least one marker, the source is enumerating
departments, so an unmarked member of that dashed list is kept too, at reduced
confidence with a confidence indicator naming the rule. That is a rule about the
list the source wrote, not an inference about the words in it. It affects two
candidates per annual source today and is what keeps `İÇ HASTALIKLARI HEMATOLOJİ`
visible in `BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD. - İÇ HASTALIKLARI HEMATOLOJİ`.

A segment with no marker anywhere in it is never a department. It is counted in
`departments.ignored.unmarkedSegment` and reported once per distinct wording as
an informational finding with the address of the first cell that carries it, so
widening the rule later starts from evidence.

Departments are content, not identity: a corrected department updates the existing
logical lesson.

### Consequences

- Secondary matching can now actually use a department, and ADR-035's amended
  two-tier rule decides when it is comparable.
- A department the source states without a marker — `TIBBİ EKOLOJİ VE
  HİDROKLİMATOLOJİ` — is deliberately not published. Publishing it would make the
  rule depend on knowing Turkish faculty structure rather than on the cell.
- The practice profile states no department: its columns are practice subjects,
  and reading one as a department would be an inference.
- The single `Department` column is replaced by a `Departments` JSONB list. The
  migration carries any existing single value into the list before dropping the
  column, even though no row can hold one.
- A future profile whose source marks departments differently adds an alias to
  this rule with its fixture, and does not weaken the marker requirement.

---

## ADR-050: A parse run left running past a timeout is recovered, not abandoned

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** domain boundary, persistence path, worker logging and
configuration

### Context

A parse run is keyed by snapshot, parser profile and profile version, and
`Resume` accepted only a `Failed` run. A worker killed mid-parse — a deploy, a
crash, a lost host — therefore left the run `Running` forever with no lease,
heartbeat or timeout anywhere. Every later cycle reported `ParseAlreadyRunning`
for that snapshot and did nothing.

Because the key includes the snapshot, the source recovered only when its content
next changed. A schedule change captured in the wedged snapshot was silently never
published until the faculty edited the workbook again, which can be days. That
violates the rule that every background job must be safe to retry.

### Decision

A run is stale when it is `Running` and has been open at least
`SIRKADIYEN_PARSER__STALE_RUN_TIMEOUT`, default 30 minutes. A stale run is
recovered in place: same run, new correlation ID, `AttemptCount` incremented, and
`LastStaleRecoveryAtUtc` recorded and never cleared by a later resume.

Recovery reuses the run rather than creating a second one. The run is the logical
execution of one profile version against one snapshot, and a duplicate would break
that uniqueness and could produce a second revision for the same snapshot.

The timeout is the caller's policy, passed into the store, not a constant inside
it. It must stay well above the parser transport timeout.

Recovery is not silent: the poll result carries `ParseRunStartKind.RecoveredStale`
and the worker logs a warning, because a recovered run says something about the
host rather than about the source.

### Consequences

- A killed worker costs one cycle, not an unbounded wait for the source to change.
- If the original worker was merely slow rather than dead and answers after
  recovery, its response is rejected because the correlation ID no longer matches
  the run. No duplicate revision can be created; the attempt is wasted and looks
  like a transport fault, which is why the default timeout leaves a wide margin.
- Two workers recovering the same run at once may both increment the attempt
  count and both call the parser. Only one response can complete the run. There
  is no row version on `parse_runs` yet; the worker processes sources sequentially
  and avoids overlapping cycles, so this needs a second worker instance to occur.
- A run that failed and was recorded as failed keeps using the existing resume
  path, and is never treated as stale.

---

## ADR-051: A parser profile declares the component order of a numeric date

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** `normalization/dates.py`, the profile registry, both implemented
profiles, `GET /v1/profiles`

### Context

The shared date resolver read every numeric date as day-first. The assumption was
never declared anywhere and no source had been shown to need it. It is the one
resolver rule that can be wrong without producing any evidence: `25/12/2026` has
a single possible meaning, but `05/06/2026` is a real date under either reading,
so a month-first source would publish lessons a few months from where they belong
on every date whose components are both twelve or lower, and refuse nothing.

Every other reading rule in the resolver already fails loudly. A bare serial, a
missing year and a compact time all stay unresolved unless the caller opts in
(ADR-011). Numeric order was the exception, and it was the most dangerous one.

Measured evidence: no numeric date exists in any committed fixture. The two annual
sources and the practice source write dates as spreadsheet serials or as text
naming the month — 896 serial and 5 month-name dates in Grade 1 Turkish annual,
953 serial in Grade 1 English annual, 60 serial and 100 month-name rotation rows
in Grade 1 Turkish practice. The day-first branch was dead code against every
source we hold, which is why nothing had failed and why the fix is cheap now.

### Decision

`ParserProfileDefinition` carries a required `numeric_date_order`, one of
`dayFirst`, `monthFirst` or `undeclared`. The field has no default: a profile
added without considering its source's date layout must not inherit an order.

Both readings are computed before anything is decided, and what the alternative
yields is treated as evidence:

- a declared order reads as declared. A cell that only the other order can explain
  is refused as `numericDateImpossibleUnderDeclaredOrder`, because either the cell
  or the declaration is wrong and silently reordering it would hide both.
- an undeclared profile publishes a numeric date only when the order cannot change
  the answer — when just one order names a real date, or when the components are
  equal. That is arithmetic, not an inference: the result is identical under either
  declaration, so no rule is being guessed.
- an undeclared profile refuses a date that means two different things, as
  `numericDateOrderNotDeclaredByProfile`, with the row unpublished and the cell
  cited.

Every profile declares `undeclared` today, because that is what the fixtures
support. Declaring an order is a claim about a document, so it waits for a document
that shows one.

The declaration lives on the parser profile rather than in the source catalog or
`sourceContext`. It describes a document family's layout, which is exactly what a
profile is; putting it in the .NET catalog would move a spreadsheet-interpretation
rule outside the parser boundary.

`dates.rule.<rule>` is now counted per published date, so a source that changes
how it writes dates is visible in the metrics of the first parse after the change
rather than only in a reader's attention.

### Consequences

- A month-first source can no longer be silently misparsed. Its rows are refused
  with the cell address until a profile declares `monthFirst`.
- One profile serves the Turkish and English workbooks of a family, so if those
  two ever disagree on date order the family needs two profile versions. That is
  the correct pressure: the declaration is about the document.
- An undeclared profile meeting a numeric source publishes the unambiguous part of
  it and refuses the rest. The parse is `completedWithWarnings`, and the warnings
  name the cells the declaration would fix.
- Golden output for all three fixtures is unchanged except for the new metrics, so
  no content hash moved and no calendar event would be touched by this change.
- `resolve_date_text` and `resolve_cell_date` default to `undeclared`, so a future
  caller that forgets to pass the profile's declaration refuses ambiguity rather
  than inheriting day-first.

---

## ADR-052: Exchange a verified Google ID credential for a local cookie session

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** user domain/persistence, Google sign-in API, secure cookie,
anti-forgery protection and SuperAdmin authorization
**Relates to:** ADR-003, ADR-023, ADR-045

### Context

ADR-003 selected Google-only authentication and ADR-023 selected a
backend-managed cookie, but the browser-to-backend Google exchange was still
open. A redirect handler would work, but the planned frontend already owns the
Google sign-in entry point and Google Identity Services can return a short-lived
ID credential without granting Calendar or other Google API access.

Sign-in identity must also remain separate from the later incremental Calendar
authorization. Persisting an access or refresh token during login would couple
the two and broaden the first consent unnecessarily.

### Decision

The frontend obtains a Google Identity Services ID credential and posts it once
to the same-site API. The request is anti-forgery protected. The backend uses
`Google.Apis.Auth` to validate the Google signature, issuer, configured client-ID
audience, expiry and `email_verified`. It persists only the immutable `sub`,
verified email/profile fields and the local role, then discards the credential
and issues the ADR-023 application cookie.

The browser client ID is required as
`SIRKADIYEN_GOOGLE__AUTH_CLIENT_ID`. It is intentionally separate from
unattended source-access credentials. Google Calendar scopes and refresh-token
storage remain a later, separate authorization flow.

The session cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, host-prefixed and has
an eight-hour sliding expiry. State-changing browser endpoints carry explicit
anti-forgery metadata. The API reloads the local user for every authenticated
request; a missing user invalidates the session and changed role/profile claims
renew it. Google sign-in is rate-limited per remote address; a proxy deployment
must configure trusted forwarded headers before relying on that address as the
internet client.

The `users` table gives Google subject and normalized email independent unique
constraints. A second subject with the same email is rejected rather than
silently linked. Concurrent first callbacks for the same subject converge on the
unique winner and retry as an update.

### Consequences

- Normal sign-in grants no Calendar access and stores no Google credential.
- Same-site HTTPS is the supported browser topology for this foundation. A later
  cross-site frontend deployment must explicitly revisit CORS, credentialed
  requests and SameSite policy rather than weakening cookies implicitly.
- The frontend must first fetch `/api/auth/csrf`, then send its request token with
  the Google credential.
- Revoking Google API access will not terminate a local session; account/session
  revocation is a separate local concern to add with licensing and suspension.
- A containerized or multi-instance deployment must configure a shared persistent
  ASP.NET Core Data Protection key ring; the host default does not guarantee
  cookie continuity across restarts or instances.
- The old shared admin key is removed. Revision approval and diff release now
  require the persisted `SuperAdmin` role and derive their actor from the
  verified session.

---

## ADR-053: Store keyed license hashes and derive activation onboarding

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** license domain/persistence, administration and redemption APIs,
rate limiting, audits, onboarding state and PostgreSQL concurrency tests
**Relates to:** ADR-004, ADR-022, ADR-045, ADR-052

### Context

The product requires administrator-issued, single-use activation codes. A code
must be looked up during redemption without retaining plaintext, while ordinary
fast hashes of a human-entered value allow cheap offline guessing after a
database leak. The same user can also submit two codes concurrently, which a
lock on either individual code does not serialize.

Onboarding must resume from backend facts. At this stage licensing exists but
student profiles and Calendar authorization do not, so persisting a client-set
workflow state would invent authority the later modules need to own.

### Decision

Generate 100 random payload bits in a display code prefixed `SIRK`. Return its
plaintext only in the successful creation response. Store a deterministic
HMAC-SHA256 lookup hash keyed by the required Base64
`SIRKADIYEN_LICENSING__HASH_KEY`, never the code itself.

Licenses use explicit `Active`, `Redeemed`, `Revoked`, and `Expired` states. An
optional expiration is an unused-code redemption deadline; it does not turn a
completed activation off later. Redemption locks the license row. A partial
unique index on `RedeemedByUserId` where status is `Redeemed` also permits at
most one current activation per user, so two different codes cannot both win a
same-user race. Revoked history leaves that index and remains auditable.

Creation, redemption, expiration discovered during redemption, and revocation
append an audit row in the same transaction. Redemption is idempotent only for
the winning user and code. The public failure does not distinguish unknown,
expired, revoked, or another user's code and is rate-limited by authenticated
user plus remote address.

Derive onboarding as follows until later authoritative records exist:

```text
no redeemed license        -> LicenseRequired
redeemed license           -> ProfileRequired
redeemed then revoked      -> Suspended
```

### Consequences

- Two users racing one code and one user racing two codes each produce exactly
  one winner under real PostgreSQL.
- Rotating the HMAC key invalidates lookup for every unredeemed code. Key
  rotation is therefore an explicit invalidate-and-reissue operation.
- A revoked user may later receive a new license without deleting historical
  redemption and revocation records.
- Student profile, Calendar permission and sync modules extend onboarding from
  their own records; the browser never submits a trusted onboarding state.

---

## ADR-054: Generate short human-friendly codes and support manual activation

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Amends:** ADR-004 and the generated-code format in ADR-053
**Implements:** compact code generation, legacy-code redemption, explicit
license kind, audited SuperAdmin manual activation, additive migration and
PostgreSQL concurrency tests

### Context

The first generated format, `SIRK-XXXXX-XXXXX-XXXXX-XXXXX`, carried 100 random
bits but was unnecessarily long for the actual distribution channel. Codes are
sent to students through WhatsApp, where quick copying, reading and occasional
manual typing matter more than carrying entropy far beyond the online attack
surface.

The future administration panel also needs to activate a user directly. Creating
an undisclosed fake code for that action would make the record lie about how
activation happened and would leave an unredeemable hash with no operational
meaning.

### Decision

Generate new codes as `SRK-XXXXX-XXXXX`. The ten random characters use the
32-character alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, omitting `I`, `O`,
`0`, and `1`. This carries 50 random bits. HMAC lookup, the five-per-minute
user/address redemption limit, unique hash constraint, and collision retry from
ADR-053 remain unchanged. Long `SIRK` codes already generated remain redeemable;
only generation changes.

Add explicit `LicenseKind` values `Code` and `Manual`. A `Code` license requires
a 32-byte keyed hash. A `Manual` license requires no hash, starts in `Redeemed`,
names the target user, and writes one `ManuallyActivated` audit with the
SuperAdmin actor and mandatory reason.

Manual activation and code redemption use the same partial unique index allowing
only one current `Redeemed` license per user. The manual endpoint is idempotent,
CSRF-protected, and restricted to `SuperAdmin`.

### Consequences

- A code is short enough to share and type comfortably in WhatsApp while online
  brute-force remains impractical under the keyed hash and rate limit.
- At one million generated codes, the approximate probability of at least one
  random collision is 0.044 percent; the unique index and generation retry
  resolve one without exposing the discarded plaintext.
- The admin panel can display whether activation came from a code or a manual
  decision and can show the responsible actor and reason.
- Migration rollback is refused while `Manual` rows exist, because the older
  schema cannot represent an activation without inventing a code hash.

---

## ADR-055: The student profile and its server-owned supported schema

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** the `StudentProfile` aggregate, a code-defined supported-profile
schema and validator, transactional upsert persistence, onboarding advancement to
`CalendarAuthorizationRequired`, the profile API, and unit/PostgreSQL tests
**Depends on:** ADR-027 (validated JSONB selectors), ADR-048 (evidence-based
selector matrix), ADR-052/053 (identity and derived onboarding)

### Context

An activated account could not progress past `ProfileRequired`: no profile module
existed, so audience resolution had nothing to match a schedule change against and
the pipeline had no one to synchronize to. A profile has to record the academic
year, class year and program language, plus variable cohort selectors that differ
by class year, and it must never trust a client's claim about which cohort exists.

Two questions had to be answered. Where does the allowlist of valid cohorts live,
and how are the group/subgroup dependency and the required/optional distinction
expressed — neither of which the source catalog's flat
`supportedAudienceSelectors` map carries.

### Decision

Model a `StudentProfile` aggregate with relational `AcademicYear`, `ClassYear`
and `ProgramLanguage`, and a schema-versioned JSONB selector document keyed by
dimension, each holding the single value the student belongs to (systemPatterns
§22). The aggregate enforces only structural bounds; it never carries the
allowlist, which changes at year rollover.

Define the supported-profile schema as **server-owned code**, not a runtime config
file and not a projection of the source catalog. It covers exactly **one current
academic year** because there is one at a time, and each `(classYear,
programLanguage)` program lists selector dimensions. A dimension is either
independent, with an explicit value list, or dependent, naming a parent and the
child values allowed per parent value (`practiceSubgroup A1` is valid only under
`practiceGroup A`). Only cohorts a committed current-year fixture confirms appear;
Grade 1 anatomy, Grade 2 and Grade 3 selectors are deliberately absent until their
sources are captured (ADR-048). A unit test cross-checks every allowed value
against the catalog's declared selectors, so the schema cannot drift from the
evidence even though it is defined separately.

One validator serves the profile write and, later, audience matching, so a stored
selector is always one the sources publish. The profile write path first requires
an active license (`ActivationRequired` otherwise), enforcing the onboarding order
in the backend rather than the UI. Onboarding now derives
`CalendarAuthorizationRequired` when a profile row exists and `ProfileRequired`
when it does not, so an interrupted onboarding resumes at the right step.

Persistence is a transactional upsert against a `UserId`-unique row; a concurrent
first-time save reruns once as an update rather than failing.

### Consequences

- The supported matrix changes only at year rollover, which is a deployment
  anyway, so code with a fixture-backed test is simpler and safer than a second
  runtime config loader in the API host.
- The frontend renders its form from `GET /api/profile/options`, which returns the
  schema including the parent-keyed child values for dependent dropdowns.
- The one-level dependency model does not express a deeper chain; a future
  rotation-within-subgroup requirement would extend it, and a test asserts the
  current depth so the assumption is visible.
- A profile stored under one schema version is not re-validated when the schema
  changes; re-validation on rollover is future work, tracked as a known risk.

## ADR-056: Store the university student number with layered semantic validation

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** a `StudentNumber` field on the `StudentProfile` aggregate, its
format and cross-validation in `StudentProfileValidator`, the EF column and format
check constraint, migration `AddStudentNumberToProfile`, the read/write DTO fields,
and domain/validator/store tests
**Depends on:** ADR-055 (the student profile and its validator)

### Context

The profile must record the student's university number (Öğrenci Numarası) for
future integrations. The number is not an opaque identifier: its ten digits encode
a faculty code (1–2), a program-language code (3–4), an entry year (5–6) and an
entry sequence (7–10), which lets it cross-validate against the profile the student
is submitting. It is stored as text because the leading zeros are significant and
must never be truncated.

### Decision

Enforce the number in three layers, each owning the rule that belongs to it.

- **Domain** (`StudentProfile`) guards only the structural invariant: a trimmed,
  exactly-ten-character, all-ASCII-digit string. The aggregate can therefore never
  hold a structurally corrupt number, and leading zeros survive because it is text.
- **Application** (`StudentProfileValidator`) owns the semantic cross-validation,
  because these rules depend on business scope and on the row's own program
  language. Digits 1–2 must be `01` (Istanbul Medical Faculty — the project's
  scope); digits 3–4 must match the selected program (`01` Turkish, `02` English).
  Format is re-checked here too so a malformed number returns a clean validation
  error (400) rather than a domain throw, and the digit-slice rules are skipped once
  the number is known to be malformed. New error codes: `InvalidStudentNumber`,
  `StudentNumberFacultyMismatch`, `StudentNumberProgramMismatch`.
- **Database** pins the same structural invariant as a check constraint
  (`"StudentNumber" ~ '^[0-9]{10}$'`), defence in depth. The semantic faculty and
  language rules stay out of the database because a check constraint cannot read the
  row's program language in a way that keeps the two codes in sync as policy evolves.

The migration adds the column as `NOT NULL` with **no** server default: the profile
table is empty at this migration, and an empty string could never satisfy the
format constraint, so a default would be both useless and a latent trap. Verified
Up → Down → Up against real PostgreSQL on an empty table.

### Consequences

- The faculty scope (`01`) and the language-code map are constants in the validator;
  widening scope to another faculty, or adding a program, is a code change with a
  test, consistent with the server-owned schema of ADR-055.
- The entry-year digits (5–6) are **not** cross-checked against the academic year,
  and the number is **not** enforced unique. Both are deliberate omissions recorded
  as known risks; a repeat student or a legitimately shared-prefix cohort must not
  be rejected by a rule we have not confirmed.

---

## ADR-057: Grant Calendar access as a separate, minimally scoped offline authorization

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `GoogleCalendarConnection` aggregate, encrypted refresh-token
storage, the authorization-code exchange and its client abstraction, the Calendar
authorization API, onboarding advancement to `ReadyForInitialSync`, migration
`AddGoogleCalendarConnections`, and unit/PostgreSQL tests
**Depends on:** ADR-003 (Google-only authentication), ADR-024 (one dedicated calendar
per user), ADR-052 (verified credential for a local cookie session)

### Context

ADR-052 deliberately left sign-in free of any Calendar scope or stored Google
credential, noting that Calendar scopes and refresh-token storage were a later,
separate authorization flow. That flow did not exist, so an activated student with a
profile stopped at `CalendarAuthorizationRequired` and nothing could ever be
synchronized. Unattended synchronization needs a long-lived refresh token, which is a
credential the product must hold and protect.

### Decision

Keep the Calendar grant a **separate, second consent**, and model it as its own
`GoogleCalendarConnection` aggregate rather than as fields on the user.

**Flow.** The frontend obtains a one-time authorization code with offline access and
posts it to a same-site, anti-forgery protected `POST /api/calendar/authorization`; the
backend performs the code exchange because it carries the client secret. This mirrors
ADR-052's shape — the frontend owns the Google entry point, the backend does the
sensitive part — so there is no redirect handler and no server-side redirect state. The
exchange `redirect_uri` is configurable and defaults to `postmessage`, the value Google
requires for the browser popup code flow.

**Scope.** Request only
`https://www.googleapis.com/auth/calendar.app.created`, which grants access solely to
calendars this application itself creates. That is exactly ADR-024's dedicated-calendar
model and structurally cannot reach the user's primary calendar. Google reports what was
actually granted, and a user can clear the permission while still completing consent, so
the service verifies the required scope is present and refuses the grant otherwise
rather than storing an authorization that cannot synchronize.

**Credential at rest.** The refresh token is encrypted with ASP.NET Core Data
Protection under a dedicated purpose string. The domain never sees plaintext: the
application layer protects the value through an `ICalendarTokenProtector` abstraction
before the aggregate is constructed, so the aggregate stores opaque ciphertext and the
domain keeps no cryptographic dependency. The read projection deliberately omits the
credential entirely, so no API response can carry it by accident.

**Ordering.** Authorization requires an active license *and* an existing profile,
enforced in the backend exactly as the profile write requires an active license. The
code is not even sent to Google when the account may not connect.

**Boundary.** This is authorization only. Creating the dedicated calendar remains part
of initial sync (ADR-024); the aggregate reserves a nullable `ManagedCalendarId` and a
re-authorization deliberately preserves it, so re-granting access never orphans the
calendar the user's events already live in.

### Consequences

- Onboarding now derives `ReadyForInitialSync` once an authorized connection exists, and
  a connection marked `NeedsReauthorization` does not count as authorized, so a revoked
  grant returns the user to consent instead of stalling in a state that cannot sync.
- The Calendar client is configured separately from both the public sign-in client and
  the unattended source credential, because it is the only one of the three that is a
  confidential client acting for a signed-in student.
- A multi-instance or containerized deployment **must** configure a shared, persistent
  Data Protection key ring. Without one, a host restart makes every stored token
  undecryptable and forces all users to authorize again. The Worker will need the same
  key ring when synchronization consumes the token.
- `Microsoft.AspNetCore.DataProtection` is pinned to the patched servicing release;
  10.0.0 carries a critical advisory and the build audits packages as errors.
- The live consent and exchange cannot be exercised without a registered Web OAuth
  client, so the exchange is abstracted behind an interface and the service is tested
  against a fake. The real client is not covered by automated tests.
- Disconnect, token refresh, and reacting to Google-side revocation are not implemented;
  they belong with synchronization.

## ADR-058: Populate each user's dedicated calendar with a worker-driven, idempotent initial sync

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the connection initial-sync lifecycle (`InitialSyncState`), the
`UserCalendarEventMapping` aggregate, the pure `CalendarAudienceResolver` and
`ManagedCalendarEventFactory`, the `InitialCalendarSyncService` and its resumable worker
stage, the `IUserCalendarClient` abstraction with a real `Google.Apis.Calendar.v3` client,
the `POST`/`GET /api/calendar/sync` endpoints, the shared Data Protection key ring, migration
`AddInitialCalendarSync`, and unit/PostgreSQL tests
**Depends on:** ADR-024 (one dedicated calendar per user), ADR-034/043 (global operational
freeze), ADR-055 (student profile), ADR-057 (Calendar authorization and encrypted refresh
token)

### Context

An activated student with a profile and an authorized `GoogleCalendarConnection` stopped at
`ReadyForInitialSync`. Nothing created their dedicated calendar or wrote any events, so the
whole pipeline — polling to a stored diff — still reached no calendar. Populating a calendar
means creating it (ADR-024), resolving which currently-published events belong to that
student, and writing them safely enough to survive crashes, retries and Google quota.

### Decision

Implement **per-user initial sync** as the first synchronization slice; diff-driven
incremental sync (patch/delete, reconciliation, quota batching) is deferred, but the mapping
and idempotency below are built for it.

**Worker-driven and state-machine-resumable.** The API only records intent:
`POST /api/calendar/sync` moves the connection's `InitialSyncState` `Pending → InProgress`.
The worker does the slow, quota-bound work across cycles, driven by connection state exactly
like the publication and diff stages, so a killed worker resumes from what is not yet mapped.
`InitialSyncState` (`Pending`/`InProgress`/`Completed`) lives on the connection, orthogonal to
the authorization `Status`; a re-authorization preserves both `ManagedCalendarId` and the sync
progress. A separate sync-job aggregate is deliberately avoided for this slice.

**Idempotency by a deterministic event id plus a durable ledger.** Each event is inserted
with a client-chosen id `base32hex(sha256(userId ‖ stableIdentity))`, whose alphabet is
exactly Google's allowed id set, so a re-insert of the same id returns 409 and is treated as
success. A `UserCalendarEventMapping` row — unique on `(UserId, StableIdentity)`, keyed by the
identity that survives room and instructor changes (ADR-018) — is the ledger of what has been
written; the worker inserts only records with no mapping. Together they survive a crash
between the Google write and the local commit without creating a duplicate.

**Affected-events resolution is a pure function.** A published record applies to a student
when academic year, class year and program language match and either it is program-wide, or it
is cohort-scoped and at least one of its `{Dimension,Value}` audience selectors equals one of
the profile's selectors; a cancelled record never applies. The set is filtered in memory over a
"current published records for this program" query (the records of each source's `Published`
revision), because the per-class-year volume is bounded and the rule is worth unit-testing
without a database.

**Safety and secrecy.** Initial sync reads the global operational freeze and does nothing
while frozen (ADR-034/043), like every other calendar job. The refresh token is decrypted only
in memory for the duration of a sync call, through the same `ICalendarTokenProtector`.

**Shared Data Protection key ring.** The worker must decrypt what the API encrypted, but Data
Protection isolates key rings by application name (defaulted to the content root) and a generic
host configures none. Both hosts now call `AddSirkadiyenDataProtection`, pinning one
application name and a configurable file-system key location.

### Consequences

- Onboarding derives `InitialSyncInProgress` while the worker runs and `Active` once every
  applicable event is written; a large first load spans several cycles under a per-user,
  per-cycle event budget, so the state is a real progress signal, not instantaneous.
- The credential-bearing pending-sync projection is separate from the read `View`, which still
  omits the token, so only the backend sync path ever sees ciphertext and nothing else can leak
  it.
- Calendar **creation** is the one non-idempotent Google step. The id is persisted immediately
  after creation to shrink the window, but a crash in that window could orphan a calendar and
  create a second on resume; a marker-tagged lookup is deferred with reconciliation.
- The real `Google.Apis.Calendar.v3` client (`1.75.0.4206`, audit-clean) is not covered by
  automated tests; only the fake `IUserCalendarClient` is exercised, so the first live sync is
  the first real exercise of that path.
- A single-host deployment shares the key ring correctly; multi-instance production still needs
  genuinely shared, backed-up key storage (carried forward from ADR-052/057).
- Migration `AddInitialCalendarSync` defaults `InitialSyncState` to `Pending` rather than EF's
  empty string, which would violate the state check constraint.
- `IGoogleCalendarConnectionStore.IsAuthorizedForUserAsync` was removed: onboarding reasons
  over the connection view's `Status` and `InitialSyncState`, so the boolean query was
  redundant.

## ADR-059: Fan each dispatchable diff out onto calendars with a resumable, idempotent dispatcher

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `ScheduleDiff` dispatch lifecycle (`CalendarDispatchState`, `DispatchAttempts`,
`NextAttemptAtUtc`, `DispatchedAtUtc`, `DispatchFailureReason`, `MarkDispatched`,
`RecordDispatchFailure`), `GoogleCalendarConnection.MarkNeedsReauthorization`, the pure
`IncrementalSyncPlanner`, the `IncrementalCalendarSyncService` and its resumable worker stage, the
reverse-lookup / update / remove mapping-store methods, `ICanonicalScheduleReadStore.ListRecordsByIds`,
the new `ICalendarSyncTargetReadStore`, `IUserCalendarClient` patch/delete plus the transient/credential
exception taxonomy and the real client's bounded back-off, migration `AddCalendarDispatch`, and
unit/PostgreSQL tests
**Depends on:** ADR-018 (identity vs content), ADR-033/042 (forward-fix, diff hold/release), ADR-034/043
(operational freeze), ADR-058 (initial sync, the mapping ledger and idempotency this reuses)

### Context

After initial sync marks a connection `Completed`, nothing kept that student's calendar in step with
the schedule. The worker already calculated a `ScheduleDiff` (`Ready`/`Released`) per republication, but
the diff was only stored and logged — it never reached a calendar. A lesson that moved, was cancelled, or
was added silently never updated for a synchronized student. AI_GUIDELINE §13 makes the diff the sole
authority for deletion ("a published revision and a valid semantic diff"), so the fix must be
diff-driven, not a level-triggered reconcile against current truth.

### Decision

Implement **diff-driven incremental sync**: a worker stage that dispatches each dispatchable diff into
per-user insert/patch/delete operations. Scope is the core plus a failure/backoff taxonomy; a
reconciliation sweep and global quota-aware batching are deferred.

**The diff carries its own dispatch lifecycle; no separate sync-job aggregate.** Mirroring ADR-058, the
`ScheduleDiff` gains `CalendarDispatchState` (`Pending → Dispatched`, terminal `Failed`) plus retry
fields. A diff is eligible when it is `IsDispatchable` (Ready/Released), still `Pending`, and past any
back-off time. This is "creating sync jobs from a diff" (AI_GUIDELINE §14) modelled on the existing
authoritative row rather than a construct incremental and reconcile would both have to own.

**Coarse per-diff tracking is safe because fine-grained idempotency lives in the ledger.** A diff's
fan-out over many users is not atomic. Rather than a per-`(diff,user)` table, a worker killed mid-fan-out
re-runs the whole diff; each per-user operation converges — insert → deterministic id → 409
`AlreadyExists` + ledger `AlreadyPresent`; patch → skipped when the ledger hash already equals the target
(Google patch is idempotent regardless); delete → event already gone (404/410) + mapping already removed.
The diff is marked `Dispatched` only after the fan-out completes with no transient failure.

**The mapping ledger is authoritative for who holds a lesson; audience decides only insertions.** Per
entry, using the referenced canonical record(s): a **Deleted** record → delete for every ledger holder; a
**Created**/**Updated** record → for existing holders, patch when content moved, delete when it no longer
applies (audience narrowed or the record is `Cancelled`, both of which `CalendarAudienceResolver.Applies`
already rejects), and for cohort users with no mapping, insert when it applies (audience widened). The
per-`(user, entry)` decision is the pure `IncrementalSyncPlanner`; the fan-out (which users) is store
queries.

**The initial/incremental boundary needs no special handling.** Initial sync captures current published
state and records each content hash, so re-dispatching a diff whose revision predates a user's completion
is a no-op for them. Only `Completed` users are dispatched to; users still `InProgress` reach the same
final state through initial sync.

**Failure taxonomy at two levels.** The real client classifies Google failures and retries transient ones
(429/5xx/network) with bounded exponential back-off inside the call, then raises a typed transient
exception. The service defers the diff with a growing back-off (`RecordDispatchFailure`), giving up to
`Failed` after `MaxDispatchAttempts`. A dead credential (`invalid_grant`/401/403) instead flags the
connection `NeedsReauthorization`, skips that user, and leaves their events — it does not count against
the diff or block other users.

### Consequences

- A held diff is never auto-dispatched (it is not dispatchable), so the diff safety gate (ADR-033/042)
  also bounds dispatched fan-out size: a mass deletion is held and only a named operator's release turns
  it into deletions.
- A user set `NeedsReauthorization` during a dispatch and later re-authorized does not automatically
  catch up on diffs marked `Dispatched` while they were dead — that repair is the deferred reconciliation
  sweep. Nothing here silently deletes their events.
- Fan-out over a cohort runs to completion within a cycle; only the number of diffs per cycle is bounded.
  In practice Ready diffs are small, so this holds; a large *Released* diff over a big cohort runs long
  until intra-diff quota batching lands.
- A patch that finds no event (404) re-inserts it: the ledger says the event should exist, so the calendar
  is made to match rather than left divergent (idempotent via the deterministic id).
- The real client's patch/delete, token refresh, and back-off classification remain untested without live
  Google; only the fake `IUserCalendarClient` is exercised.
- Migration `AddCalendarDispatch` defaults `CalendarDispatchState` to `Pending` rather than EF's empty
  string (which would violate the state check constraint); existing Ready/Released diffs become
  dispatch-eligible, which is correct and idempotent since no completed-sync user exists yet.
- `GoogleCalendarConnectionStatus.NeedsReauthorization` now has a producer for the first time; a
  distinct `GoogleCalendarCredentialException` was added to avoid clashing with the ADR-057
  authorization-code-exchange `GoogleCalendarAuthorizationException`.

## ADR-060: Preserve a durable semantic-diff replay cursor across Calendar re-authorization

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `GoogleCalendarConnection` reconciliation boundary/cursor state,
`PendingCalendarReconciliation`, connection-store list/advance/complete operations,
migration `AddCalendarReconciliationCursor`, the freeze-gated
`CalendarReconciliationService`, ordered dispatched-diff replay query, Worker stage, and
mocked Calendar/PostgreSQL tests
**Depends on:** ADR-033/042 (forward-fix and diff release), ADR-034/043 (global freeze),
ADR-058 (completed initial sync and managed calendar), ADR-059 (credential failure and
diff dispatch lifecycle)

### Context

ADR-059 deliberately lets one dead credential be skipped so it cannot block a global
diff fan-out. The diff is still marked `Dispatched`; after that user authorizes again,
the normal edge-triggered dispatcher will never revisit the missed diff. Rebuilding
state by comparing only the current schedule to the mapping ledger would make deletion
level-triggered and violate AI_GUIDELINE section 13, which requires a published revision
and valid semantic diff.

### Decision

Start reconciliation with a **durable per-connection missed-diff boundary and ordered
cursor**. On the first credential failure after initial sync has completed,
`GoogleCalendarConnection.MarkNeedsReauthorization` stores:

```text
ReconciliationRequiredSinceUtc = failure time
ReconciliationCursorDispatchedAtUtc = failure time
ReconciliationCursorDiffId = Guid.Empty
```

Repeated failures do not move the boundary forward. Re-authorization replaces the
credential and restores `Authorized` while preserving the tuple. A credential failure
before initial sync completes creates no reconciliation request, because the resumable
initial sync remains the authoritative route to current state.

The worker-facing queue returns only connections that are `Authorized`, initial-sync
`Completed`, attached to a managed calendar, and have a complete cursor tuple. Replay
ordering is `(DispatchedAtUtc, DiffId)` because one dispatcher pass can give several
diffs the same timestamp. Cursor advancement is monotonic. Both advancement and
completion require the original `ReconciliationRequiredSinceUtc` as an optimistic
workflow token, so stale work cannot mutate a newer request.

The replay worker will consume only `Ready`/`Released` diffs whose
`CalendarDispatchState` is `Dispatched`, after the cursor. It will apply those entries
for the one user using the same mapping ledger and idempotent Calendar operations as
incremental sync. In particular, a reconciliation delete must be traced to a replayed
semantic diff; current-state absence alone never authorizes deletion.

### Consequences

- A re-authorized user is durably discoverable for catch-up even though the global diff
  was already marked dispatched.
- The database requires the three cursor fields to be all null or all populated; a
  populated cursor also requires completed initial sync, a managed calendar, and an
  ordered timestamp.
- The ciphertext credential appears only in the backend-only pending-work projection,
  like initial and incremental synchronization.
- Missed diffs are now enumerated and applied by the freeze-gated replay worker. The
  cursor advances after each complete diff and an empty scan completes the request; a
  Calendar/store failure preserves the current cursor for idempotent retry.
- Actual Google inventory comparison, duplicate/missing-event repair, and marker-based
  orphan-calendar recovery remain later reconciliation work.

## ADR-061: Preserve the Google event ID when a secondary match changes stable identity

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** atomic `UserCalendarEventMapping.Reidentify`, mapping-store transition,
ledger-targeted patching in incremental dispatch and reconciliation, and unit/PostgreSQL
regression tests
**Depends on:** ADR-018 (start time is part of stable identity), ADR-035 (secondary
matching recognizes a moved lesson), ADR-058/059 (deterministic event IDs and ledger),
ADR-060 (ordered replay)

### Context

A lesson moved to another start time changes its stable identity (ADR-018), while the
semantic diff correctly classifies it as one `Updated` entry through secondary matching
(ADR-035). Calendar patching nevertheless re-derived the deterministic Google event ID
from the **new** identity, and the mapping lookup considered only that new identity. A
healthy user holding the previous mapping could therefore receive a new event while the
old event and ledger row remained, contradicting the decision to update in place. Replay
would have inherited the same defect.

### Decision

For a secondary-matched update, treat the durable mapping as the patch target:

1. load the user's mapping under the previous stable identity;
2. patch its stored `GoogleEventId` with the current canonical content and private
   properties;
3. after the external write succeeds, atomically change the mapping's stable identity,
   canonical record ID and content hash while preserving its Google calendar/event IDs.

The transition is idempotent: finding only the current identity is
`AlreadyReidentified`. Finding neither identity, finding both identities, or finding an
identity owned by another source is not guessed into a merge; the diff/reconciliation
cursor remains unadvanced for operator-visible retry or repair.

Every later patch also targets the ledger's stored Google event ID rather than deriving
one again. This matters because a reidentified mapping deliberately retains the ID
created from its original identity.

### Consequences

- A time move patches one existing Google event instead of creating a duplicate.
- A crash before the mapping commit repeats the same patch against the same Google event;
  a crash after it sees the idempotent current identity.
- The mapping's unique `(UserId, StableIdentity)` constraint continues to prevent two
  ledger rows for the same current lesson.
- A pre-existing both-sides conflict is left untouched. Removing an extra Google event
  belongs to the later inventory reconciliation sweep and must not be inferred here.

## ADR-062: Reconcile Calendar inventory as a non-destructive three-way repair

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `CalendarInventoryReconciliationService`, inventory target scheduling,
managed-event enumeration/comparison, connection inventory health state,
`AddCalendarInventoryReconciliation` migration, worker stage, and unit/PostgreSQL tests
**Depends on:** ADR-033/042 (forward-fix and release authority), ADR-034/043 (global
freeze), ADR-058/059 (managed event identity and ledger), ADR-060/061 (ordered replay
and identity-preserving patch)

### Context

Semantic replay repairs changes missed while a credential was dead, but it cannot find
out-of-band drift: a user may delete or edit a managed event, a process may write Google
successfully and die before committing its ledger row, or local metadata may become stale.
Conversely, treating every object absent from current published truth as removable would
bypass the semantic-diff hold/release safety boundary.

### Decision

Run a periodic, freeze-gated three-way inventory over:

1. current published records applicable to the user;
2. all of the user's durable event mappings;
3. actual Google events carrying Sirkadiyen private markers.

Repair only positive expected state. Recreate a missing mapped event with its ledger event
ID; adopt and patch one exact unledgered marked event; otherwise insert the deterministic
event and create its ledger row. Patch visible or private-property drift even when the
stored content hash is current, and update stale canonical IDs or hashes in the ledger.

Do not delete from inventory. A mapping without a current expected record, a duplicate or
unexpected marked event, and an identity/source conflict are counted and preserved for
operator-visible handling. Only a `Deleted` entry or a no-longer-applicable `Updated` entry
from a valid published dispatchable semantic diff authorizes deletion.

Persist `LastCalendarInventoryAtUtc` for cadence and
`ManagedCalendarUnavailableAtUtc` when the attached calendar returns unavailable. The latter
removes the connection from writers and makes onboarding report `ActionRequired`.

### Consequences

- Manual deletion and visible edits to an expected managed event converge on the next
  inventory without waiting for that lesson's next source change.
- A crash between the Google write and ledger commit can be repaired without creating a
  second event.
- Ambiguous external state stays intact; cleanup requires an explicit audited design and
  cannot silently inherit reconciliation authority.
- Successful inventories, including those reporting conflicts, advance the cadence.
  Transient failures retain the due state and currently retry on the next worker cycle.
- Automatic recreation of a deleted whole calendar is not part of this decision.

## ADR-063: Recover one marker-matched orphan calendar before creating another

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `ManagedCalendarIdentity`, Calendar description marker, CalendarList lookup,
minimal additional OAuth scope, initial-sync recovery policy, and mocked tests
**Depends on:** ADR-024 (one dedicated calendar), ADR-057 (separate least-privilege consent),
ADR-058 (resumable initial sync)

### Context

Google Calendar creation has no client-supplied idempotency key. If a worker creates the
dedicated calendar and dies before persisting `ManagedCalendarId`, retrying creation can
orphan the first calendar and provision a duplicate.

### Decision

Write an exact, versioned, per-user marker to the created calendar's description. When an
initial-sync connection has no stored calendar ID, list the user's owned CalendarList
entries with that exact marker and validate candidates by reading Sirkadiyen-marked events
through the app-created grant.

- exactly one accessible candidate: attach and resume;
- zero candidates: create a new marked calendar and persist its ID immediately;
- multiple candidates: stop safely and do not guess.

Retain `calendar.app.created` for all calendar/event mutations and add only
`calendar.calendarlist.readonly` for the lookup. Authorization verifies both granted scopes.
An older grant lacking the new scope can continue using an already-attached calendar; if
orphan lookup is required, the credential is marked for re-authorization.

### Consequences

- The create/commit crash window converges to the original calendar instead of duplicating it.
- Existing unrelated and primary calendars remain outside mutation scope.
- Exact-marker ambiguity is visible rather than resolved destructively.
- The real CalendarList behavior still needs a live-consent smoke test.

## ADR-064: Fence dispatch and reconciliation across worker instances

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `ICalendarDispatchReconciliationFence`,
`PostgresCalendarDispatchReconciliationFence`, worker orchestration, DI registration, and
PostgreSQL exclusion test
**Depends on:** ADR-059 (global dispatch), ADR-060 (empty scan completes catch-up), ADR-062
(periodic inventory)

### Context

In one worker process, dispatch precedes replay. With multiple workers, one process could
complete a user's replay after an empty scan while another process is concurrently
dispatching a diff that already skipped that user. Inventory could also inspect an
intermediate dispatch/replay state.

### Decision

Serialize the Calendar maintenance critical section with a PostgreSQL session advisory lock.
Acquire it non-blockingly on a dedicated connection and keep that connection alive across:

```text
global Ready/Released diff dispatch
-> per-user dispatched-diff replay, including empty-scan completion
-> due Calendar/ledger inventory
```

A worker that cannot acquire the lock yields that section for the cycle. Disposal explicitly
unlocks; connection loss also releases the session lock. Initial sync remains outside because
it does not participate in the completed-user dispatch/replay ordering proof.

### Consequences

- The empty replay scan is a valid cross-process completion proof.
- External Calendar calls are not wrapped in a long database transaction.
- Only one worker performs Calendar maintenance at a time; throughput remains bounded until
  the work is partitioned with a stronger per-partition protocol.
- Any future entry point that invokes dispatch, replay, or inventory must go through the same
  fenced coordinator.

## ADR-065: Bound one diff's fan-out with the existing idempotency ledger

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** per-diff Calendar mutation budget, `PartiallyDispatched` outcome, worker
configuration/logging, and insert/delete resume regression tests
**Depends on:** ADR-058 (durable event mapping), ADR-059 (coarse diff state over fine-grained
idempotency), ADR-064 (single fenced dispatch coordinator)

### Context

`DiffDispatchBatchSize` limits how many diffs a cycle admits, but ADR-059 still fanned every
user affected by one diff to completion. A normal `Ready` diff is small because safety
thresholds hold mass changes, while an operator may legitimately release a large held diff.
That released diff could perform a long, quota-heavy Calendar fan-out in one worker cycle.

A persisted `(diff, entry, user)` cursor appears attractive, but the affected-user set is
derived from live profiles, connection health and mappings. Paginating that mutable set by a
cursor can skip a user who becomes eligible behind the cursor, and a work-item table would
duplicate the fine-grained progress already represented by the mapping ledger.

### Decision

Set `CalendarOperationsPerDiffBatch` (environment key
`SIRKADIYEN_SYNC__CALENDAR_OPERATIONS_PER_DIFF_BATCH`, default 100). Count one per-user
semantic Calendar mutation: insert, patch, delete, or identity-moving patch. When the planner
finds a required mutation after the budget is exhausted:

1. return `PartiallyDispatched`;
2. leave `CalendarDispatchState` as `Pending`;
3. do not increment `DispatchAttempts`, set back-off, or record a failure;
4. replan the immutable diff in a later worker cycle.

Use the existing mapping ledger as the durable checkpoint. A successful insert adds a mapping,
a patch updates its content or stable identity, a delete removes it, and a credential rejection
makes that user ineligible while creating the reconciliation boundary. Those completed units
therefore disappear from the next plan. Only after a full scan finds no additional required
mutation is the diff marked `Dispatched`.

### Consequences

- A large released diff is spread over worker cycles without weakening its publication,
  hold/release or deletion authority.
- Quota yield is observably different from transient failure and never consumes the failure
  retry budget.
- No schema change, dispatch work table or mutable cursor is required.
- The limit bounds semantic mutation units, not exact provider requests. A patch that receives
  not-found may issue one recovery insert in the same unit.
- Each partial pass replans and may enumerate a whole cohort for the current entry; write volume
  is bounded, read/planning pagination is a possible later optimization.

---

## ADR-066: Develop the frontend same-origin behind an HTTPS proxy edge

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `web/` Next.js scaffold (App Router, TypeScript), its
`next.config.mjs` `/api/*` rewrite proxy, a CSRF-aware typed API client, Google
Identity Services sign-in and the Calendar popup code flow, and the onboarding
route gating
**Depends on:** ADR-023 (secure-cookie session), ADR-036 (Next.js), ADR-052
(sign-in boundary), ADR-057 (Calendar authorization)

### Context

The backend session and antiforgery cookies are `__Host-` prefixed, `Secure`, and
`SameSite=Lax`/`Strict` by deliberate design. ADR-052 recorded the constraint that
a cross-site frontend "must explicitly revisit CORS, credentialed requests and
SameSite policy rather than weakening cookies implicitly." Local development must
therefore exercise those cookies faithfully, not disable them.

Running the frontend cross-origin against Kestrel (`http://localhost:3000` →
`http://localhost:5080`) would force a Development-only CORS policy *and* dropping
`Secure`/`__Host-` over plain HTTP, so dev would test a weaker configuration than
production ships.

### Decision

In development the Next.js dev server is the only origin the browser talks to. It
runs over HTTPS (`next dev --experimental-https`) and proxies `/api/:path*` to the
backend server-side (`BACKEND_ORIGIN`, default the HTTP Kestrel URL
`http://localhost:5080`). The browser therefore makes only same-origin requests.

Consequences of the topology, and why the backend needs no change:

- Same-origin means **no CORS** and **no SameSite relaxation**; the cookies work
  unchanged.
- The HTTPS edge means the backend's `Secure` + `__Host-` cookies are accepted and
  stored exactly as in production. Kestrel stays on plain HTTP locally; only the
  edge terminates TLS, mirroring the production reverse proxy in front of Kestrel
  (`CookieSecurePolicy.Always` still emits `Secure` regardless of the Kestrel-side
  scheme).
- Both Google flows are popup/`postMessage`, so only the dev frontend origin
  (`https://localhost:3000`) is added to the OAuth client's Authorized JavaScript
  origins; no localhost redirect URI is registered.

### Consequences

- Dev is faithful to the hardened production cookie configuration; the class of
  bug ADR-052 warned about cannot hide behind a weakened dev config.
- A future genuinely cross-site production topology (separate apex domains) would
  still have to revisit CORS/SameSite explicitly; this ADR only removes the need
  for the *development* environment by keeping it same-origin.
- The frontend consumes string-serialized enums (the API uses
  `JsonStringEnumConverter`) and treats backend onboarding state as authoritative,
  mapping each state to a route rather than deciding activation client-side.
- Production deployment still ships a separate frontend (ADR-036); the same-origin
  guarantee there is provided by the reverse proxy, not by this dev proxy.

### Amendment (2026-07-24): the backend does need a forwarded-scheme change

The original claim above — "why the backend needs no change" — was wrong, and is
corrected here rather than rewritten. The HTTPS edge makes the browser↔edge hop
secure, but Kestrel still receives the proxied request over plain HTTP, so
`Request.IsHttps` is `false`. The antiforgery system has a hard runtime guard
(`CheckSSLConfig`) that throws `The antiforgery system has the configuration value
AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an
SSL request` on the first `GET /api/auth/csrf`. (Cookie *emission* was fine —
`CookieSecurePolicy.Always` writes `Secure` regardless of scheme — which is why the
gap was not obvious until runtime.)

The fix is the same mechanism production requires behind a TLS-terminating reverse
proxy, so it is not a dev-only shim:

- **Frontend:** `web/src/middleware.ts` sets `X-Forwarded-Proto: https` on `/api/*`.
  This is necessary because Next's rewrite proxy forwards `X-Forwarded-Host` but
  **not** `X-Forwarded-Proto` (verified empirically against an echo upstream and in
  `next/dist/server/lib/router-utils/proxy-request.js`, which enables neither
  `xfwd` nor a proto header). Note the file must live at `src/middleware.ts` when a
  `src/` directory is used, not the project root.
- **Backend:** `Program.cs` calls `UseForwardedHeaders` (first in the pipeline) with
  `XForwardedFor | XForwardedProto`. In `Development` it clears
  `KnownIPNetworks`/`KnownProxies` to trust the immediate loopback peer (which can
  present as `::ffff:127.0.0.1` and miss the default known-network entries). A
  production reverse proxy on another host MUST instead be pinned through
  `KnownProxies`/`KnownIPNetworks` (ADR-052) — otherwise the same antiforgery error
  will surface in production.

Verified: `GET /api/auth/csrf` returns `500` without the header and `200` with a
valid token when `X-Forwarded-Proto: https` is present; the middleware injects that
header into the upstream request.

---

## ADR-067: Route a SuperAdmin to the admin panel, not student onboarding

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `routeForUser` role-based routing, the `web/src/app/admin` panel
with the operational-freeze control and SuperAdmin self-activation, and a
role guard
**Depends on:** ADR-045 (SuperAdmin role), ADR-052 (session role claim), ADR-066
(frontend), ADR-034/043 (operational freeze)

### Context

Onboarding state is derived purely from student activation records — license,
profile, Calendar connection (`OnboardingStateService`). Role is not an input, so a
SuperAdmin with no license correctly computes to `LicenseRequired`. The first
frontend routed every signed-in user by onboarding state, so a SuperAdmin was sent
to the license-redemption page and could not reach any operator surface.

Making onboarding itself role-aware would conflate operator identity with student
activation and pollute the student state machine. Letting the frontend fabricate an
"activated" state would violate AI_GUIDELINE §6/§16 (backend is authoritative; the
frontend must not duplicate or fake authorization).

### Decision

Keep onboarding state student-only and unchanged. The frontend decides the *landing
route* from the backend-authoritative session `role`: a `SuperAdmin` lands on
`/admin`, everyone else on their onboarding route (`routeForUser`). This is
navigation, not authorization — every admin API remains enforced by the
`SuperAdmin` policy server-side, and the panel reads/writes only through those
protected endpoints.

The initial `/admin` panel surfaces the one fully-wired admin capability, the
runtime operational freeze (`GET`/`POST /api/operations/freeze`, ADR-034/043), lists
the not-yet-built Phase 10 surfaces, and offers audited SuperAdmin self-activation
(`POST /api/admin/users/{id}/activate`, ADR-053) so the operator can also walk the
student flow with their own account to test synchronization.

### Consequences

- A SuperAdmin is never blocked by the student license gate; a student is never
  routed to the admin panel (a role guard on `/admin` redirects non-admins to their
  onboarding route).
- No backend change was required; the role claim and the freeze/activation
  endpoints already existed.
- Self-activation makes the SuperAdmin a normal activated student from that point,
  so they resume the ordinary onboarding routes — the operator and student roles
  coexist on one account without special-casing the state machine.
- The panel is intentionally minimal; the remaining Phase 10 admin surfaces are
  still to be built behind this same role gate.

---

