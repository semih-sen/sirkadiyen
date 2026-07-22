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
**Implements:** not yet

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

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** semantic diff model and pure matching engine; persistence and
publication orchestration remain

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
